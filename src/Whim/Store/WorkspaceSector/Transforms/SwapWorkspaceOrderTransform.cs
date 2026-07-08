namespace Whim;

/// <summary>
/// Swaps the position of the workspace with <paramref name="WorkspaceId"/> with the adjacent
/// workspace on the same monitor in the workspace order. The workspaces which are shown on each
/// monitor are unchanged - only their order changes.
/// </summary>
/// <param name="WorkspaceId">
/// The id of the workspace to reorder. Defaults to the active workspace.
/// </param>
/// <param name="Reverse">
/// When <see langword="true"/>, swaps with the previous workspace, otherwise with the next.
/// Defaults to <see langword="false"/>.
/// </param>
public record SwapWorkspaceOrderTransform(WorkspaceId WorkspaceId = default, bool Reverse = false) : Transform
{
	internal override Result<Unit> Execute(
		IContext ctx,
		IInternalContext internalCtx,
		MutableRootSector mutableRootSector
	)
	{
		WorkspaceId workspaceId = WorkspaceId.OrActiveWorkspace(ctx);

		// Find the monitor the workspace is currently shown on.
		Result<IMonitor> monitorResult = ctx.Store.Pick(PickMonitorByWorkspace(workspaceId));
		if (!monitorResult.TryGet(out IMonitor monitor))
		{
			return Result.FromError<Unit>(monitorResult.Error!);
		}

		// Get the ordered workspaces which can be shown on that monitor.
		Result<IReadOnlyList<IWorkspace>> workspacesResult = ctx.Store.Pick(
			PickStickyWorkspacesByMonitor(monitor.Handle)
		);
		if (!workspacesResult.TryGet(out IReadOnlyList<IWorkspace> monitorWorkspaces))
		{
			return Result.FromError<Unit>(workspacesResult.Error!);
		}

		int idx = -1;
		for (int i = 0; i < monitorWorkspaces.Count; i++)
		{
			if (monitorWorkspaces[i].Id == workspaceId)
			{
				idx = i;
				break;
			}
		}

		if (idx == -1)
		{
			return Result.FromError<Unit>(StoreErrors.WorkspaceNotFound(workspaceId));
		}

		if (monitorWorkspaces.Count < 2)
		{
			// There is nothing to swap with.
			return Unit.Result;
		}

		int delta = Reverse ? -1 : 1;
		WorkspaceId adjacentId = monitorWorkspaces[(idx + delta).Mod(monitorWorkspaces.Count)].Id;

		// Swap the two workspaces' positions in the global order.
		WorkspaceSector sector = mutableRootSector.WorkspaceSector;
		int globalIdx = sector.WorkspaceOrder.IndexOf(workspaceId);
		int globalAdjacentIdx = sector.WorkspaceOrder.IndexOf(adjacentId);

		if (globalIdx == -1 || globalAdjacentIdx == -1)
		{
			return Result.FromError<Unit>(StoreErrors.WorkspaceNotFound(workspaceId));
		}

		ImmutableArray<WorkspaceId>.Builder builder = sector.WorkspaceOrder.ToBuilder();
		builder[globalIdx] = adjacentId;
		builder[globalAdjacentIdx] = workspaceId;
		sector.WorkspaceOrder = builder.ToImmutable();

		sector.QueueEvent(new WorkspaceOrderChangedEventArgs() { WorkspaceOrder = sector.WorkspaceOrder });

		return Unit.Result;
	}
}
