using System.Threading.Tasks;

namespace Whim;

/// <summary>
/// Transform for when the monitors have changed.
/// </summary>
internal record MonitorsChangedTransform : Transform
{
	internal override Result<Unit> Execute(
		IContext ctx,
		IInternalContext internalCtx,
		MutableRootSector mutableRootSector
	)
	{
		Logger.Debug($"Monitors changed");
		MonitorSector sector = mutableRootSector.MonitorSector;

		// Get the new monitors.
		ImmutableArray<IMonitor> previousMonitors = sector.Monitors;
		UpdateMonitorSector(ctx, internalCtx, mutableRootSector);

		List<IMonitor> unchangedMonitors = [];
		List<IMonitor> removedMonitors = [];
		List<IMonitor> addedMonitors = [];

		// For each monitor in the previous set, check if it's in the current set.
		foreach (IMonitor monitor in previousMonitors)
		{
			if (sector.Monitors.Contains(monitor))
			{
				unchangedMonitors.Add(monitor);
			}
			else
			{
				removedMonitors.Add(monitor);
			}
		}

		// For each monitor in the current set, check if it's in the previous set.
		for (int idx = 0; idx < sector.Monitors.Length; idx += 1)
		{
			IMonitor monitor = sector.Monitors[idx];
			if (!previousMonitors.Contains(monitor))
			{
				addedMonitors.Add(monitor);
			}

			if (monitor.IsPrimary)
			{
				sector.PrimaryMonitorHandle = monitor.Handle;
			}
		}

		MonitorsChangedEventArgs args = new()
		{
			UnchangedMonitors = unchangedMonitors,
			RemovedMonitors = removedMonitors,
			AddedMonitors = addedMonitors,
		};

		if (addedMonitors.Count != 0 || removedMonitors.Count != 0)
		{
			// Remember the workspaces of unplugged monitors before the pins are remapped away, so they
			// can be moved back if the monitor is reconnected.
			RememberUnpluggedMonitorWorkspaces(mutableRootSector, previousMonitors, removedMonitors);

			// Keep sticky monitor pins pointing at the same physical monitors as indices shift,
			// moving pins to removed monitors onto the lowest index monitor.
			mutableRootSector.MapSector.StickyWorkspaceMonitorIndexMap =
				mutableRootSector.MapSector.StickyWorkspaceMonitorIndexMap.RemapStickyMonitorIndices(
					previousMonitors,
					sector.Monitors
				);

			UpdateMapSector(ctx, mutableRootSector, addedMonitors, removedMonitors);
		}

		// Make sure the other monitor handles are set if they're unset.
		if (sector.ActiveMonitorHandle == (HMONITOR)0)
		{
			sector.ActiveMonitorHandle = sector.PrimaryMonitorHandle;
			sector.LastWhimActiveMonitorHandle = sector.PrimaryMonitorHandle;
		}

		sector.QueueEvent(args);

		return Unit.Result;
	}

	/// <summary>
	/// Records, for each removed monitor, the workspaces which were on it, so they can be moved back
	/// when the monitor is reconnected. Must run before the sticky pins are remapped, while they still
	/// point at the removed monitor. The primary monitor is skipped, as its name is not a stable
	/// identity (see <see cref="MonitorSector.UnpluggedMonitorWorkspaces"/>).
	/// </summary>
	private static void RememberUnpluggedMonitorWorkspaces(
		MutableRootSector rootSector,
		ImmutableArray<IMonitor> previousMonitors,
		List<IMonitor> removedMonitors
	)
	{
		MonitorSector monitorSector = rootSector.MonitorSector;
		MapSector mapSector = rootSector.MapSector;

		foreach (IMonitor removed in removedMonitors)
		{
			if (removed.IsPrimary)
			{
				continue;
			}

			int previousIndex = previousMonitors.IndexOf(removed);
			if (previousIndex < 0)
			{
				continue;
			}

			// Record the shown workspace first, followed by the other workspaces pinned to the monitor,
			// so the shown workspace is preferred when restoring.
			ImmutableArray<WorkspaceId>.Builder rememberedWorkspaces = ImmutableArray.CreateBuilder<WorkspaceId>();

			if (mapSector.MonitorWorkspaceMap.TryGetValue(removed.Handle, out WorkspaceId shownWorkspaceId))
			{
				rememberedWorkspaces.Add(shownWorkspaceId);
			}

			foreach ((WorkspaceId workspaceId, ImmutableArray<int> indices) in mapSector.StickyWorkspaceMonitorIndexMap)
			{
				if (indices.Contains(previousIndex) && !rememberedWorkspaces.Contains(workspaceId))
				{
					rememberedWorkspaces.Add(workspaceId);
				}
			}

			if (rememberedWorkspaces.Count > 0)
			{
				monitorSector.UnpluggedMonitorWorkspaces = monitorSector.UnpluggedMonitorWorkspaces.SetItem(
					removed.Name,
					rememberedWorkspaces.ToImmutable()
				);
			}
		}
	}

