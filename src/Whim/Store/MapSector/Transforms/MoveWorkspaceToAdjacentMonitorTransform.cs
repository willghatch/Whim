namespace Whim;

/// <summary>
/// Moves the workspace with <paramref name="WorkspaceId"/> to the adjacent monitor.
///
/// The current monitor switches to another of its workspaces, and the target monitor gains the moved
/// workspace (focused) without losing the workspace it was already showing - that workspace simply
/// becomes hidden on the target monitor. This is not a swap: no workspace is moved onto the current
/// monitor from the target.
///
/// This fails if the moved workspace is the last workspace on the current monitor, since the current
/// monitor would have no other workspace to switch to.
///
/// If the moved workspace is pinned (sticky), its pin follows it to the target monitor.
/// </summary>
/// <param name="WorkspaceId">
/// The id of the workspace to move. Defaults to the active workspace.
/// </param>
/// <param name="Reverse">
/// When <see langword="true"/>, moves the workspace to the previous monitor, otherwise to the next.
/// Defaults to <see langword="false"/>.
/// </param>
/// <param name="FocusWorkspaceWindow">
/// When <see langword="true"/> (the default), focus follows the moved workspace to the adjacent
/// monitor. When <see langword="false"/>, focus stays on the current monitor's new workspace.
/// </param>
public record MoveWorkspaceToAdjacentMonitorTransform(
	WorkspaceId WorkspaceId = default,
	bool Reverse = false,
	bool FocusWorkspaceWindow = true
) : Transform
{
	internal override Result<Unit> Execute(IContext ctx, IInternalContext internalCtx, MutableRootSector rootSector)
	{
		WorkspaceId workspaceId = WorkspaceId.OrActiveWorkspace(ctx);

		// Get the monitor the workspace is currently on.
		Result<IMonitor> currentMonitorResult = ctx.Store.Pick(PickMonitorByWorkspace(workspaceId));
		if (!currentMonitorResult.TryGet(out IMonitor currentMonitor))
		{
			return Result.FromError<Unit>(currentMonitorResult.Error!);
		}

		// Get the adjacent monitor.
		Result<IMonitor> nextMonitorResult = ctx.Store.Pick(
			PickAdjacentMonitor(currentMonitor.Handle, reverse: Reverse)
		);
		if (!nextMonitorResult.TryGet(out IMonitor nextMonitor))
		{
			return Result.FromError<Unit>(nextMonitorResult.Error!);
		}

		if (currentMonitor.Equals(nextMonitor))
		{
			Logger.Debug($"Monitor {currentMonitor} is already the {(!Reverse ? "next" : "previous")} monitor");
			return Unit.Result;
		}

		// Find a workspace for the current monitor to switch to once the moved workspace leaves.
		Result<WorkspaceId> replacementResult = FindReplacementWorkspace(ctx, rootSector, currentMonitor, workspaceId);
		if (!replacementResult.TryGet(out WorkspaceId replacementId))
		{
			return Result.FromError<Unit>(replacementResult.Error!);
		}

		// If the moved workspace is pinned, move its pin to the target monitor so it is valid there.
		ImmutableArray<IMonitor> monitors = rootSector.MonitorSector.Monitors;
		RepinIfSticky(rootSector.MapSector, workspaceId, monitors.IndexOf(currentMonitor), monitors.IndexOf(nextMonitor));

		// Switch the current monitor to the replacement workspace. Because the moved workspace is not
		// shown elsewhere, this deactivates it (rather than swapping anything onto the current monitor).
		Result<Unit> replacementActivation = ctx.Store.Dispatch(
			new ActivateWorkspaceTransform(replacementId, currentMonitor.Handle, FocusWorkspaceWindow: false)
		);
		if (!replacementActivation.IsSuccessful)
		{
			return replacementActivation;
		}

		// Show the moved workspace on the target monitor, focused. The target's previous workspace
		// becomes hidden but stays assigned to that monitor.
		return ctx.Store.Dispatch(new ActivateWorkspaceTransform(workspaceId, nextMonitor.Handle, FocusWorkspaceWindow));
	}

	/// <summary>
	/// Finds a workspace, other than <paramref name="movedWorkspaceId"/>, which can be shown on
	/// <paramref name="currentMonitor"/> and is not already shown on another monitor. Returns an error
	/// if there is none - i.e. the moved workspace is the last workspace on the monitor.
	/// </summary>
	private static Result<WorkspaceId> FindReplacementWorkspace(
		IContext ctx,
		MutableRootSector rootSector,
		IMonitor currentMonitor,
		WorkspaceId movedWorkspaceId
	)
	{
		Result<IReadOnlyList<IWorkspace>> monitorWorkspacesResult = ctx.Store.Pick(
			PickStickyWorkspacesByMonitor(currentMonitor.Handle)
		);
		if (!monitorWorkspacesResult.TryGet(out IReadOnlyList<IWorkspace> monitorWorkspaces))
		{
			return Result.FromError<WorkspaceId>(monitorWorkspacesResult.Error!);
		}

		foreach (IWorkspace workspace in monitorWorkspaces)
		{
			if (workspace.Id == movedWorkspaceId)
			{
				continue;
			}

			if (rootSector.MapSector.MonitorWorkspaceMap.ContainsValue(workspace.Id))
			{
				// Already shown on some monitor; can't pull it here without disturbing that monitor.
				continue;
			}

			return workspace.Id;
		}

		return Result.FromError<WorkspaceId>(
			new WhimError(
				$"Cannot move workspace {movedWorkspaceId}: it is the last workspace on monitor {currentMonitor.Handle}"
			)
		);
	}

	/// <summary>
	/// If <paramref name="workspaceId"/> is pinned to <paramref name="fromIndex"/>, re-pin that entry
	/// to <paramref name="toIndex"/>. Unpinned workspaces are left free.
	/// </summary>
	private static void RepinIfSticky(MapSector mapSector, WorkspaceId workspaceId, int fromIndex, int toIndex)
	{
		if (!mapSector.StickyWorkspaceMonitorIndexMap.TryGetValue(workspaceId, out ImmutableArray<int> indices))
		{
			return;
		}

		SortedSet<int> updated = [];
		foreach (int index in indices)
		{
			updated.Add(index == fromIndex ? toIndex : index);
		}

		mapSector.StickyWorkspaceMonitorIndexMap = mapSector.StickyWorkspaceMonitorIndexMap.SetItem(
			workspaceId,
			[.. updated]
		);
	}
}
