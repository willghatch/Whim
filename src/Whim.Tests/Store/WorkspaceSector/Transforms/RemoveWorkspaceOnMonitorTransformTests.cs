namespace Whim.Tests;

public class RemoveWorkspaceOnMonitorTransformTests
{
	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void NoWorkspaceOnMonitor_Fails(IContext ctx, MutableRootSector root)
	{
		// Given a monitor with no workspace shown on it
		IMonitor monitor = CreateMonitor();
		AddMonitorsToSector(root, monitor);
		root.MonitorSector.ActiveMonitorHandle = monitor.Handle;

		RemoveWorkspaceOnMonitorTransform sut = new();

		// When
		var result = ctx.Store.Dispatch(sut);

		// Then it fails
		Assert.False(result.IsSuccessful);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void LastWorkspaceOnMonitor_Fails(IContext ctx, MutableRootSector root)
	{
		// Given two monitors and three workspaces, where only w1 can be shown on monitor0
		Workspace w1 = CreateWorkspace();
		Workspace w2 = CreateWorkspace();
		Workspace w3 = CreateWorkspace();

		IMonitor monitor0 = CreateMonitor((HMONITOR)1);
		IMonitor monitor1 = CreateMonitor((HMONITOR)2);

		PopulateMonitorWorkspaceMap(root, monitor0, w1);
		PopulateMonitorWorkspaceMap(root, monitor1, w2);
		AddWorkspaceToStore(root, w3);
		root.MonitorSector.ActiveMonitorHandle = monitor0.Handle;

		// w1 is sticky to monitor0, w2 and w3 are sticky to monitor1
		root.MapSector.StickyWorkspaceMonitorIndexMap = root
			.MapSector.StickyWorkspaceMonitorIndexMap.SetItem(w1.Id, [0])
			.SetItem(w2.Id, [1])
			.SetItem(w3.Id, [1]);

		RemoveWorkspaceOnMonitorTransform sut = new();

		// When we try to remove the last workspace assignable to monitor0
		var result = ctx.Store.Dispatch(sut);

		// Then it fails, and w1 is not removed
		Assert.False(result.IsSuccessful);
		Assert.Contains(w1.Id, root.WorkspaceSector.WorkspaceOrder);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void NotLastWorkspace_Removes(IContext ctx, MutableRootSector root)
	{
		// Given a single monitor with two workspaces assignable to it, w1 shown
		Workspace w1 = CreateWorkspace();
		Workspace w2 = CreateWorkspace();

		IMonitor monitor = CreateMonitor();
		PopulateMonitorWorkspaceMap(root, monitor, w1);
		AddWorkspaceToStore(root, w2);
		root.MonitorSector.ActiveMonitorHandle = monitor.Handle;

		RemoveWorkspaceOnMonitorTransform sut = new();

		// When we remove the current workspace on the monitor
		var result = ctx.Store.Dispatch(sut);

		// Then it succeeds and w1 is removed
		Assert.True(result.IsSuccessful);
		Assert.DoesNotContain(w1.Id, root.WorkspaceSector.WorkspaceOrder);
	}
}
