namespace Whim;

/// <summary>
/// Moves the workspace with <paramref name="WorkspaceId"/> to the adjacent monitor. The workspace
/// currently shown on the adjacent monitor is swapped onto the workspace's original monitor.
///
/// If either workspace is pinned (sticky) to its monitor, its pin follows the move so that the
/// workspace remains valid on - and stays pinned to - the monitor it ends up on.
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
/// monitor. When <see langword="false"/>, focus stays on the currently active workspace.
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

		ImmutableArray<IMonitor> monitors = rootSector.MonitorSector.Monitors;
		int currentIndex = monitors.IndexOf(currentMonitor);
		int nextIndex = monitors.IndexOf(nextMonitor);

		// If either workspace is pinned, move its pin with it so the swap is valid and stays pinned.
		// This must happen before activating, since activation rejects monitors a workspace is not
		// sticky to.
		RepinIfSticky(rootSector.MapSector, workspaceId, currentIndex, nextIndex);

		if (ctx.Store.Pick(PickWorkspaceByMonitor(nextMonitor.Handle)).TryGet(out IWorkspace displacedWorkspace))
		{
			RepinIfSticky(rootSector.MapSector, displacedWorkspace.Id, nextIndex, currentIndex);
		}

		// Activating the workspace on the adjacent monitor swaps the two monitors' workspaces.
		return ctx.Store.Dispatch(
			new ActivateWorkspaceTransform(workspaceId, nextMonitor.Handle, FocusWorkspaceWindow)
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
