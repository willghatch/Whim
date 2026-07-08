namespace Whim.Tests;

public class SetStickyMonitorIndicesTransformTests
{
	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void WorkspaceNotFound(IContext ctx)
	{
		// Given a workspace which does not exist
		Guid workspaceId = Guid.NewGuid();
		SetStickyMonitorIndicesTransform sut = new(workspaceId, [0]);

		// When we execute the transform
		var result = ctx.Store.Dispatch(sut);

		// Then we fail
		Assert.False(result.IsSuccessful);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void SetIndices_Pins(IContext ctx, MutableRootSector rootSector)
	{
		// Given a workspace which is not currently sticky
		Workspace workspace = CreateWorkspace();
		AddWorkspaceToStore(rootSector, workspace);

		SetStickyMonitorIndicesTransform sut = new(workspace.Id, [1, 2]);

		// When we execute the transform
		var result = ctx.Store.Dispatch(sut);

		// Then the workspace is pinned to the given monitor indices
		Assert.True(result.IsSuccessful);
		Assert.True(
			rootSector.MapSector.StickyWorkspaceMonitorIndexMap.TryGetValue(
				workspace.Id,
				out ImmutableArray<int> indices
			)
		);
		Assert.Equal([1, 2], indices);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void SetIndices_Overwrites(IContext ctx, MutableRootSector rootSector)
	{
		// Given a workspace which is already sticky
		Workspace workspace = CreateWorkspace();
		AddWorkspaceToStore(rootSector, workspace);
		rootSector.MapSector.StickyWorkspaceMonitorIndexMap =
			rootSector.MapSector.StickyWorkspaceMonitorIndexMap.SetItem(workspace.Id, [0]);

		SetStickyMonitorIndicesTransform sut = new(workspace.Id, [3]);

		// When we execute the transform
		var result = ctx.Store.Dispatch(sut);

		// Then the previous indices are replaced
		Assert.True(result.IsSuccessful);
		Assert.Equal([3], rootSector.MapSector.StickyWorkspaceMonitorIndexMap[workspace.Id]);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void EmptyIndices_Unpins(IContext ctx, MutableRootSector rootSector)
	{
		// Given a workspace which is currently sticky
		Workspace workspace = CreateWorkspace();
		AddWorkspaceToStore(rootSector, workspace);
		rootSector.MapSector.StickyWorkspaceMonitorIndexMap =
			rootSector.MapSector.StickyWorkspaceMonitorIndexMap.SetItem(workspace.Id, [0]);

		SetStickyMonitorIndicesTransform sut = new(workspace.Id, []);

		// When we execute the transform with no indices
		var result = ctx.Store.Dispatch(sut);

		// Then the workspace is unpinned (removed from the sticky map)
		Assert.True(result.IsSuccessful);
		Assert.False(rootSector.MapSector.StickyWorkspaceMonitorIndexMap.ContainsKey(workspace.Id));
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void DefaultWorkspaceId_UsesActiveWorkspace(IContext ctx, MutableRootSector rootSector)
	{
		// Given an active workspace
		Workspace workspace = CreateWorkspace();
		IMonitor monitor = CreateMonitor();
		PopulateMonitorWorkspaceMap(rootSector, monitor, workspace);

		SetStickyMonitorIndicesTransform sut = new(default, [2]);

		// When we execute the transform with the default workspace id
		var result = ctx.Store.Dispatch(sut);

		// Then the active workspace is pinned
		Assert.True(result.IsSuccessful);
		Assert.Equal([2], rootSector.MapSector.StickyWorkspaceMonitorIndexMap[workspace.Id]);
	}
}
