using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Whim.SliceLayout;

/// <summary>
/// A rectangle, and the index of the windows to use.
/// </summary>
/// <param name="Index"></param>
/// <param name="Rectangle"></param>
internal record SliceRectangleItem(int Index, Rectangle<int> Rectangle);

/// <summary>
/// A layout engine that divides the screen into "areas", which correspond to "slices" of a list
/// of windows. This can be used to accomplish a variety of layouts, including master-stack layouts.
/// </summary>
/// <remarks>
/// The layout engine is a tree of <see cref="IArea"/>s. Each <see cref="IArea"/> is either a
/// <see cref="ParentArea"/>, <see cref="SliceArea"/>, or <see cref="OverflowArea"/>.
///
/// Windows are assigned to <see cref="SliceArea"/>s according to the <see cref="SliceArea.Order"/>.
/// </remarks>
public partial record SliceLayoutEngine : ILayoutEngine
{
	private readonly IContext _context;
	private readonly ImmutableList<IWindow> _windows;
	private readonly ImmutableList<IWindow> _minimizedWindows;
	private readonly ISliceLayoutPlugin _plugin;

	/// <inheritdoc />
	public string Name { get; init; } = "Slice";

	/// <inheritdoc />
	public int Count => _windows.Count + _minimizedWindows.Count;

	/// <inheritdoc />
	public LayoutEngineIdentity Identity { get; }

	private const int _cachedWindowStatesScale = 10000;

	/// <summary>
	/// The areas in the tree which contain windows, in order of the <see cref="SliceArea.Order"/>.
	/// The last area is an <see cref="OverflowArea"/>.
	/// </summary>
	private readonly BaseSliceArea[] _windowAreas;

	/// <summary>
	/// Cheekily cache the window states with fake coordinates, to facilitate linear searching.
	///
	/// NOTE: Do not access this directly - instead use <see cref="GetLazyWindowStates"/>
	/// </summary>
	private IWindowState[]? _cachedWindowStates;

	// The following two parent areas are important for equality comparisons. The record will automatically use
	// these in its auto-generated equality method.

	/// <summary>
	/// The raw root area, with no pruning - see <see cref="PrunedRootArea"/>.
	/// </summary>
	public ParentArea RootArea { get; }

	/// <summary>
	/// The root area, with any empty areas pruned.
	/// </summary>
	public ParentArea PrunedRootArea { get; }

	private SliceLayoutEngine(
		SliceLayoutEngine engine,
		ImmutableList<IWindow> windows,
		ImmutableList<IWindow> minimizedWindows
	)
	{
		_context = engine._context;
		_plugin = engine._plugin;
		Identity = engine.Identity;
		Name = engine.Name;

		_windows = windows;
		_minimizedWindows = minimizedWindows;

		(RootArea, _windowAreas) = engine.RootArea.SetStartIndexes();
		PrunedRootArea = RootArea.Prune(_windows.Count);
	}

	private SliceLayoutEngine(
		SliceLayoutEngine engine,
		ParentArea rootArea,
		ImmutableList<IWindow> windows,
		ImmutableList<IWindow> minimizedWindows
	)
	{
		_context = engine._context;
		_plugin = engine._plugin;
		Identity = engine.Identity;
		Name = engine.Name;

		_windows = windows;
		_minimizedWindows = minimizedWindows;

		(RootArea, _windowAreas) = rootArea.SetStartIndexes();
		PrunedRootArea = RootArea.Prune(_windows.Count);
	}

	/// <summary>
	/// Create a new <see cref="SliceLayoutEngine"/>.
	/// </summary>
	/// <param name="context"></param>
	/// <param name="plugin"></param>
	/// <param name="identity"></param>
	/// <param name="rootArea">
	/// The structure of the tree of areas. This must be a <see cref="ParentArea"/>, and should
	/// contain at least one <see cref="SliceArea"/>.
	/// </param>
	public SliceLayoutEngine(
		IContext context,
		ISliceLayoutPlugin plugin,
		LayoutEngineIdentity identity,
		ParentArea rootArea
	)
	{
		_context = context;
		_plugin = plugin;
		Identity = identity;
		_windows = [];
		_minimizedWindows = [];

		(RootArea, _windowAreas) = rootArea.SetStartIndexes();
		PrunedRootArea = RootArea.Prune(_windows.Count);
	}

	/// <inheritdoc />
	public ILayoutEngine AddWindow(IWindow window)
	{
		Logger.Debug($"Adding {window}");

		if (_windows.Contains(window))
		{
			return this;
		}

		return new SliceLayoutEngine(this, _windows.Add(window), _minimizedWindows.Remove(window));
	}

