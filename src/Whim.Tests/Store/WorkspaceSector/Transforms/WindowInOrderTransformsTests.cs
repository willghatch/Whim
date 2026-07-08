namespace Whim.Tests;

public class WindowInOrderTransformsTests
{
	private static (Workspace workspace, IWindow a, IWindow b, IWindow c) SetupWorkspace(
		MutableRootSector root,
		HWND lastFocused
	)
	{
		IWindow a = CreateWindow((HWND)1);
		IWindow b = CreateWindow((HWND)2);
		IWindow c = CreateWindow((HWND)3);
		AddWindowToSector(root, a);
		AddWindowToSector(root, b);
		AddWindowToSector(root, c);

		// Positions left-to-right: a, b, c.
		Workspace workspace = CreateWorkspace() with
		{
			LastFocusedWindowHandle = lastFocused,
			WindowPositions = ImmutableDictionary<HWND, WindowPosition>
				.Empty.SetItem(a.Handle, new WindowPosition(WindowSize.Normal, new Rectangle<int>(0, 0, 100, 50)))
				.SetItem(b.Handle, new WindowPosition(WindowSize.Normal, new Rectangle<int>(100, 0, 100, 50)))
				.SetItem(c.Handle, new WindowPosition(WindowSize.Normal, new Rectangle<int>(200, 0, 100, 50))),
		};

		IMonitor monitor = CreateMonitor();
		PopulateMonitorWorkspaceMap(root, monitor, workspace);

		return (workspace, a, b, c);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void PickWorkspaceWindowsInOrder_SortsByPositionAndExcludesMinimized(
		IContext ctx,
		MutableRootSector root
	)
	{
		// Given a workspace whose windows are positioned out of insertion order, with one minimized
		IWindow a = CreateWindow((HWND)1);
		IWindow b = CreateWindow((HWND)2);
		IWindow minimized = CreateWindow((HWND)3);
		AddWindowToSector(root, a);
		AddWindowToSector(root, b);
		AddWindowToSector(root, minimized);

		Workspace workspace = CreateWorkspace() with
		{
			WindowPositions = ImmutableDictionary<HWND, WindowPosition>
				.Empty
				// b is to the right of a
				.SetItem(b.Handle, new WindowPosition(WindowSize.Normal, new Rectangle<int>(500, 0, 100, 50)))
				.SetItem(a.Handle, new WindowPosition(WindowSize.Normal, new Rectangle<int>(0, 0, 100, 50)))
				// minimized window would sort first by position, but must be excluded
				.SetItem(minimized.Handle, new WindowPosition(WindowSize.Minimized, new Rectangle<int>(-100, 0, 0, 0))),
		};
		AddWorkspaceToStore(root, workspace);

		// When
		var result = ctx.Store.Pick(PickWorkspaceWindowsInOrder(workspace.Id));

		// Then the windows are ordered a, b and the minimized window is excluded
		Assert.True(result.IsSuccessful);
		Assert.Equal(2, result.Value.Count);
		Assert.Equal(a.Handle, result.Value[0].Handle);
		Assert.Equal(b.Handle, result.Value[1].Handle);
	}

	[Theory]
	[InlineAutoSubstituteData<StoreCustomization>(false, 3)] // b -> next -> c
	[InlineAutoSubstituteData<StoreCustomization>(true, 1)] // b -> previous -> a
	internal void FocusWindowInOrder_FocusesAdjacent(
		bool reverse,
		int expectedHandle,
		IContext ctx,
		MutableRootSector root,
		List<object> transforms
	)
	{
		// Given b is the last focused window
		SetupWorkspace(root, (HWND)2);

		FocusWindowInOrderTransform sut = new(Reverse: reverse);

		// When
		var result = ctx.Store.Dispatch(sut);

		// Then the adjacent window is focused
		Assert.True(result.IsSuccessful);
		Assert.Contains(transforms, t => t.Equals(new FocusWindowTransform((HWND)expectedHandle)));
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void FocusWindowInOrder_WrapsAround(IContext ctx, MutableRootSector root, List<object> transforms)
	{
		// Given c (the last window) is focused
		SetupWorkspace(root, (HWND)3);

		FocusWindowInOrderTransform sut = new(Reverse: false);

		// When we focus the next window
		ctx.Store.Dispatch(sut);

		// Then it wraps around to the first window, a
		Assert.Contains(transforms, t => t.Equals(new FocusWindowTransform((HWND)1)));
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void FocusWindowInOrder_NoCurrent_FocusesFirst(
		IContext ctx,
		MutableRootSector root,
		List<object> transforms
	)
	{
		// Given there is no last focused window
		SetupWorkspace(root, default);

		FocusWindowInOrderTransform sut = new(Reverse: false);

		// When
		ctx.Store.Dispatch(sut);

		// Then the first window is focused
		Assert.Contains(transforms, t => t.Equals(new FocusWindowTransform((HWND)1)));
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void SwapWindowInOrder_MovesToTargetSlot(IContext ctx, MutableRootSector root, List<object> transforms)
	{
		// Given b is focused, in a workspace on a 1920x1080 monitor
		SetupWorkspace(root, (HWND)2);

		SwapWindowInOrderTransform sut = new(Reverse: false);

		// When we swap b with the next window (c, centred at x=250, y=25)
		var result = ctx.Store.Dispatch(sut);

		// Then b is moved to c's normalized centre
		Assert.True(result.IsSuccessful);
		Assert.Contains(
			transforms,
			t =>
				t is MoveWindowToPointInWorkspaceTransform m
				&& m.WindowHandle == (HWND)2
				&& System.Math.Abs(m.Point.X - (250.0 / 1920)) < 1e-9
				&& System.Math.Abs(m.Point.Y - (25.0 / 1080)) < 1e-9
		);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void SwapWindowInOrder_SingleWindow_NoOp(IContext ctx, MutableRootSector root, List<object> transforms)
	{
		// Given a workspace with a single window
		IWindow a = CreateWindow((HWND)1);
		AddWindowToSector(root, a);
		Workspace workspace = CreateWorkspace() with
		{
			LastFocusedWindowHandle = a.Handle,
			WindowPositions = ImmutableDictionary<HWND, WindowPosition>.Empty.SetItem(
				a.Handle,
				new WindowPosition(WindowSize.Normal, new Rectangle<int>(0, 0, 100, 50))
			),
		};
		PopulateMonitorWorkspaceMap(root, CreateMonitor(), workspace);

		SwapWindowInOrderTransform sut = new();

		// When
		var result = ctx.Store.Dispatch(sut);

		// Then nothing is moved
		Assert.True(result.IsSuccessful);
		Assert.DoesNotContain(transforms, t => t is MoveWindowToPointInWorkspaceTransform);
	}
}
