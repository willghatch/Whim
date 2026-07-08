namespace Whim;

internal static class MapUtils
{
	public static HMONITOR GetMonitorByWorkspace(this IMapSector sector, WorkspaceId searchWorkspaceId)
	{
		foreach ((HMONITOR monitor, WorkspaceId workspace) in sector.MonitorWorkspaceMap)
		{
			if (workspace.Equals(searchWorkspaceId))
			{
				return monitor;
			}
		}

		return default;
	}

	/// <summary>
	/// Remaps the sticky monitor indices in <paramref name="stickyMap"/> after the set of monitors
	/// has changed, so that each pin continues to refer to the same physical monitor.
	///
	/// Each stored index refers to a position in the monitor array, which shifts when monitors are
	/// added or removed. Every index is remapped by identity: the monitor previously at that index is
	/// located in <paramref name="currentMonitors"/> and its new index is used. Pins to monitors which
	/// no longer exist (e.g. a removed monitor) are moved to <paramref name="fallbackIndex"/>, the
	/// lowest index monitor. Indices which were already out of range are dropped; if a workspace ends
	/// up with no valid indices, its entry is removed (allowing it on any monitor).
	/// </summary>
	/// <param name="stickyMap">The workspace-to-monitor-indices map to remap.</param>
	/// <param name="previousMonitors">The monitors before the change.</param>
	/// <param name="currentMonitors">The monitors after the change.</param>
	/// <param name="fallbackIndex">The index to use for pins whose monitor was removed. Defaults to 0.</param>
	/// <returns>The remapped map.</returns>
	public static ImmutableDictionary<WorkspaceId, ImmutableArray<int>> RemapStickyMonitorIndices(
		this ImmutableDictionary<WorkspaceId, ImmutableArray<int>> stickyMap,
		ImmutableArray<IMonitor> previousMonitors,
		ImmutableArray<IMonitor> currentMonitors,
		int fallbackIndex = 0
	)
	{
		// Without any current monitors there is nothing valid to remap to.
		if (currentMonitors.IsDefaultOrEmpty)
		{
			return stickyMap;
		}

		ImmutableDictionary<WorkspaceId, ImmutableArray<int>> result = stickyMap;

		foreach ((WorkspaceId workspaceId, ImmutableArray<int> indices) in stickyMap)
		{
			SortedSet<int> newIndices = [];
			foreach (int index in indices)
			{
				if (index < 0 || index >= previousMonitors.Length)
				{
					// The index was already invalid; drop it.
					continue;
				}

				int newIndex = currentMonitors.IndexOf(previousMonitors[index]);
				newIndices.Add(newIndex == -1 ? fallbackIndex : newIndex);
			}

			if (newIndices.Count == 0)
			{
				result = result.Remove(workspaceId);
			}
			else
			{
				result = result.SetItem(workspaceId, [.. newIndices]);
			}
		}

		return result;
	}
}
