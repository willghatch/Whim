using System.Linq;

namespace Whim;

/// <summary>
/// Moves the windows of all the workspaces which are not shown on a monitor into the workspaces
/// which are shown on a monitor, and lays out the affected workspaces.
///
/// Windows in workspaces which are not shown on a monitor are hidden, so they cannot be managed by
/// Windows once Whim is no longer running. This ensures every window ends up in a workspace which
/// is shown on a monitor.
///
/// A workspace which is sticky to specific monitors is consolidated into the workspace shown on one
/// of those monitors.
/// </summary>
public record ConsolidateWorkspacesTransform : Transform
{
	internal override Result<Unit> Execute(IContext ctx, IInternalContext internalCtx, MutableRootSector rootSector)
	{
		Logger.Debug("Consolidating workspaces");

		ImmutableHashSet<WorkspaceId> activeWorkspaceIds = [.. rootSector.MapSector.MonitorWorkspaceMap.Values];
		HashSet<WorkspaceId> workspacesToLayout = [];

		// Snapshot the order, as consolidating a workspace dispatches transforms which update the store.
		foreach (WorkspaceId sourceWorkspaceId in rootSector.WorkspaceSector.WorkspaceOrder.ToArray())
		{
			if (activeWorkspaceIds.Contains(sourceWorkspaceId))
			{
				continue;
			}

			ConsolidateWorkspace(ctx, rootSector, sourceWorkspaceId, workspacesToLayout);
		}

		foreach (WorkspaceId targetWorkspaceId in workspacesToLayout)
		{
			ctx.Store.Dispatch(new DoWorkspaceLayoutTransform(targetWorkspaceId));
		}

		return Unit.Result;
	}

	private static void ConsolidateWorkspace(
		IContext ctx,
		MutableRootSector rootSector,
		WorkspaceId sourceWorkspaceId,
		HashSet<WorkspaceId> workspacesToLayout
	)
	{
		// Deliberately not using MoveWindowToWorkspaceTransform: when the source workspace is not
		// shown on a monitor, it activates the target workspace on the active monitor, which can
		// pull the target workspace onto a different monitor.
		Result<HMONITOR> targetMonitorResult = ctx.Store.Pick(PickValidMonitorByWorkspace(sourceWorkspaceId));
		if (!targetMonitorResult.TryGet(out HMONITOR targetMonitorHandle))
		{
			Logger.Debug($"No valid monitor for workspace {sourceWorkspaceId}, skipping");
			return;
		}

		Result<IWorkspace> targetWorkspaceResult = ctx.Store.Pick(PickWorkspaceByMonitor(targetMonitorHandle));
		if (!targetWorkspaceResult.TryGet(out IWorkspace targetWorkspace))
		{
			Logger.Debug($"No workspace shown on monitor {targetMonitorHandle}, skipping {sourceWorkspaceId}");
			return;
		}

		if (targetWorkspace.Id == sourceWorkspaceId)
		{
			Logger.Debug($"Workspace {sourceWorkspaceId} is already shown on monitor {targetMonitorHandle}, skipping");
			return;
		}

		Result<IEnumerable<IWindow>> sourceWindowsResult = ctx.Store.Pick(PickWorkspaceWindows(sourceWorkspaceId));
		if (!sourceWindowsResult.TryGet(out IEnumerable<IWindow> sourceWindows))
		{
			Logger.Debug($"Could not get the windows of workspace {sourceWorkspaceId}, skipping");
			return;
		}

		foreach (IWindow window in sourceWindows.ToArray())
		{
			Logger.Debug($"Consolidating window {window.Handle} into workspace {targetWorkspace.Id}");

			rootSector.MapSector.WindowWorkspaceMap = rootSector.MapSector.WindowWorkspaceMap.SetItem(
				window.Handle,
				targetWorkspace.Id
			);

			ctx.Store.Dispatch(new RemoveWindowFromWorkspaceTransform(sourceWorkspaceId, window) { SkipDoLayout = true });
			ctx.Store.Dispatch(new AddWindowToWorkspaceTransform(targetWorkspace.Id, window) { SkipDoLayout = true });

			workspacesToLayout.Add(targetWorkspace.Id);
		}
	}
}
