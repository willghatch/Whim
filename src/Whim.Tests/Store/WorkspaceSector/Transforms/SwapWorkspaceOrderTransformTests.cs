namespace Whim.Tests;

public class SwapWorkspaceOrderTransformTests
{
	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void NoMonitorForWorkspace(IContext ctx, MutableRootSector root)
	{
		// Given a workspace that is not shown on any monitor
		Workspace workspace = CreateWorkspace();
		AddWorkspaceToStore(root, workspace);

		SwapWorkspaceOrderTransform sut = new(workspace.Id);

		// When
		var result = ctx.Store.Dispatch(sut);

		// Then it fails
		Assert.False(result.IsSuccessful);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void SingleWorkspaceOnMonitor_NoOp(IContext ctx, MutableRootSector root)
	{
		// Given a single workspace on a monitor
		Workspace workspace = CreateWorkspace();
		IMonitor monitor = CreateMonitor();
		PopulateMonitorWorkspaceMap(root, monitor, workspace);

		SwapWorkspaceOrderTransform sut = new(workspace.Id);

		// When
		var result = ctx.Store.Dispatch(sut);

		// Then it succeeds without changing the order
		Assert.True(result.IsSuccessful);
		Assert.Equal([workspace.Id], root.WorkspaceSector.WorkspaceOrder);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void SwapNext_MatchesExample(IContext ctx, MutableRootSector root)
	{
		// Given workspaces 1 2 3 in order, all assignable to one monitor, with 2 active
		Workspace w1 = CreateWorkspace();
		Workspace w2 = CreateWorkspace();
		Workspace w3 = CreateWorkspace();
		AddWorkspaceToStore(root, w1);
		AddWorkspaceToStore(root, w2);
		AddWorkspaceToStore(root, w3);

		IMonitor monitor = CreateMonitor();
		PopulateMonitorWorkspaceMap(root, monitor, w2);

		SwapWorkspaceOrderTransform sut = new(w2.Id, Reverse: false);

		// When we swap the focused workspace (2) with the next
		var result = ctx.Store.Dispatch(sut);

		// Then the order becomes 1 3 2, and 2 is still the active workspace on the monitor
		Assert.True(result.IsSuccessful);
		Assert.Equal([w1.Id, w3.Id, w2.Id], root.WorkspaceSector.WorkspaceOrder);
		Assert.Equal(w2.Id, root.MapSector.MonitorWorkspaceMap[monitor.Handle]);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void SwapPrevious(IContext ctx, MutableRootSector root)
	{
		// Given workspaces 1 2 3 in order, with 2 active
		Workspace w1 = CreateWorkspace();
		Workspace w2 = CreateWorkspace();
		Workspace w3 = CreateWorkspace();
		AddWorkspaceToStore(root, w1);
		AddWorkspaceToStore(root, w2);
		AddWorkspaceToStore(root, w3);

		IMonitor monitor = CreateMonitor();
		PopulateMonitorWorkspaceMap(root, monitor, w2);

		SwapWorkspaceOrderTransform sut = new(w2.Id, Reverse: true);

		// When we swap the focused workspace (2) with the previous
		var result = ctx.Store.Dispatch(sut);

		// Then the order becomes 2 1 3
		Assert.True(result.IsSuccessful);
		Assert.Equal([w2.Id, w1.Id, w3.Id], root.WorkspaceSector.WorkspaceOrder);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void SwapNext_WrapsAround(IContext ctx, MutableRootSector root)
	{
		// Given workspaces 1 2 3 in order, with 3 active
		Workspace w1 = CreateWorkspace();
		Workspace w2 = CreateWorkspace();
		Workspace w3 = CreateWorkspace();
		AddWorkspaceToStore(root, w1);
		AddWorkspaceToStore(root, w2);
		AddWorkspaceToStore(root, w3);

		IMonitor monitor = CreateMonitor();
		PopulateMonitorWorkspaceMap(root, monitor, w3);

		SwapWorkspaceOrderTransform sut = new(w3.Id, Reverse: false);

		// When we swap the last workspace with the next (wraps to the first)
		var result = ctx.Store.Dispatch(sut);

		// Then the order becomes 3 2 1
		Assert.True(result.IsSuccessful);
		Assert.Equal([w3.Id, w2.Id, w1.Id], root.WorkspaceSector.WorkspaceOrder);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void RaisesWorkspaceOrderChanged(IContext ctx, MutableRootSector root)
	{
		// Given workspaces 1 2 with 1 active
		Workspace w1 = CreateWorkspace();
		Workspace w2 = CreateWorkspace();
		AddWorkspaceToStore(root, w1);
		AddWorkspaceToStore(root, w2);

		IMonitor monitor = CreateMonitor();
		PopulateMonitorWorkspaceMap(root, monitor, w1);

		SwapWorkspaceOrderTransform sut = new(w1.Id);

		// When we swap the order
		var raised = Assert.Raises<WorkspaceOrderChangedEventArgs>(
			h => ctx.Store.WorkspaceEvents.WorkspaceOrderChanged += h,
			h => ctx.Store.WorkspaceEvents.WorkspaceOrderChanged -= h,
			() => ctx.Store.Dispatch(sut)
		);

		// Then the WorkspaceOrderChanged event is raised with the new order
		Assert.Equal([w2.Id, w1.Id], raised.Arguments.WorkspaceOrder);
	}
}
