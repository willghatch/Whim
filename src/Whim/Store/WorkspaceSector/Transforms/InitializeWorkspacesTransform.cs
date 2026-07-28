using System.Linq;

namespace Whim;

/// <summary>
/// Initializes the state with the saved workspaces, and adds windows.
/// </summary>
internal record InitializeWorkspacesTransform : Transform
{
	internal override Result<Unit> Execute(IContext ctx, IInternalContext internalCtx, MutableRootSector rootSector)
	{
		CreatePreInitializationWorkspaces(ctx, rootSector);
		ShowHiddenSavedWindows(ctx, internalCtx);
		PopulatedSavedWorkspaces(ctx, internalCtx, rootSector);
		DeactivateWorkspacesWithoutMonitors(ctx, rootSector);

		return Unit.Result;
	}

	/// <summary>
	/// Create the workspaces which were specified prior to initialization.
	/// </summary>
	/// <param name="ctx"></param>
	/// <param name="rootSector"></param>
	private static void CreatePreInitializationWorkspaces(IContext ctx, MutableRootSector rootSector)
	{
		WorkspaceSector workspaceSector = rootSector.WorkspaceSector;

		workspaceSector.HasInitialized = true;
		foreach (WorkspaceToCreate w in workspaceSector.WorkspacesToCreate)
		{
			ctx.Store.Dispatch(
				new AddWorkspaceTransform(w.Name, w.CreateLeafLayoutEngines, w.WorkspaceId, w.MonitorIndices)
			);
		}

		workspaceSector.WorkspacesToCreate = workspaceSector.WorkspacesToCreate.Clear();
	}

	/// <summary>
	/// Show the windows in the saved state which are hidden.
	///
	/// Whim hides the windows of a workspace which is not shown on a monitor. If a previous Whim
	/// instance did not show them again - because it crashed, or because the saved workspace's name
	/// no longer matches a configured workspace - then they are still hidden, which makes them
	/// invisible to the user, and to <see cref="ICoreNativeManager.IsStandardWindow"/>, and thus to
	/// <see cref="WindowAddedTransform"/>. Showing them here means they can be added to a workspace
	/// instead of being lost.
	///
	/// Only the windows which Whim itself hid are shown - applications which close to the tray keep
	/// hidden windows around, and must be left alone.
	/// </summary>
	/// <param name="ctx"></param>
	/// <param name="internalCtx"></param>
	private static void ShowHiddenSavedWindows(IContext ctx, IInternalContext internalCtx)
	{
		foreach (SavedWorkspace savedWorkspace in internalCtx.CoreSavedStateManager.SavedState?.Workspaces ?? [])
		{
			foreach (SavedWindow savedWindow in savedWorkspace.Windows)
			{
				HWND hwnd = (HWND)savedWindow.Handle;

				if (!internalCtx.CoreNativeManager.IsWindow(hwnd))
				{
					continue;
				}

				if (internalCtx.CoreNativeManager.IsWindowVisible(hwnd))
				{
					continue;
				}

				Logger.Information(
					$"Showing hidden window {savedWindow.Handle} from the saved workspace {savedWorkspace.Name}"
				);
				ctx.NativeManager.ShowWindowNoActivate(hwnd);
			}
		}
	}

	/// <summary>
	/// Populate the existing workspaces with their saved windows, where possible.
	/// </summary>
	/// <param name="ctx"></param>
	/// <param name="internalCtx"></param>
	/// <param name="rootSector"></param>
	private static void PopulatedSavedWorkspaces(
		IContext ctx,
		IInternalContext internalCtx,
		MutableRootSector rootSector
	)
	{
		WorkspaceSector workspaceSector = rootSector.WorkspaceSector;
		WindowSector windowSector = rootSector.WindowSector;
		MapSector mapSector = rootSector.MapSector;

		windowSector.StartupWindows = [.. internalCtx.CoreNativeManager.GetAllWindows()];

		// Add the saved windows at their saved locations inside their saved workspaces.
		// Other windows are routed to the monitor they're on.
		List<HWND> processedWindows = [];

		// Route windows to their saved workspaces.
		foreach (SavedWorkspace savedWorkspace in internalCtx.CoreSavedStateManager.SavedState?.Workspaces ?? [])
		{
			Workspace? workspace = workspaceSector.Workspaces.Values.FirstOrDefault(w => w.Name == savedWorkspace.Name);

			if (workspace == null)
			{
				Logger.Information($"Could not find workspace {savedWorkspace.Name}");
				continue;
			}

			PopulateSavedWindows(ctx, windowSector, mapSector, processedWindows, savedWorkspace, workspace);
		}

		// Activate the workspaces before we add the unprocessed windows to make sure we have a workspace for each monitor.
		ActivateWorkspaces(ctx, rootSector);

		// Route the rest of the windows to the monitor they're on.
		// Don't route to the active workspace while we're adding all the windows.

		// Add all existing windows.
		foreach (HWND hwnd in internalCtx.CoreNativeManager.GetAllWindows())
		{
			if (processedWindows.Contains(hwnd))
			{
				continue;
			}

			ctx.Store.Dispatch(new WindowAddedTransform(hwnd, RouterOptions.RouteToLaunchedWorkspace));
		}
	}

