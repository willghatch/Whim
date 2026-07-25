using System.Linq;
using FluentAssertions;

namespace Whim.Tests;

public class InitializeWorkspacesTransformTests
{
	private static Result<Unit> AssertDoesNotRaise(
		IContext ctx,
		MutableRootSector rootSector,
		InitializeWorkspacesTransform sut
	)
	{
		Result<Unit>? result = null;
		CustomAssert.DoesNotRaise<WindowAddedEventArgs>(
			h => rootSector.WindowSector.WindowAdded += h,
			h => rootSector.WindowSector.WindowAdded -= h,
			() => result = ctx.Store.Dispatch(sut)
		);
		return result!.Value;
	}

	private static (Result<Unit>, List<WindowAddedEventArgs>) AssertRaises(
		IContext ctx,
		MutableRootSector rootSector,
		InitializeWorkspacesTransform sut
	)
	{
		Result<Unit>? result = null;
		List<WindowAddedEventArgs> evs = new();
		CustomAssert.Raises<WindowAddedEventArgs>(
			h => rootSector.WindowSector.WindowAdded += h,
			h => rootSector.WindowSector.WindowAdded -= h,
			() => result = ctx.Store.Dispatch(sut),
			(sender, args) => evs.Add(args)
		);
		return (result!.Value, evs);
	}

