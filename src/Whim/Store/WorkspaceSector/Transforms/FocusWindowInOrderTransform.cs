namespace Whim;

/// <summary>
/// Focuses the window adjacent to <paramref name="WindowHandle"/> in the workspace's spatial window
/// order (see <see cref="Pickers.PickWorkspaceWindowsInOrder"/>). This cycles focus through all
/// windows in the workspace, wrapping around at the ends.
/// </summary>
/// <param name="WorkspaceId">
/// The id of the workspace to focus the window in. Defaults to the active workspace.
/// </param>
/// <param name="Reverse">
/// When <see langword="true"/>, focuses the previous window, otherwise the next. Defaults to
/// <see langword="false"/>.
/// </param>
/// <param name="WindowHandle">
/// The handle of the window to move focus from. Defaults to the last focused window in the workspace.
/// </param>
public record FocusWindowInOrderTransform(
	WorkspaceId WorkspaceId = default,
	bool Reverse = false,
	HWND WindowHandle = default
) : Transform
{
	internal override Result<Unit> Execute(IContext ctx, IInternalContext internalCtx, MutableRootSector rootSector)
	{
		WorkspaceId workspaceId = WorkspaceId.OrActiveWorkspace(ctx);

		Result<IReadOnlyList<IWindow>> orderedResult = ctx.Store.Pick(PickWorkspaceWindowsInOrder(workspaceId));
		if (!orderedResult.TryGet(out IReadOnlyList<IWindow> ordered))
		{
			return Result.FromError<Unit>(orderedResult.Error!);
		}

		if (ordered.Count == 0)
		{
			return Result.FromError<Unit>(new WhimError($"No windows to focus in workspace {workspaceId}"));
		}

		HWND currentHandle = WindowHandle;
		if (currentHandle == default)
		{
			currentHandle = ctx.Store.Pick(PickLastFocusedWindowHandle(workspaceId)).ValueOrDefault;
		}

		int idx = IndexOfHandle(ordered, currentHandle);

		// If the current window isn't part of the order, start from the first window.
		int targetIdx = idx == -1 ? 0 : (idx + (Reverse ? -1 : 1)).Mod(ordered.Count);

		return ctx.Store.Dispatch(new FocusWindowTransform(ordered[targetIdx].Handle));
	}

	internal static int IndexOfHandle(IReadOnlyList<IWindow> windows, HWND handle)
	{
		for (int i = 0; i < windows.Count; i++)
		{
			if (windows[i].Handle == handle)
			{
				return i;
			}
		}

		return -1;
	}
}