	private static void PopulateSavedWindows(
		IContext ctx,
		WindowSector windowSector,
		MapSector mapSector,
		List<HWND> processedWindows,
		SavedWorkspace savedWorkspace,
		Workspace workspace
	)
	{
		foreach (SavedWindow savedWindow in savedWorkspace.Windows)
		{
			HWND hwnd = (HWND)savedWindow.Handle;
			processedWindows.Add(hwnd);
			if (!ctx.CreateWindow(hwnd).TryGet(out IWindow window))
			{
				Logger.Information($"Could not find window with handle {savedWindow.Handle}");
				continue;
			}

			mapSector.WindowWorkspaceMap = mapSector.WindowWorkspaceMap.SetItem(window.Handle, workspace.Id);
			windowSector.Windows = windowSector.Windows.Add(window.Handle, window);

			ctx.Store.Dispatch(
				new MoveWindowToPointInWorkspaceTransform(workspace.Id, window.Handle, savedWindow.Rectangle.Center)
			);

			windowSector.QueueEvent(new WindowAddedEventArgs() { Window = window });
		}
	}

	private static void ActivateWorkspaces(IContext ctx, MutableRootSector rootSector)
	{
		WorkspaceSector workspaceSector = rootSector.WorkspaceSector;
		MonitorSector monitorSector = rootSector.MonitorSector;
		MapSector mapSector = rootSector.MapSector;

		// Assign workspaces to monitors.
		List<HMONITOR> processedMonitors = [];
		for (int idx = 0; idx < workspaceSector.WorkspaceOrder.Length; idx++)
		{
			WorkspaceId workspaceId = workspaceSector.WorkspaceOrder[idx];

			if (
				!ctx.Store.Pick(PickStickyMonitorsByWorkspace(workspaceId)).TryGet(out IReadOnlyList<HMONITOR> monitors)
			)
			{
				continue;
			}

			foreach (HMONITOR monitor in monitors)
			{
				if (!processedMonitors.Contains(monitor))
				{
					processedMonitors.Add(monitor);
					ctx.Store.Dispatch(new ActivateWorkspaceTransform(workspaceId, monitor));
					break;
				}
			}
		}

		// Create a workspace pinned to each monitor which does not yet have one, so that
		// by default every monitor owns a workspace instead of sharing unpinned ones.
		for (int monitorIndex = 0; monitorIndex < monitorSector.Monitors.Length; monitorIndex++)
		{
			HMONITOR monitor = monitorSector.Monitors[monitorIndex].Handle;
			if (processedMonitors.Contains(monitor))
			{
				continue;
			}

			Result<WorkspaceId> addResult = ctx.Store.Dispatch(
				new AddWorkspaceTransform(
					MonitorIndices: [monitorIndex]
				)
			);
			if (addResult.TryGet(out WorkspaceId workspaceId))
			{
				ctx.Store.Dispatch(new ActivateWorkspaceTransform(workspaceId, monitor));
			}
		}
	}

	/// <summary>
	/// Hide the windows of the workspaces which are not shown on a monitor.
	///
	/// The windows shown by <see cref="ShowHiddenSavedWindows"/> which end up in a workspace which
	/// is not shown on a monitor would otherwise float on top of the shown workspaces, unmanaged.
	/// Deactivating the workspace hides them, while keeping them tracked, so they can be reached by
	/// switching to the workspace.
	/// </summary>
	/// <param name="ctx"></param>
	/// <param name="rootSector"></param>
	private static void DeactivateWorkspacesWithoutMonitors(IContext ctx, MutableRootSector rootSector)
	{
		ImmutableHashSet<WorkspaceId> shownWorkspaceIds = [.. rootSector.MapSector.MonitorWorkspaceMap.Values];

		// Snapshot the workspaces, as deactivating a workspace updates the store.
		foreach (WorkspaceId workspaceId in rootSector.WorkspaceSector.Workspaces.Keys.ToArray())
		{
			if (shownWorkspaceIds.Contains(workspaceId))
			{
				continue;
			}

			ctx.Store.Dispatch(new DeactivateWorkspaceTransform(workspaceId));
		}
	}
}