	/// <summary>
	/// Moves the workspaces which were on a now-reconnected monitor back onto it, pinning them to it
	/// and showing the one which was previously shown. Workspaces which no longer exist, or which are
	/// currently shown on another monitor, are skipped. Returns <see langword="true"/> if a workspace
	/// was restored and shown, so the caller does not also create a new workspace for the monitor.
	/// </summary>
	private static bool TryRestoreUnpluggedMonitorWorkspaces(
		MutableRootSector rootSector,
		IMonitor monitor,
		int monitorIndex
	)
	{
		MonitorSector monitorSector = rootSector.MonitorSector;
		MapSector mapSector = rootSector.MapSector;
		WorkspaceSector workspaceSector = rootSector.WorkspaceSector;

		if (!monitorSector.UnpluggedMonitorWorkspaces.TryGetValue(monitor.Name, out ImmutableArray<WorkspaceId> remembered))
		{
			return false;
		}

		// The memory is one-shot: consume it whether or not anything could be restored.
		monitorSector.UnpluggedMonitorWorkspaces = monitorSector.UnpluggedMonitorWorkspaces.Remove(monitor.Name);

		List<WorkspaceId> restorable = [];
		foreach (WorkspaceId workspaceId in remembered)
		{
			// Skip workspaces which have since been deleted.
			if (!workspaceSector.Workspaces.ContainsKey(workspaceId))
			{
				continue;
			}

			// Skip workspaces the user has since moved onto another monitor.
			if (mapSector.GetMonitorByWorkspace(workspaceId) != (HMONITOR)0)
			{
				continue;
			}

			restorable.Add(workspaceId);
		}

		if (restorable.Count == 0)
		{
			return false;
		}

		// Pin every restorable workspace back to the reconnected monitor.
		foreach (WorkspaceId workspaceId in restorable)
		{
			mapSector.StickyWorkspaceMonitorIndexMap = mapSector.StickyWorkspaceMonitorIndexMap.SetItem(
				workspaceId,
				[monitorIndex]
			);
		}

		// Show the previously shown workspace (stored first), falling back to the first survivor.
		mapSector.MonitorWorkspaceMap = mapSector.MonitorWorkspaceMap.SetItem(monitor.Handle, restorable[0]);
		return true;
	}

	private static void UpdateMonitorSector(IContext ctx, IInternalContext internalCtx, MutableRootSector rootSector)
	{
		MonitorSector sector = rootSector.MonitorSector;

		sector.Monitors = MonitorUtils.GetCurrentMonitors(internalCtx);

		foreach (IMonitor m in sector.Monitors)
		{
			if (m.IsPrimary)
			{
				sector.PrimaryMonitorHandle = m.Handle;
				sector.ActiveMonitorHandle = m.Handle;
				sector.LastWhimActiveMonitorHandle = m.Handle;
				break;
			}
		}
	}

	private static void UpdateMapSector(
		IContext ctx,
		MutableRootSector rootSector,
		List<IMonitor> addedMonitors,
		List<IMonitor> removedMonitors
	)
	{
		MapSector mapSector = rootSector.MapSector;
		MonitorSector monitorSector = rootSector.MonitorSector;
		WorkspaceSector workspaceSector = rootSector.WorkspaceSector;

		if (!workspaceSector.HasInitialized)
		{
			return;
		}

		monitorSector.MonitorsChangingTasks++;

		// Deactivate all workspaces.
		foreach (IWorkspace visibleWorkspace in ctx.Store.Pick(PickAllActiveWorkspaces()))
		{
			ctx.Store.Dispatch(new DeactivateWorkspaceTransform(visibleWorkspace.Id));
		}

		// If a monitor was removed, remove the workspace from the map.
		foreach (IMonitor monitor in removedMonitors)
		{
			if (!ctx.Store.Pick(PickWorkspaceByMonitor(monitor.Handle)).TryGet(out IWorkspace workspace))
			{
				continue;
			}

			ctx.Store.Dispatch(new DeactivateWorkspaceTransform(workspace.Id));
			mapSector.MonitorWorkspaceMap = mapSector.MonitorWorkspaceMap.Remove(monitor.Handle);
		}

		// For each added monitor, move back the workspaces it had when it was unplugged. If there are
		// none to restore, give it its own new workspace, pinned (sticky) to that monitor.
		foreach (IMonitor monitor in addedMonitors)
		{
			int monitorIndex = monitorSector.Monitors.IndexOf(monitor);
			if (monitorIndex < 0)
			{
				continue;
			}

			if (TryRestoreUnpluggedMonitorWorkspaces(rootSector, monitor, monitorIndex))
			{
				continue;
			}

			Result<WorkspaceId> addWorkspaceResult = ctx.Store.Dispatch(
				new AddWorkspaceTransform(MonitorIndices: [monitorIndex])
			);
			if (addWorkspaceResult.TryGet(out WorkspaceId newWorkspaceId))
			{
				mapSector.MonitorWorkspaceMap = mapSector.MonitorWorkspaceMap.SetItem(monitor.Handle, newWorkspaceId);
			}
		}

		// Hack to only accept window events after Windows has been given a chance to stop moving
		// windows around after a monitor change.
		ctx.NativeManager.TryEnqueue(async () =>
		{
			await Task.Delay(monitorSector.MonitorsChangedDelay).ConfigureAwait(true);

			monitorSector.MonitorsChangingTasks--;
			if (monitorSector.MonitorsChangingTasks > 0)
			{
				Logger.Debug("Monitors changed: More tasks are pending");
				return;
			}

			Logger.Debug("Cleared AreMonitorsChanging");

			// For each workspace which is active in a monitor, do a layout.
			// This will handle cases when the monitor's properties have changed.
			ctx.Store.Dispatch(new LayoutAllActiveWorkspacesTransform());
		});
	}
}