	private static void AddWorkspacesToSavedState(IInternalContext internalCtx, params SavedWorkspace[] workspaces)
	{
		internalCtx.CoreSavedStateManager.SavedState.Returns(new CoreSavedState([.. workspaces]));
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void NoSavedWorkspaces(IContext ctx, MutableRootSector rootSector)
	{
		// Given there are no saved workspaces
		InitializeWorkspacesTransform sut = new();

		// When
		var result = AssertDoesNotRaise(ctx, rootSector, sut);

		// Then
		Assert.True(result.IsSuccessful);
		Assert.Empty(rootSector.MapSector.WindowWorkspaceMap);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void CouldNotFindWorkspace(IContext ctx, IInternalContext internalCtx, MutableRootSector rootSector)
	{
		// Given there are saved workspaces which don't exist in the workspace manager
		SavedWorkspace workspace = new("test", new List<SavedWindow>(), null);
		AddWorkspacesToSavedState(internalCtx, workspace);

		InitializeWorkspacesTransform sut = new();

		// When the map transform is dispatched
		var result = AssertDoesNotRaise(ctx, rootSector, sut);

		// Then
		Assert.True(result.IsSuccessful);
		Assert.Empty(rootSector.MapSector.WindowWorkspaceMap);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void CouldNotFindWindowFromHandle(IContext ctx, IInternalContext internalCtx, MutableRootSector rootSector)
	{
		// Given there are saved workspaces which don't exist in the workspace manager
		IWindow window = CreateWindow((HWND)10);
		SavedWorkspace workspace = new("test", [new SavedWindow(window.Handle, Rectangle.UnitSquare<double>())], null);
		AddWorkspacesToSavedState(internalCtx, workspace);

		ctx.CreateWindow(window.Handle).Returns(Result.FromException<IWindow>(new Exception("nope")));

		InitializeWorkspacesTransform sut = new();

		// When the map transform is dispatched
		var result = AssertDoesNotRaise(ctx, rootSector, sut);

		// Then
		Assert.True(result.IsSuccessful);
		Assert.Empty(rootSector.MapSector.WindowWorkspaceMap);
	}

	private static HWND BrowserHandle => (HWND)1;
	private static HWND SpotifyHandle => (HWND)2;
	private static HWND BrokenHandle => (HWND)3;
	private static HWND DiscordHandle => (HWND)4;
	private static HWND VscodeHandle => (HWND)5;

	private static string BrowserWorkspaceName => "Browser";
	private static string MediaWorkspaceName => "Media";
	private static string CodeWorkspaceName => "Code";
	private static string StickyWorkspaceName => "Sticky";
	private static string SavedStickyWorkspaceName => "SavedSticky";

	private static HMONITOR BrowserMonitor => (HMONITOR)1;
	private static HMONITOR CodeMonitor => (HMONITOR)2;
	private static HMONITOR AutoMonitor => (HMONITOR)3;

	private static void Setup_UserCreatedWorkspaces(MutableRootSector root)
	{
		root.WorkspaceSector.WorkspacesToCreate =
		[
			new WorkspaceToCreate(Guid.NewGuid(), BrowserWorkspaceName, null, null),
			new WorkspaceToCreate(
				Guid.NewGuid(),
				CodeWorkspaceName,
				[(id) => new ImmutableTestLayoutEngine(), (id) => new ImmutableTestLayoutEngine()],
				null
			),
			new WorkspaceToCreate(Guid.NewGuid(), StickyWorkspaceName, null, [0, 1]),
		];
	}

	private static void Setup_SavedState(IInternalContext internalCtx)
	{
		SavedWindow browserWindow = new(BrowserHandle, Rectangle.UnitSquare<double>());
		SavedWindow spotifyWindow = new(SpotifyHandle, Rectangle.UnitSquare<double>());
		SavedWindow brokenWindow = new(BrokenHandle, Rectangle.UnitSquare<double>());
		SavedWindow discordWindow = new(DiscordHandle, Rectangle.UnitSquare<double>());

		SavedWorkspace browserWorkspace = new(BrowserWorkspaceName, [browserWindow, brokenWindow], null);
		SavedWorkspace mediaWorkspace = new(MediaWorkspaceName, [spotifyWindow, discordWindow], null);
		SavedWorkspace savedStickyWorkspace = new(SavedStickyWorkspaceName, [], [1, 2]);

		AddWorkspacesToSavedState(internalCtx, browserWorkspace, mediaWorkspace, savedStickyWorkspace);
	}

	private static void Setup_CreateWindow(IContext ctx)
	{
		IWindow browserWindow = CreateWindow(BrowserHandle);
		IWindow discordWindow = CreateWindow(DiscordHandle);
		IWindow spotifyWindow = CreateWindow(SpotifyHandle);
		IWindow vscodeWindow = CreateWindow(VscodeHandle);

		ctx.CreateWindow(BrowserHandle).Returns(Result.FromValue(browserWindow));
		ctx.CreateWindow(DiscordHandle).Returns(Result.FromValue(discordWindow));
		ctx.CreateWindow(SpotifyHandle).Returns(Result.FromValue(spotifyWindow));
		ctx.CreateWindow(BrokenHandle).Returns(Result.FromException<IWindow>(new Exception("nope")));
		ctx.CreateWindow(VscodeHandle).Returns(Result.FromValue(vscodeWindow));
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void PopulateSavedWorkspaces(IContext ctx, IInternalContext internalCtx, MutableRootSector rootSector)
	{
		// Given:
		// - the user has created the "Browser", "Code", and "Sticky" workspaces
		Setup_UserCreatedWorkspaces(rootSector);

		// - the user has saved the "Browser", "Media", and "SavedSticky" workspaces
		//   - "Browser" has a browser window, a Spotify window, and a broken window
		//   - "Media" has a saved Discord window
		//   - "SavedSticky" has no windows
		Setup_SavedState(internalCtx);

		// - the Broken window fails to create
		// - there's a new vscode window
		Setup_CreateWindow(ctx);

		// - the Spotify and Discord window will appear in the newly created workspace's monitor
		internalCtx
			.CoreNativeManager.MonitorFromWindow(
				Arg.Is<HWND>(h => h == SpotifyHandle || h == DiscordHandle),
				MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST
			)
			.Returns(_ => AutoMonitor);

		// - the vscode window will appear in the "Code" workspace's monitor
		internalCtx
			.CoreNativeManager.MonitorFromWindow(
				Arg.Is<HWND>(h => h == VscodeHandle),
				MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST
			)
			.Returns(_ => CodeMonitor);

		// - there are three monitors
		AddMonitorsToSector(
			rootSector,
			CreateMonitor(BrowserMonitor),
			CreateMonitor(CodeMonitor),
			CreateMonitor(AutoMonitor)
		);

		internalCtx
			.CoreNativeManager.GetAllWindows()
			.Returns(_ => new List<HWND>() { BrowserHandle, DiscordHandle, SpotifyHandle, BrokenHandle, VscodeHandle });

		internalCtx.CoreNativeManager.IsStandardWindow(Arg.Any<HWND>()).Returns(true);
		internalCtx.CoreNativeManager.HasNoVisibleOwner(Arg.Any<HWND>()).Returns(true);

		rootSector.WorkspaceSector.CreateLayoutEngines = () => [id => new ImmutableTestLayoutEngine()];

		InitializeWorkspacesTransform sut = new();

		// When the map transform is dispatched
		var (result, evs) = AssertRaises(ctx, rootSector, sut);

		// Then:
		Assert.True(result.IsSuccessful);

		// - 4 windows have been added
		Assert.Equal(4, evs.Count);
		Assert.Equal(4, rootSector.WindowSector.Windows.Count);
		Assert.Equal(4, rootSector.MapSector.WindowWorkspaceMap.Count);

		// - there are 4 workspaces
		Assert.Equal(4, rootSector.WorkspaceSector.Workspaces.Count);

		// - the "Browser" workspace has been added with the "Browser", "Spotify", and "Discord"  windows
		Workspace browserWorkspace = rootSector.WorkspaceSector.Workspaces.Values.FirstOrDefault(w =>
			w.Name == BrowserWorkspaceName
		)!;
		Assert.Single(browserWorkspace.WindowPositions);
		Assert.Contains(BrowserHandle, browserWorkspace.WindowPositions);

		Assert.Single(browserWorkspace.LayoutEngines);

		// - the new "Code" workspace has been added with the new "vscode" window
		Workspace codeWorkspace = rootSector.WorkspaceSector.Workspaces.Values.FirstOrDefault(w =>
			w.Name == CodeWorkspaceName
		)!;
		Assert.Single(codeWorkspace.WindowPositions);
		Assert.Contains(VscodeHandle, codeWorkspace.WindowPositions);

		Assert.Equal(2, codeWorkspace.LayoutEngines.Count);

		// - the "Sticky" workspace has been added with no windows
		Workspace stickyWorkspace = rootSector.WorkspaceSector.Workspaces.Values.FirstOrDefault(w =>
			w.Name == StickyWorkspaceName
		)!;
		Assert.Empty(stickyWorkspace.WindowPositions);

		Assert.Single(stickyWorkspace.LayoutEngines);
		Assert.Equal(2, rootSector.MapSector.StickyWorkspaceMonitorIndexMap.Count);
		rootSector.MapSector.StickyWorkspaceMonitorIndexMap[stickyWorkspace.Id].Should().BeEquivalentTo([0, 1]);

		// - the automatically created workspace has the "Spotify" and "Discord" windows
		Workspace autoWorkspace = rootSector.WorkspaceSector.Workspaces.Values.FirstOrDefault(w =>
			w.Name == "Workspace 4"
		)!;
		Assert.Equal(2, autoWorkspace.WindowPositions.Count);
		Assert.Contains(SpotifyHandle, autoWorkspace.WindowPositions);
		Assert.Contains(DiscordHandle, autoWorkspace.WindowPositions);

		Assert.Single(autoWorkspace.LayoutEngines);

		// - the automatically created workspace is pinned to its monitor (the leftover AutoMonitor, index 2)
		rootSector.MapSector.StickyWorkspaceMonitorIndexMap[autoWorkspace.Id].Should().BeEquivalentTo([2]);

		// - the WorkspaceSector has initialized
		Assert.True(rootSector.WorkspaceSector.HasInitialized);

		// - the workspaces are activated on each of the three monitors
		Assert.Equal(3, rootSector.MapSector.MonitorWorkspaceMap.Count);
		Assert.Equal(browserWorkspace.Id, rootSector.MapSector.MonitorWorkspaceMap[BrowserMonitor]);
		Assert.Equal(codeWorkspace.Id, rootSector.MapSector.MonitorWorkspaceMap[CodeMonitor]);
		Assert.Equal(autoWorkspace.Id, rootSector.MapSector.MonitorWorkspaceMap[AutoMonitor]);

		// - the startup windows are set
		Assert.Equal(5, rootSector.WindowSector.StartupWindows.Count);
		rootSector
			.WindowSector.StartupWindows.Should()
			.BeEquivalentTo([BrowserHandle, DiscordHandle, SpotifyHandle, BrokenHandle, VscodeHandle]);
	}

	/// <summary>
	/// With no configured or saved workspaces, each monitor gets its own workspace pinned to that
	/// monitor's index.
	/// </summary>
	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void NoConfiguredWorkspaces_EachMonitorGetsPinnedWorkspace(
		IContext ctx,
		IInternalContext internalCtx,
		MutableRootSector rootSector
	)
	{
		// Given there are no configured or saved workspaces, and two monitors
		AddMonitorsToSector(rootSector, CreateMonitor(BrowserMonitor), CreateMonitor(CodeMonitor));
		rootSector.WorkspaceSector.CreateLayoutEngines = () => [(id) => new ImmutableTestLayoutEngine()];

		internalCtx.CoreNativeManager.GetAllWindows().Returns(_ => new List<HWND>());

		InitializeWorkspacesTransform sut = new();

		// When the transform is dispatched
		var result = AssertDoesNotRaise(ctx, rootSector, sut);

		// Then each monitor has its own distinct workspace, pinned to that monitor's index
		Assert.True(result.IsSuccessful);

		Assert.Equal(2, rootSector.WorkspaceSector.Workspaces.Count);
		Assert.Equal(2, rootSector.MapSector.MonitorWorkspaceMap.Count);

		WorkspaceId browserWorkspaceId = rootSector.MapSector.MonitorWorkspaceMap[BrowserMonitor];
		WorkspaceId codeWorkspaceId = rootSector.MapSector.MonitorWorkspaceMap[CodeMonitor];
		Assert.NotEqual(browserWorkspaceId, codeWorkspaceId);

		Assert.Equal(2, rootSector.MapSector.StickyWorkspaceMonitorIndexMap.Count);
		rootSector.MapSector.StickyWorkspaceMonitorIndexMap[browserWorkspaceId].Should().BeEquivalentTo([0]);
		rootSector.MapSector.StickyWorkspaceMonitorIndexMap[codeWorkspaceId].Should().BeEquivalentTo([1]);
	}

	/// <summary>
	/// Marks the given handles as hidden, and all the other handles as visible.
	/// </summary>
	private static void Setup_HiddenWindows(IInternalContext internalCtx, params HWND[] hiddenHandles)
	{
		internalCtx.CoreNativeManager.IsWindowVisible(Arg.Any<HWND>()).Returns(true);

		foreach (HWND handle in hiddenHandles)
		{
			internalCtx.CoreNativeManager.IsWindowVisible(handle).Returns(false);
		}
	}

	/// <summary>
	/// Makes every window pass the <see cref="WindowAddedTransform"/> filters.
	/// </summary>
	private static void Setup_StandardWindows(IInternalContext internalCtx)
	{
		internalCtx.CoreNativeManager.IsStandardWindow(Arg.Any<HWND>()).Returns(true);
		internalCtx.CoreNativeManager.HasNoVisibleOwner(Arg.Any<HWND>()).Returns(true);
	}

	private static void Setup_SingleMonitor(MutableRootSector rootSector)
	{
		AddMonitorsToSector(rootSector, CreateMonitor(BrowserMonitor));
		rootSector.WorkspaceSector.CreateLayoutEngines = () => [(id) => new ImmutableTestLayoutEngine()];
	}

	/// <summary>
	/// A window which Whim hid in a previous run and never restored is shown again, so that it can
	/// be seen by the user and by Whim.
	/// </summary>
	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void HiddenSavedWindow_IsShownAndRoutedToItsSavedWorkspace(
		IContext ctx,
		IInternalContext internalCtx,
		MutableRootSector rootSector
	)
	{
		// Given the "Browser" workspace is configured and saved with a browser window which is
		// still a window, but is hidden
		rootSector.WorkspaceSector.WorkspacesToCreate =
		[
			new WorkspaceToCreate(Guid.NewGuid(), BrowserWorkspaceName, null, null),
		];
		AddWorkspacesToSavedState(
			internalCtx,
			new SavedWorkspace(
				BrowserWorkspaceName,
				[new SavedWindow(BrowserHandle, Rectangle.UnitSquare<double>())],
				null
			)
		);
		Setup_CreateWindow(ctx);
		Setup_StandardWindows(internalCtx);
		Setup_HiddenWindows(internalCtx, BrowserHandle);
		Setup_SingleMonitor(rootSector);

		internalCtx.CoreNativeManager.GetAllWindows().Returns(_ => new List<HWND>() { BrowserHandle });

		InitializeWorkspacesTransform sut = new();

		// When the transform is dispatched
		var (result, _) = AssertRaises(ctx, rootSector, sut);

		// Then the window is shown, and is in the "Browser" workspace
		Assert.True(result.IsSuccessful);
		ctx.NativeManager.Received(1).ShowWindowNoActivate(BrowserHandle);

		Workspace browserWorkspace = rootSector.WorkspaceSector.Workspaces.Values.First(w =>
			w.Name == BrowserWorkspaceName
		);
		Assert.Equal(browserWorkspace.Id, rootSector.MapSector.WindowWorkspaceMap[BrowserHandle]);
	}

	/// <summary>
	/// The regression this feature exists for: a window hidden by a previous Whim run, whose saved
	/// workspace is no longer configured, is shown again and picked up into some workspace, instead
	/// of being lost.
	/// </summary>
	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void HiddenSavedWindow_WithoutMatchingWorkspace_IsShownAndAddedToAWorkspace(
		IContext ctx,
		IInternalContext internalCtx,
		MutableRootSector rootSector
	)
	{
		// Given only the "Browser" workspace is configured, but the saved "Media" workspace has a
		// hidden Discord window
		rootSector.WorkspaceSector.WorkspacesToCreate =
		[
			new WorkspaceToCreate(Guid.NewGuid(), BrowserWorkspaceName, null, null),
		];
		AddWorkspacesToSavedState(
			internalCtx,
			new SavedWorkspace(
				MediaWorkspaceName,
				[new SavedWindow(DiscordHandle, Rectangle.UnitSquare<double>())],
				null
			)
		);
		Setup_CreateWindow(ctx);
		Setup_StandardWindows(internalCtx);
		Setup_HiddenWindows(internalCtx, DiscordHandle);
		Setup_SingleMonitor(rootSector);

		internalCtx
			.CoreNativeManager.MonitorFromWindow(DiscordHandle, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST)
			.Returns(_ => BrowserMonitor);
		internalCtx.CoreNativeManager.GetAllWindows().Returns(_ => new List<HWND>() { DiscordHandle });

		InitializeWorkspacesTransform sut = new();

		// When the transform is dispatched
		var (result, _) = AssertRaises(ctx, rootSector, sut);

		// Then the window is shown, and is added to the workspace on the monitor it's on
		Assert.True(result.IsSuccessful);
		ctx.NativeManager.Received(1).ShowWindowNoActivate(DiscordHandle);

		Workspace browserWorkspace = rootSector.WorkspaceSector.Workspaces.Values.First(w =>
			w.Name == BrowserWorkspaceName
		);
		Assert.Equal(browserWorkspace.Id, rootSector.MapSector.WindowWorkspaceMap[DiscordHandle]);
	}

	/// <summary>
	/// A saved window which the user can still see is left alone.
	/// </summary>
	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void VisibleSavedWindow_IsNotShown(
		IContext ctx,
		IInternalContext internalCtx,
		MutableRootSector rootSector
	)
	{
		// Given the saved browser window is visible
		rootSector.WorkspaceSector.WorkspacesToCreate =
		[
			new WorkspaceToCreate(Guid.NewGuid(), BrowserWorkspaceName, null, null),
		];
		AddWorkspacesToSavedState(
			internalCtx,
			new SavedWorkspace(
				BrowserWorkspaceName,
				[new SavedWindow(BrowserHandle, Rectangle.UnitSquare<double>())],
				null
			)
		);
		Setup_CreateWindow(ctx);
		Setup_StandardWindows(internalCtx);
		Setup_HiddenWindows(internalCtx);
		Setup_SingleMonitor(rootSector);

		internalCtx.CoreNativeManager.GetAllWindows().Returns(_ => new List<HWND>() { BrowserHandle });

		InitializeWorkspacesTransform sut = new();

		// When the transform is dispatched
		var (result, _) = AssertRaises(ctx, rootSector, sut);

		// Then the window is not shown again
		Assert.True(result.IsSuccessful);
		ctx.NativeManager.DidNotReceive().ShowWindowNoActivate(BrowserHandle);
	}

	/// <summary>
	/// A saved handle which no longer belongs to a window is not resurrected.
	/// </summary>
	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void SavedHandleWhichIsNoLongerAWindow_IsNotShown(
		IContext ctx,
		IInternalContext internalCtx,
		MutableRootSector rootSector
	)
	{
		// Given the saved browser handle is no longer a window
		rootSector.WorkspaceSector.WorkspacesToCreate =
		[
			new WorkspaceToCreate(Guid.NewGuid(), BrowserWorkspaceName, null, null),
		];
		AddWorkspacesToSavedState(
			internalCtx,
			new SavedWorkspace(
				BrowserWorkspaceName,
				[new SavedWindow(BrowserHandle, Rectangle.UnitSquare<double>())],
				null
			)
		);
		Setup_StandardWindows(internalCtx);
		Setup_HiddenWindows(internalCtx, BrowserHandle);
		Setup_SingleMonitor(rootSector);

		internalCtx.CoreNativeManager.IsWindow(BrowserHandle).Returns(false);
		ctx.CreateWindow(BrowserHandle).Returns(Result.FromException<IWindow>(new Exception("nope")));
		internalCtx.CoreNativeManager.GetAllWindows().Returns(_ => new List<HWND>());

		InitializeWorkspacesTransform sut = new();

		// When the transform is dispatched
		var result = AssertDoesNotRaise(ctx, rootSector, sut);

		// Then the dead handle is not shown
		Assert.True(result.IsSuccessful);
		ctx.NativeManager.DidNotReceive().ShowWindowNoActivate(BrowserHandle);
	}

	/// <summary>
	/// A workspace which did not end up on a monitor is deactivated, so that the windows which were
	/// shown by this transform are hidden and tracked, rather than floating over the shown workspace.
	/// </summary>
	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void WorkspaceWithoutMonitor_IsDeactivated(
		IContext ctx,
		IInternalContext internalCtx,
		MutableRootSector rootSector
	)
	{
		// Given the "Browser" and "Code" workspaces are configured and saved with a window each,
		// but there is only a single monitor
		rootSector.WorkspaceSector.WorkspacesToCreate =
		[
			new WorkspaceToCreate(Guid.NewGuid(), BrowserWorkspaceName, null, null),
			new WorkspaceToCreate(Guid.NewGuid(), CodeWorkspaceName, null, null),
		];
		AddWorkspacesToSavedState(
			internalCtx,
			new SavedWorkspace(
				BrowserWorkspaceName,
				[new SavedWindow(BrowserHandle, Rectangle.UnitSquare<double>())],
				null
			),
			new SavedWorkspace(CodeWorkspaceName, [new SavedWindow(VscodeHandle, Rectangle.UnitSquare<double>())], null)
		);
		Setup_CreateWindow(ctx);
		Setup_StandardWindows(internalCtx);
		Setup_HiddenWindows(internalCtx, VscodeHandle);
		Setup_SingleMonitor(rootSector);

		internalCtx.CoreNativeManager.GetAllWindows().Returns(_ => new List<HWND>() { BrowserHandle, VscodeHandle });

		InitializeWorkspacesTransform sut = new();

		// When the transform is dispatched
		var (result, _) = AssertRaises(ctx, rootSector, sut);

		// Then the "Code" workspace is not shown on a monitor, so its window is hidden again, but
		// stays tracked in the "Code" workspace
		Assert.True(result.IsSuccessful);

		Workspace codeWorkspace = rootSector.WorkspaceSector.Workspaces.Values.First(w =>
			w.Name == CodeWorkspaceName
		);
		Assert.False(rootSector.MapSector.MonitorWorkspaceMap.ContainsValue(codeWorkspace.Id));

		ctx.NativeManager.Received().HideWindow(VscodeHandle);
		ctx.NativeManager.DidNotReceive().HideWindow(BrowserHandle);

		Assert.Equal(codeWorkspace.Id, rootSector.MapSector.WindowWorkspaceMap[VscodeHandle]);
		Assert.Equal(WindowSize.Minimized, codeWorkspace.WindowPositions[VscodeHandle].WindowSize);
	}
}
