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

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void LastWorkspaceOnMonitor_Fails(IContext ctx, MutableRootSector rootSector)
	{
		// Given two monitors, each with a single workspace pinned to it
		Workspace wA = CreateWorkspace();
		Workspace wB = CreateWorkspace();

		IMonitor monitor1 = CreateMonitor((HMONITOR)1);
		IMonitor monitor2 = CreateMonitor((HMONITOR)2);

		PopulateMonitorWorkspaceMap(rootSector, monitor1, wA);
		PopulateMonitorWorkspaceMap(rootSector, monitor2, wB);

		rootSector.MapSector.StickyWorkspaceMonitorIndexMap = rootSector
			.MapSector.StickyWorkspaceMonitorIndexMap.SetItem(wA.Id, [0])
			.SetItem(wB.Id, [1]);

		MoveWorkspaceToAdjacentMonitorTransform sut = new(wA.Id);

		// When we try to move the only workspace on monitor1
		var result = ctx.Store.Dispatch(sut);

		// Then it fails and nothing moves
		Assert.False(result.IsSuccessful);
		Assert.Equal(wA.Id, rootSector.MapSector.MonitorWorkspaceMap[monitor1.Handle]);
		Assert.Equal(wB.Id, rootSector.MapSector.MonitorWorkspaceMap[monitor2.Handle]);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void MovesWorkspace_WithoutSwapping(IContext ctx, MutableRootSector rootSector)
	{
		// Given monitor1 shows wA and also has a hidden workspace wC, and monitor2 shows wB
		Workspace wA = CreateWorkspace();
		Workspace wB = CreateWorkspace();
		Workspace wC = CreateWorkspace();

		IMonitor monitor1 = CreateMonitor((HMONITOR)1);
		IMonitor monitor2 = CreateMonitor((HMONITOR)2);

		PopulateMonitorWorkspaceMap(rootSector, monitor1, wA);
		PopulateMonitorWorkspaceMap(rootSector, monitor2, wB);
		AddWorkspaceToStore(rootSector, wC);

		// wA and wC are pinned to monitor1, wB to monitor2.
		rootSector.MapSector.StickyWorkspaceMonitorIndexMap = rootSector
			.MapSector.StickyWorkspaceMonitorIndexMap.SetItem(wA.Id, [0])
			.SetItem(wC.Id, [0])
			.SetItem(wB.Id, [1]);

		MoveWorkspaceToAdjacentMonitorTransform sut = new(wA.Id);

		// When we move wA to the next monitor
		var result = ctx.Store.Dispatch(sut);

		// Then monitor1 switches to wC, monitor2 shows wA, and wB is hidden (not moved to monitor1)
		Assert.True(result.IsSuccessful);
		Assert.Equal(wC.Id, rootSector.MapSector.MonitorWorkspaceMap[monitor1.Handle]);
		Assert.Equal(wA.Id, rootSector.MapSector.MonitorWorkspaceMap[monitor2.Handle]);
		Assert.DoesNotContain(wB.Id, rootSector.MapSector.MonitorWorkspaceMap.Values);

		// The moved workspace's pin follows it; the other pins are unchanged.
		Assert.Equal([1], rootSector.MapSector.StickyWorkspaceMonitorIndexMap[wA.Id]);
		Assert.Equal([1], rootSector.MapSector.StickyWorkspaceMonitorIndexMap[wB.Id]);
		Assert.Equal([0], rootSector.MapSector.StickyWorkspaceMonitorIndexMap[wC.Id]);
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
		// Given monitor1 shows wA with a hidden wC, and monitor2 shows wB
		Workspace wA = CreateWorkspace();
		Workspace wB = CreateWorkspace();
		Workspace wC = CreateWorkspace();

		IMonitor monitor1 = CreateMonitor((HMONITOR)1);
		IMonitor monitor2 = CreateMonitor((HMONITOR)2);

		PopulateMonitorWorkspaceMap(rootSector, monitor1, wA);
		PopulateMonitorWorkspaceMap(rootSector, monitor2, wB);
		AddWorkspaceToStore(rootSector, wC);

		MoveWorkspaceToAdjacentMonitorTransform sut = new(wA.Id, FocusWorkspaceWindow: focusWorkspaceWindow);

		// When we execute the transform
		ctx.Store.Dispatch(sut);

		// Then the moved workspace is activated on the target monitor with the given focus flag
		Assert.Contains(
			transforms,
			t => t.Equals(new ActivateWorkspaceTransform(wA.Id, monitor2.Handle, focusWorkspaceWindow))
		);
	}
}
