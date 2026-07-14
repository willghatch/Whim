namespace Whim.Tests;

public class ConsolidateWorkspacesTransformTests
{
	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void InactiveWorkspace_WindowsMovedToActiveWorkspace(IContext ctx, MutableRootSector rootSector)
	{
		// Given a workspace active on the only monitor, and an inactive workspace containing a window
		Workspace activeWorkspace = CreateWorkspace();
		Workspace inactiveWorkspace = CreateWorkspace();

		IMonitor monitor = CreateMonitor((HMONITOR)1);
		IWindow window = CreateWindow((HWND)10);

		PopulateMonitorWorkspaceMap(rootSector, monitor, activeWorkspace);
		inactiveWorkspace = PopulateWindowWorkspaceMap(rootSector, window, inactiveWorkspace);
		rootSector.MonitorSector.ActiveMonitorHandle = monitor.Handle;

		ConsolidateWorkspacesTransform sut = new();

		// When we consolidate the workspaces
		var result = ctx.Store.Dispatch(sut);

		// Then the window is moved to the workspace on the monitor, and that workspace is laid out
		Assert.True(result.IsSuccessful);
		Assert.Equal(activeWorkspace.Id, rootSector.MapSector.WindowWorkspaceMap[window.Handle]);

		Assert.Contains(
			ctx.GetTransforms(),
			t =>
				(t as RemoveWindowFromWorkspaceTransform)
				== new RemoveWindowFromWorkspaceTransform(inactiveWorkspace.Id, window) { SkipDoLayout = true }
		);
		Assert.Contains(
			ctx.GetTransforms(),
			t =>
				(t as AddWindowToWorkspaceTransform)
				== new AddWindowToWorkspaceTransform(activeWorkspace.Id, window) { SkipDoLayout = true }
		);
		Assert.Contains(
			ctx.GetTransforms(),
			t => (t as DoWorkspaceLayoutTransform) == new DoWorkspaceLayoutTransform(activeWorkspace.Id)
		);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void StickyWorkspace_WindowsMovedToWorkspaceOnStickyMonitor(IContext ctx, MutableRootSector rootSector)
	{
		// Given two monitors with different active workspaces, and an inactive workspace which is
		// sticky to the second monitor
		Workspace firstMonitorWorkspace = CreateWorkspace();
		Workspace secondMonitorWorkspace = CreateWorkspace();
		Workspace stickyWorkspace = CreateWorkspace();

		IMonitor firstMonitor = CreateMonitor((HMONITOR)1);
		IMonitor secondMonitor = CreateMonitor((HMONITOR)2);
		IWindow window = CreateWindow((HWND)10);

		PopulateMonitorWorkspaceMap(rootSector, firstMonitor, firstMonitorWorkspace);
		PopulateMonitorWorkspaceMap(rootSector, secondMonitor, secondMonitorWorkspace);
		stickyWorkspace = PopulateWindowWorkspaceMap(rootSector, window, stickyWorkspace);

		// The active monitor is the first monitor, so a non-sticky workspace would be consolidated
		// into the first monitor's workspace.
		rootSector.MonitorSector.ActiveMonitorHandle = firstMonitor.Handle;
		rootSector.MapSector.StickyWorkspaceMonitorIndexMap = rootSector
			.MapSector.StickyWorkspaceMonitorIndexMap.SetItem(stickyWorkspace.Id, [1]);

		ConsolidateWorkspacesTransform sut = new();

		// When we consolidate the workspaces
		var result = ctx.Store.Dispatch(sut);

		// Then the window is moved to the workspace active on the sticky monitor, not the workspace
		// active on the active monitor
		Assert.True(result.IsSuccessful);
		Assert.Equal(secondMonitorWorkspace.Id, rootSector.MapSector.WindowWorkspaceMap[window.Handle]);

		Assert.Contains(
			ctx.GetTransforms(),
			t =>
				(t as AddWindowToWorkspaceTransform)
				== new AddWindowToWorkspaceTransform(secondMonitorWorkspace.Id, window) { SkipDoLayout = true }
		);
		Assert.Contains(
			ctx.GetTransforms(),
			t => (t as DoWorkspaceLayoutTransform) == new DoWorkspaceLayoutTransform(secondMonitorWorkspace.Id)
		);
		Assert.DoesNotContain(
			ctx.GetTransforms(),
			t =>
				(t as AddWindowToWorkspaceTransform)
				== new AddWindowToWorkspaceTransform(firstMonitorWorkspace.Id, window) { SkipDoLayout = true }
		);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void ActiveWorkspaces_AreLeftAlone(IContext ctx, MutableRootSector rootSector)
	{
		// Given a workspace which is active on a monitor and contains a window, and an inactive
		// workspace which also contains a window
		Workspace activeWorkspace = CreateWorkspace();
		Workspace inactiveWorkspace = CreateWorkspace();

		IMonitor monitor = CreateMonitor((HMONITOR)1);
		IWindow activeWindow = CreateWindow((HWND)10);
		IWindow inactiveWindow = CreateWindow((HWND)11);

		activeWorkspace = PopulateThreeWayMap(rootSector, monitor, activeWorkspace, activeWindow);
		inactiveWorkspace = PopulateWindowWorkspaceMap(rootSector, inactiveWindow, inactiveWorkspace);
		rootSector.MonitorSector.ActiveMonitorHandle = monitor.Handle;

		ConsolidateWorkspacesTransform sut = new();

		// When we consolidate the workspaces
		var result = ctx.Store.Dispatch(sut);

		// Then the active workspace's window is untouched, while the inactive workspace's window is moved
		Assert.True(result.IsSuccessful);
		Assert.Equal(activeWorkspace.Id, rootSector.MapSector.WindowWorkspaceMap[activeWindow.Handle]);
		Assert.Equal(activeWorkspace.Id, rootSector.MapSector.WindowWorkspaceMap[inactiveWindow.Handle]);
		Assert.Equal(activeWorkspace.Id, rootSector.MapSector.MonitorWorkspaceMap[monitor.Handle]);

		Assert.DoesNotContain(
			ctx.GetTransforms(),
			t =>
				(t as RemoveWindowFromWorkspaceTransform)
				== new RemoveWindowFromWorkspaceTransform(activeWorkspace.Id, activeWindow) { SkipDoLayout = true }
		);
		Assert.Contains(
			ctx.GetTransforms(),
			t =>
				(t as RemoveWindowFromWorkspaceTransform)
				== new RemoveWindowFromWorkspaceTransform(inactiveWorkspace.Id, inactiveWindow)
				{
					SkipDoLayout = true,
				}
		);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void NoInactiveWorkspaces_NoWindowsMoved(IContext ctx, MutableRootSector rootSector)
	{
		// Given all the workspaces are active on monitors
		Workspace firstWorkspace = CreateWorkspace();
		Workspace secondWorkspace = CreateWorkspace();

		IMonitor firstMonitor = CreateMonitor((HMONITOR)1);
		IMonitor secondMonitor = CreateMonitor((HMONITOR)2);
		IWindow window = CreateWindow((HWND)10);

		firstWorkspace = PopulateThreeWayMap(rootSector, firstMonitor, firstWorkspace, window);
		PopulateMonitorWorkspaceMap(rootSector, secondMonitor, secondWorkspace);
		rootSector.MonitorSector.ActiveMonitorHandle = firstMonitor.Handle;

		ConsolidateWorkspacesTransform sut = new();

		// When we consolidate the workspaces
		var result = ctx.Store.Dispatch(sut);

		// Then no windows are moved, and no layouts are performed
		Assert.True(result.IsSuccessful);
		Assert.Equal(firstWorkspace.Id, rootSector.MapSector.WindowWorkspaceMap[window.Handle]);

		Assert.DoesNotContain(ctx.GetTransforms(), t => t is RemoveWindowFromWorkspaceTransform);
		Assert.DoesNotContain(ctx.GetTransforms(), t => t is AddWindowToWorkspaceTransform);
		Assert.DoesNotContain(ctx.GetTransforms(), t => t is DoWorkspaceLayoutTransform);
	}
}
