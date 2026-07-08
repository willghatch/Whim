namespace Whim;

/// <summary>
/// Moves <paramref name="WindowHandle"/> to the slot of the adjacent window in the workspace's
/// spatial window order (see <see cref="Pickers.PickWorkspaceWindowsInOrder"/>). Whether this swaps
/// the two windows or rotates them into place depends on the active layout engine's window insertion
/// behaviour.
/// </summary>
/// <param name="WorkspaceId">
/// The id of the workspace containing the window. Defaults to the active workspace.
/// </param>
/// <param name="Reverse">
/// When <see langword="true"/>, moves towards the previous window, otherwise the next. Defaults to
/// <see langword="false"/>.
/// </param>
/// <param name="WindowHandle">
/// The handle of the window to move. Defaults to the last focused window in the workspace.
/// </param>
public record SwapWindowInOrderTransform(
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

		if (ordered.Count < 2)
		{
			// There is nothing to swap with.
			return Unit.Result;
		}

		HWND currentHandle = WindowHandle;
		if (currentHandle == default)
		{
			currentHandle = ctx.Store.Pick(PickLastFocusedWindowHandle(workspaceId)).ValueOrDefault;
		}

		int idx = FocusWindowInOrderTransform.IndexOfHandle(ordered, currentHandle);
		if (idx == -1)
		{
			return Result.FromError<Unit>(
				new WhimError($"Window {currentHandle} is not part of the ordered windows in workspace {workspaceId}")
			);
		}

		IWindow target = ordered[(idx + (Reverse ? -1 : 1)).Mod(ordered.Count)];

		// Find the target window's normalized centre within the monitor, so the layout engine can
		// place the current window into the target's slot.
		Result<IMonitor> monitorResult = ctx.Store.Pick(PickMonitorByWorkspace(workspaceId));
		if (!monitorResult.TryGet(out IMonitor monitor))
		{
			return Result.FromError<Unit>(monitorResult.Error!);
		}

		Workspace workspace = rootSector.WorkspaceSector.Workspaces[workspaceId];
		IRectangle<int> targetRect = workspace.WindowPositions[target.Handle].LastWindowRectangle;
		IRectangle<int> workingArea = monitor.WorkingArea;

		Point<double> point = new()
		{
			X = (targetRect.X + (targetRect.Width / 2.0) - workingArea.X) / workingArea.Width,
			Y = (targetRect.Y + (targetRect.Height / 2.0) - workingArea.Y) / workingArea.Height,
		};

		Result<bool> moveResult = ctx.Store.Dispatch(
			new MoveWindowToPointInWorkspaceTransform(workspaceId, currentHandle, point)
		);
		if (!moveResult.TryGet(out _))
		{
			return Result.FromError<Unit>(moveResult.Error!);
		}

		return Unit.Result;
	}
}