	/// <inheritdoc />
	public ILayoutEngine RemoveWindow(IWindow window)
	{
		Logger.Debug($"Removing {window}");

		ImmutableList<IWindow> windows = _windows.Remove(window);
		ImmutableList<IWindow> minimizedWindows = _minimizedWindows.Remove(window);
		if (windows == _windows && minimizedWindows == _minimizedWindows)
		{
			return this;
		}

		return new SliceLayoutEngine(this, windows, minimizedWindows);
	}

	/// <inheritdoc />
	public bool ContainsWindow(IWindow window)
	{
		Logger.Debug($"Checking if {window} is contained");
		return _windows.Contains(window) || _minimizedWindows.Contains(window);
	}

	/// <inheritdoc />
	public IEnumerable<IWindowState> DoLayout(IRectangle<int> rectangle, IMonitor monitor)
	{
		Logger.Debug(
			$"Doing layout on {rectangle} on {monitor}, with {_windows.Count} windows and {_minimizedWindows.Count} minimized windows"
		);

		IWindowState[] windowStates = DoNormalLayout(rectangle);
		IWindowState[] minimizedWindowStates = DoMinimizedLayout();

		return windowStates.Concat(minimizedWindowStates);
	}

	private IWindowState[] DoNormalLayout(IRectangle<int> rectangle)
	{
		if (_windows.Count == 0)
		{
			return [];
		}

		// Get the rectangles for each window
		SliceRectangleItem[] items = new SliceRectangleItem[_windows.Count];
		PrunedRootArea.DoParentLayout(rectangle, items);

		// Get the window states
		IWindowState[] windowStates = new IWindowState[_windows.Count];
		for (int idx = 0; idx < items.Length; idx++)
		{
			windowStates[idx] = new WindowState()
			{
				Rectangle = items[idx].Rectangle,
				Window = _windows[items[idx].Index],
				WindowSize = WindowSize.Normal,
			};
		}

		return windowStates;
	}

	private IWindowState[] DoMinimizedLayout()
	{
		if (_minimizedWindows.Count == 0)
		{
			return [];
		}

		IWindowState[] minimizedWindowStates = new IWindowState[_minimizedWindows.Count];
		Rectangle<int> minimizedRectangle = new();
		for (int idx = 0; idx < minimizedWindowStates.Length; idx++)
		{
			minimizedWindowStates[idx] = new WindowState()
			{
				Rectangle = minimizedRectangle,
				Window = _minimizedWindows[idx],
				WindowSize = WindowSize.Minimized,
			};
		}

		return minimizedWindowStates;
	}

	/// <inheritdoc />
	public ILayoutEngine FocusWindowInDirection(Direction direction, IWindow window)
	{
		Logger.Debug($"Focusing window in direction {direction} from {window}");
		GetWindowInDirection(direction, window)?.Focus();
		return this;
	}

	/// <inheritdoc />
	public IWindow? GetFirstWindow()
	{
		Logger.Debug($"Getting first window");
		return _windows.Count > 0 ? _windows[0] : null;
	}

	/// <inheritdoc />
	public ILayoutEngine MoveWindowEdgesInDirection(Direction edges, IPoint<double> deltas, IWindow window)
	{
		// See #738
		return this;
	}

	/// <inheritdoc />
	public ILayoutEngine MoveWindowToPoint(IWindow window, IPoint<double> point)
	{
		Logger.Debug($"Moving {window} to {point}");

		int windowIdx = _windows.IndexOf(window);
		SliceLayoutEngine engine = this;

		// If the window is not in the tree, add it.
		if (windowIdx == -1)
		{
			engine = (SliceLayoutEngine)engine.AddWindow(window);
			windowIdx = engine._windows.IndexOf(window);
		}

		if (engine.GetWindowAtPoint(point) is not (int, IWindow) windowAtPoint)
		{
			return this;
		}

		return engine.MoveWindowToIndex(windowIdx, windowAtPoint.Index);
	}

	/// <inheritdoc />
	public ILayoutEngine SwapWindowInDirection(Direction direction, IWindow window)
	{
		Logger.Debug($"Swapping {window} in direction {direction}");

		int windowIdx = _windows.IndexOf(window);
		if (windowIdx == -1)
		{
			Logger.Error($"Window not found");
			return this;
		}

		IWindow? windowInDirection = GetWindowInDirection(direction, window);
		if (windowInDirection == null)
		{
			return this;
		}

		return MoveWindowToIndex(windowIdx, _windows.IndexOf(windowInDirection));
	}

	/// <inheritdoc />
	public ILayoutEngine MinimizeWindowStart(IWindow window)
	{
		Logger.Debug($"Minimizing {window}");

		if (_minimizedWindows.Contains(window))
		{
			return this;
		}

		ImmutableList<IWindow> minimizedWindows = _minimizedWindows.Add(window);
		ImmutableList<IWindow> windows = _windows.Remove(window);

		return new SliceLayoutEngine(this, windows, minimizedWindows);
	}

