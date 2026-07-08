namespace Whim.Tests;

public class MoveWorkspaceToAdjacentMonitorTransformTests
{
	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void NoMonitorForWorkspace(IContext ctx)
	{
		// Given there is no monitor for the given workspace
		Guid workspaceId = Guid.NewGuid();
		MoveWorkspaceToAdjacentMonitorTransform sut = new(workspaceId);

		// When we execute the transform
		var result = ctx.Store.Dispatch(sut);

		// Then we fail
		Assert.False(result.IsSuccessful);
	}

	[Theory]
	[InlineAutoSubstituteData<StoreCustomization>(false)]
	[InlineAutoSubstituteData<StoreCustomization>(true)]
	internal void SingleMonitor(bool reverse, IContext ctx, MutableRootSector rootSector)
	{
		// Given there is a single monitor
		IMonitor monitor = CreateMonitor((HMONITOR)10);
		Workspace workspace = CreateWorkspace();
		PopulateMonitorWorkspaceMap(rootSector, monitor, workspace);

		MoveWorkspaceToAdjacentMonitorTransform sut = new(workspace.Id, reverse);

		// When we execute the transform
		var result = ctx.Store.Dispatch(sut);

		// Then we succeed with no change (there is no adjacent monitor)
		Assert.True(result.IsSuccessful);
		Assert.Equal(workspace.Id, rootSector.MapSector.MonitorWorkspaceMap[monitor.Handle]);
	}

	[Theory]
	[InlineAutoSubstituteData<StoreCustomization>(false, 2)]
	[InlineAutoSubstituteData<StoreCustomization>(true, 3)]
	internal void Success_SwapsWorkspaces(
		bool reverse,
		int expectedTargetMonitor,
		IContext ctx,
		MutableRootSector rootSector
	)
	{
		// Given three monitors each with a workspace, and workspace1 is on monitor1
		Workspace workspace1 = CreateWorkspace();
		Workspace workspace2 = CreateWorkspace();
		Workspace workspace3 = CreateWorkspace();

		IMonitor monitor1 = CreateMonitor((HMONITOR)1);
		IMonitor monitor2 = CreateMonitor((HMONITOR)2);
		IMonitor monitor3 = CreateMonitor((HMONITOR)3);

		PopulateMonitorWorkspaceMap(rootSector, monitor1, workspace1);
		PopulateMonitorWorkspaceMap(rootSector, monitor2, workspace2);
		PopulateMonitorWorkspaceMap(rootSector, monitor3, workspace3);

		MoveWorkspaceToAdjacentMonitorTransform sut = new(workspace1.Id, reverse);

		// When we move workspace1 to the adjacent monitor
		var result = ctx.Store.Dispatch(sut);

		// Then workspace1 moves to the adjacent monitor, and that monitor's old workspace moves to monitor1
		Assert.True(result.IsSuccessful);
		Assert.Equal(workspace1.Id, rootSector.MapSector.MonitorWorkspaceMap[(HMONITOR)expectedTargetMonitor]);
		Assert.Equal(
			expectedTargetMonitor == 2 ? workspace2.Id : workspace3.Id,
			rootSector.MapSector.MonitorWorkspaceMap[monitor1.Handle]
		);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void PinnedWorkspaces_MoveAndSwapPins(IContext ctx, MutableRootSector rootSector)
	{
		// Given two monitors, each with a workspace pinned to it
		Workspace workspace1 = CreateWorkspace();
		Workspace workspace2 = CreateWorkspace();

		IMonitor monitor1 = CreateMonitor((HMONITOR)1);
		IMonitor monitor2 = CreateMonitor((HMONITOR)2);

		PopulateMonitorWorkspaceMap(rootSector, monitor1, workspace1);
		PopulateMonitorWorkspaceMap(rootSector, monitor2, workspace2);

		rootSector.MapSector.StickyWorkspaceMonitorIndexMap = rootSector
			.MapSector.StickyWorkspaceMonitorIndexMap.SetItem(workspace1.Id, [0])
			.SetItem(workspace2.Id, [1]);

		MoveWorkspaceToAdjacentMonitorTransform sut = new(workspace1.Id);

		// When we move the pinned workspace to the next monitor
		var result = ctx.Store.Dispatch(sut);

		// Then the workspaces are swapped and their pins follow them
		Assert.True(result.IsSuccessful);
		Assert.Equal(workspace1.Id, rootSector.MapSector.MonitorWorkspaceMap[monitor2.Handle]);
		Assert.Equal(workspace2.Id, rootSector.MapSector.MonitorWorkspaceMap[monitor1.Handle]);
		Assert.Equal([1], rootSector.MapSector.StickyWorkspaceMonitorIndexMap[workspace1.Id]);
		Assert.Equal([0], rootSector.MapSector.StickyWorkspaceMonitorIndexMap[workspace2.Id]);
	}

	[Theory]
	[InlineAutoSubstituteData<StoreCustomization>(true)]
	[InlineAutoSubstituteData<StoreCustomization>(false)]
	internal void PassesFocusFlagToActivate(
		bool focusWorkspaceWindow,
		IContext ctx,
		MutableRootSector rootSector,
		List<object> transforms
	)
	{
		// Given two monitors each with a workspace
		Workspace workspace1 = CreateWorkspace();
		Workspace workspace2 = CreateWorkspace();

		IMonitor monitor1 = CreateMonitor((HMONITOR)1);
		IMonitor monitor2 = CreateMonitor((HMONITOR)2);

		PopulateMonitorWorkspaceMap(rootSector, monitor1, workspace1);
		PopulateMonitorWorkspaceMap(rootSector, monitor2, workspace2);

		MoveWorkspaceToAdjacentMonitorTransform sut = new(workspace1.Id, FocusWorkspaceWindow: focusWorkspaceWindow);

		// When we execute the transform
		ctx.Store.Dispatch(sut);

		// Then the workspace is activated on the target monitor with the given focus flag
		Assert.Contains(
			transforms,
			t => t.Equals(new ActivateWorkspaceTransform(workspace1.Id, monitor2.Handle, focusWorkspaceWindow))
		);
	}
}
