namespace Whim;

/// <summary>
/// Moves the workspace with <paramref name="WorkspaceId"/> to the adjacent monitor. The workspace
/// currently shown on the adjacent monitor is swapped onto the workspace's original monitor.
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

		// Activating the workspace on the adjacent monitor swaps the two monitors' workspaces.
		return ctx.Store.Dispatch(
			new ActivateWorkspaceTransform(workspaceId, nextMonitor.Handle, FocusWorkspaceWindow)
		);
	}
}