	/// <inheritdoc />
	public ILayoutEngine MinimizeWindowEnd(IWindow window)
	{
		Logger.Debug($"Minimizing {window}");

		if (_windows.Contains(window))
		{
			return this;
		}

		ImmutableList<IWindow> windows = _windows.Add(window);
		ImmutableList<IWindow> minimizedWindows = _minimizedWindows.Remove(window);

		return new SliceLayoutEngine(this, windows, minimizedWindows);
	}

	/// <inheritdoc />
	public ILayoutEngine PerformCustomAction<T>(LayoutEngineCustomAction<T> action) =>
		action switch
		{
			LayoutEngineCustomAction<IWindow> promoteAction
				when promoteAction.Name == _plugin.PromoteWindowActionName => PromoteWindowInStack(
				promoteAction.Payload,
				promote: true
			),
			LayoutEngineCustomAction<IWindow> demoteAction when demoteAction.Name == _plugin.DemoteWindowActionName =>
				PromoteWindowInStack(demoteAction.Payload, promote: false),
			LayoutEngineCustomAction<IWindow> promoteFocusAction
				when promoteFocusAction.Name == _plugin.PromoteFocusActionName => PromoteFocusInStack(
				promoteFocusAction.Payload,
				promote: true
			),
			LayoutEngineCustomAction<IWindow> demoteFocusAction
				when demoteFocusAction.Name == _plugin.DemoteFocusActionName => PromoteFocusInStack(
				demoteFocusAction.Payload,
				promote: false
			),
			LayoutEngineCustomAction<IWindow> increasePrimaryAction
				when increasePrimaryAction.Name == _plugin.IncreasePrimaryCountActionName => ChangePrimaryCount(delta: 1),
			LayoutEngineCustomAction<IWindow> decreasePrimaryAction
				when decreasePrimaryAction.Name == _plugin.DecreasePrimaryCountActionName => ChangePrimaryCount(
				delta: -1
			),
			_ => this,
		};

	/// <summary>
	/// The order of the primary <see cref="SliceArea"/> - the one whose window count is changed by
	/// <see cref="ISliceLayoutPlugin.IncreasePrimaryCount"/> and
	/// <see cref="ISliceLayoutPlugin.DecreasePrimaryCount"/>.
	/// </summary>
	private const int PrimaryAreaOrder = 0;

	/// <summary>
	/// Returns a new engine whose primary <see cref="SliceArea"/> holds <paramref name="delta"/> more
	/// windows (clamped to a minimum of one). Returns this engine unchanged if the primary area is not
	/// found or the count would not change.
	/// </summary>
	private ILayoutEngine ChangePrimaryCount(int delta)
	{
		if (GetSliceAreaMaxChildren(RootArea, PrimaryAreaOrder) is not int currentMax)
		{
			return this;
		}

		int newMax = currentMax + delta < 1 ? 1 : currentMax + delta;
		if (newMax == currentMax)
		{
			return this;
		}

		ParentArea newRootArea = (ParentArea)CloneWithSliceMaxChildren(RootArea, PrimaryAreaOrder, newMax);
		return new SliceLayoutEngine(this, newRootArea, _windows, _minimizedWindows);
	}

	private static int? GetSliceAreaMaxChildren(IArea area, int order)
	{
		switch (area)
		{
			case ParentArea parent:
				foreach (IArea child in parent.Children)
				{
					if (GetSliceAreaMaxChildren(child, order) is int max)
					{
						return max;
					}
				}

				return null;
			case SliceArea slice when slice.Order == order:
				return slice.MaxChildren;
			default:
				return null;
		}
	}

	/// <summary>
	/// Deep-clones the area tree, setting the <see cref="SliceArea.MaxChildren"/> of the slice with the
	/// given <paramref name="order"/> to <paramref name="maxChildren"/>. Every area is a fresh instance
	/// so that <see cref="AreaHelpers.SetStartIndexes"/> (which mutates <c>StartIndex</c>) cannot affect
	/// the original engine's tree.
	/// </summary>
	private static IArea CloneWithSliceMaxChildren(IArea area, int order, int maxChildren) =>
		area switch
		{
			ParentArea parent => new ParentArea(
				parent.IsRow,
				parent.Weights,
				[.. parent.Children.Select(child => CloneWithSliceMaxChildren(child, order, maxChildren))]
			),
			SliceArea slice => new SliceArea(
				order: (uint)slice.Order,
				maxChildren: (uint)(slice.Order == order ? maxChildren : slice.MaxChildren),
				isRow: slice.IsRow
			),
			OverflowArea overflow => new OverflowArea(isRow: overflow.IsRow),
			_ => area,
		};
}
