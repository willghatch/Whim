namespace Whim.Tests;

public class CreateWorkspaceOnMonitorTransformTests
{
	private static void SetupEngineCreators(MutableRootSector root)
	{
		root.WorkspaceSector.HasInitialized = true;
		root.WorkspaceSector.CreateLayoutEngines = () =>
			new CreateLeafLayoutEngine[] { (id) => Substitute.For<ILayoutEngine>() };
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void NoMonitor_Fails(IContext ctx, MutableRootSector root)
	{
		// Given there is no active monitor
		SetupEngineCreators(root);
		CreateWorkspaceOnMonitorTransform sut = new();

		// When
		var result = ctx.Store.Dispatch(sut);

		// Then it fails
		Assert.False(result.IsSuccessful);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void ExplicitName_PinsAndShows(IContext ctx, MutableRootSector root)
	{
		// Given a single active monitor
		SetupEngineCreators(root);
		IMonitor monitor = CreateMonitor((HMONITOR)7);
		AddMonitorsToSector(root, monitor);
		root.MonitorSector.ActiveMonitorHandle = monitor.Handle;

		CreateWorkspaceOnMonitorTransform sut = new(Name: "Custom");

		// When
		var result = ctx.Store.Dispatch(sut);

		// Then the workspace is created with the given name, pinned to the monitor's index, and shown on it
		Assert.True(result.IsSuccessful);
		WorkspaceId id = result.Value;
		Assert.Equal("Custom", root.WorkspaceSector.Workspaces[id].Name);
		Assert.Equal([0], root.MapSector.StickyWorkspaceMonitorIndexMap[id]);
		Assert.Equal(id, root.MapSector.MonitorWorkspaceMap[monitor.Handle]);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void NoName_GeneratesFirstUnusedNumericName(IContext ctx, MutableRootSector root)
	{
		// Given an active monitor and existing workspaces named "0" and "2"
		SetupEngineCreators(root);
		IMonitor monitor = CreateMonitor();
		AddMonitorsToSector(root, monitor);
		root.MonitorSector.ActiveMonitorHandle = monitor.Handle;

		AddWorkspaceToStore(root, CreateWorkspace() with { Name = "0" });
		AddWorkspaceToStore(root, CreateWorkspace() with { Name = "2" });

		CreateWorkspaceOnMonitorTransform sut = new();

		// When
		var result = ctx.Store.Dispatch(sut);

		// Then the new workspace gets the first unused numeric name, "1"
		Assert.True(result.IsSuccessful);
		Assert.Equal("1", root.WorkspaceSector.Workspaces[result.Value].Name);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void PinsToSpecificMonitorIndex(IContext ctx, MutableRootSector root)
	{
		// Given two monitors, with the second active
		SetupEngineCreators(root);
		IMonitor monitor1 = CreateMonitor((HMONITOR)1);
		IMonitor monitor2 = CreateMonitor((HMONITOR)2);
		AddMonitorsToSector(root, monitor1, monitor2);
		root.MonitorSector.ActiveMonitorHandle = monitor2.Handle;

		CreateWorkspaceOnMonitorTransform sut = new();

		// When
		var result = ctx.Store.Dispatch(sut);

		// Then the new workspace is pinned to the second monitor's index (1)
		Assert.True(result.IsSuccessful);
		Assert.Equal([1], root.MapSector.StickyWorkspaceMonitorIndexMap[result.Value]);
	}
}
