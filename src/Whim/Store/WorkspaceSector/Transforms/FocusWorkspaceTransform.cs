namespace Whim;

/// <summary>
/// Focus the last focused window in the workspace with given <paramref name="WorkspaceId"/>.
///
/// NOTE: This does not update the workspace's <see cref="Workspace.LastFocusedWindowHandle"/>.
/// Instead, it queues a call to <see cref="IWindow.Focus"/>. If there is no last focused window but
/// the workspace has windows, the first window is focused. If the workspace has no windows, the
/// monitor's desktop will be focused.
/// </summary>
/// <param name="WorkspaceId"></param>
public record FocusWorkspaceTransform(WorkspaceId WorkspaceId) : BaseWorkspaceTransform(WorkspaceId)
{
	private protected override Result<Workspace> WorkspaceOperation(
		IContext ctx,
		IInternalContext internalCtx,
		MutableRootSector rootSector,
		Workspace workspace
	)
	{
		if (workspace.LastFocusedWindowHandle != default)
		{
			rootSector.WorkspaceSector.WindowHandleToFocus = workspace.LastFocusedWindowHandle;
			return workspace;
		}

		// There is no recorded last-focused window (e.g. the workspace was never interacted with).
		// Focus the first window in the workspace if there is one, so switching to it still gives a
		// window keyboard focus rather than dropping focus to the desktop.
		if (
			ctx.Store.Pick(PickWorkspaceWindowsInOrder(workspace.Id)).TryGet(out IReadOnlyList<IWindow> windows)
			&& windows.Count > 0
		)
		{
			rootSector.WorkspaceSector.WindowHandleToFocus = windows[0].Handle;
			return workspace;
		}

		Logger.Debug($"No windows in workspace {workspace.Name} to focus, focusing desktop");

		// Get the bounds of the monitor for this workspace.
		Result<IMonitor> monitorResult = ctx.Store.Pick(PickMonitorByWorkspace(workspace.Id));
		if (!monitorResult.TryGet(out IMonitor monitor))
		{
			return Result.FromError<Workspace>(monitorResult.Error!);
		}

		ctx.Store.Dispatch(new FocusMonitorDesktopTransform(monitor.Handle));
		return workspace;
	}
}
