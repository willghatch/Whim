namespace Whim;

/// <summary>
/// Sets the indices of the monitors that the workspace with <paramref name="WorkspaceId"/> is
/// allowed (sticky) to be shown on.
///
/// This is the runtime equivalent of specifying <c>monitors</c> when creating a workspace, allowing
/// a workspace to be pinned to specific monitors dynamically.
/// </summary>
/// <param name="WorkspaceId">
/// The id of the workspace to update. Defaults to the active workspace.
/// </param>
/// <param name="MonitorIndices">
/// The indices of the monitors the workspace is allowed to be shown on. An empty collection unpins
/// the workspace, allowing it to be shown on any monitor.
/// </param>
public record SetStickyMonitorIndicesTransform(WorkspaceId WorkspaceId, IReadOnlyList<int> MonitorIndices) : Transform
{
	internal override Result<Unit> Execute(
		IContext ctx,
		IInternalContext internalCtx,
		MutableRootSector mutableRootSector
	)
	{
		WorkspaceId workspaceId = WorkspaceId.OrActiveWorkspace(ctx);

		if (!mutableRootSector.WorkspaceSector.Workspaces.ContainsKey(workspaceId))
		{
			return Result.FromError<Unit>(StoreErrors.WorkspaceNotFound(workspaceId));
		}

		MapSector mapSector = mutableRootSector.MapSector;

		if (MonitorIndices.Count == 0)
		{
			// An empty set of indices means the workspace is no longer sticky.
			mapSector.StickyWorkspaceMonitorIndexMap = mapSector.StickyWorkspaceMonitorIndexMap.Remove(workspaceId);
		}
		else
		{
			mapSector.StickyWorkspaceMonitorIndexMap = mapSector.StickyWorkspaceMonitorIndexMap.SetItem(
				workspaceId,
				[.. MonitorIndices]
			);
		}

		return Unit.Result;
	}
}
