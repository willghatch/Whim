namespace Whim;

/// <summary>
/// Removes the workspace currently shown on the given monitor. This fails if the workspace is the
/// last workspace which can be shown on that monitor, to avoid leaving the monitor with no
/// workspace to display.
/// </summary>
/// <param name="MonitorHandle">
/// The handle of the monitor whose workspace should be removed. Defaults to the active monitor.
/// </param>
public record RemoveWorkspaceOnMonitorTransform(HMONITOR MonitorHandle = default) : Transform
{
	internal override Result<Unit> Execute(
		IContext ctx,
		IInternalContext internalCtx,
		MutableRootSector mutableRootSector
	)
	{
		HMONITOR monitorHandle =
			MonitorHandle == default ? mutableRootSector.MonitorSector.ActiveMonitorHandle : MonitorHandle;

		// Get the workspace currently shown on the monitor.
		Result<IWorkspace> workspaceResult = ctx.Store.Pick(PickWorkspaceByMonitor(monitorHandle));
		if (!workspaceResult.TryGet(out IWorkspace workspace))
		{
			return Result.FromError<Unit>(workspaceResult.Error!);
		}

		// Get all the workspaces which can be shown on the monitor. If the current workspace is the
		// only one, we cannot remove it without leaving the monitor empty.
		Result<IReadOnlyList<IWorkspace>> monitorWorkspacesResult = ctx.Store.Pick(
			PickStickyWorkspacesByMonitor(monitorHandle)
		);
		if (!monitorWorkspacesResult.TryGet(out IReadOnlyList<IWorkspace> monitorWorkspaces))
		{
			return Result.FromError<Unit>(monitorWorkspacesResult.Error!);
		}

		if (monitorWorkspaces.Count <= 1)
		{
			return Result.FromError<Unit>(
				new WhimError($"Cannot remove workspace {workspace.Id}: it is the last workspace on the monitor")
			);
		}

		return ctx.Store.Dispatch(new RemoveWorkspaceByIdTransform(workspace.Id));
	}
}
