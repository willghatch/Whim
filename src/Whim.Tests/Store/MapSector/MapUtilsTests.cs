namespace Whim.Tests;

public class MapUtilsTests
{
	[Fact]
	public void RemapStickyMonitorIndices_RemovedMonitor_MovesToLowestIndex()
	{
		// Given three monitors and a workspace pinned to the middle one (index 1)
		IMonitor m0 = CreateMonitor((HMONITOR)1);
		IMonitor m1 = CreateMonitor((HMONITOR)2);
		IMonitor m2 = CreateMonitor((HMONITOR)3);
		ImmutableArray<IMonitor> previous = [m0, m1, m2];
		// m1 is removed
		ImmutableArray<IMonitor> current = [m0, m2];

		WorkspaceId workspaceId = Guid.NewGuid();
		ImmutableDictionary<WorkspaceId, ImmutableArray<int>> stickyMap = ImmutableDictionary<
			WorkspaceId,
			ImmutableArray<int>
		>.Empty.SetItem(workspaceId, [1]);

		// When we remap
		var result = stickyMap.RemapStickyMonitorIndices(previous, current);

		// Then the workspace is pinned to the lowest index monitor (0)
		Assert.Equal([0], result[workspaceId]);
	}

	[Fact]
	public void RemapStickyMonitorIndices_SurvivingMonitor_FollowsItsNewIndex()
	{
		// Given a workspace pinned to the last monitor (index 2), and the first monitor is removed
		IMonitor m0 = CreateMonitor((HMONITOR)1);
		IMonitor m1 = CreateMonitor((HMONITOR)2);
		IMonitor m2 = CreateMonitor((HMONITOR)3);
		ImmutableArray<IMonitor> previous = [m0, m1, m2];
		ImmutableArray<IMonitor> current = [m1, m2];

		WorkspaceId workspaceId = Guid.NewGuid();
		ImmutableDictionary<WorkspaceId, ImmutableArray<int>> stickyMap = ImmutableDictionary<
			WorkspaceId,
			ImmutableArray<int>
		>.Empty.SetItem(workspaceId, [2]);

		// When we remap
		var result = stickyMap.RemapStickyMonitorIndices(previous, current);

		// Then the pin follows m2 to its new index (1), so the workspace stays on the same monitor
		Assert.Equal([1], result[workspaceId]);
	}

	[Fact]
	public void RemapStickyMonitorIndices_MultiplePins_RemovedGoesToZeroOthersFollow()
	{
		// Given a workspace pinned to the removed monitor (1) and a surviving one (2)
		IMonitor m0 = CreateMonitor((HMONITOR)1);
		IMonitor m1 = CreateMonitor((HMONITOR)2);
		IMonitor m2 = CreateMonitor((HMONITOR)3);
		ImmutableArray<IMonitor> previous = [m0, m1, m2];
		ImmutableArray<IMonitor> current = [m0, m2];

		WorkspaceId workspaceId = Guid.NewGuid();
		ImmutableDictionary<WorkspaceId, ImmutableArray<int>> stickyMap = ImmutableDictionary<
			WorkspaceId,
			ImmutableArray<int>
		>.Empty.SetItem(workspaceId, [1, 2]);

		// When we remap
		var result = stickyMap.RemapStickyMonitorIndices(previous, current);

		// Then the removed pin becomes 0 and the surviving pin follows m2 to index 1
		Assert.Equal([0, 1], result[workspaceId]);
	}

	[Fact]
	public void RemapStickyMonitorIndices_OnlyOutOfRangeIndices_RemovesEntry()
	{
		// Given a workspace whose only pin is out of range (orphaned)
		IMonitor m0 = CreateMonitor((HMONITOR)1);
		ImmutableArray<IMonitor> previous = [m0];
		ImmutableArray<IMonitor> current = [m0];

		WorkspaceId workspaceId = Guid.NewGuid();
		ImmutableDictionary<WorkspaceId, ImmutableArray<int>> stickyMap = ImmutableDictionary<
			WorkspaceId,
			ImmutableArray<int>
		>.Empty.SetItem(workspaceId, [5]);

		// When we remap
		var result = stickyMap.RemapStickyMonitorIndices(previous, current);

		// Then the entry is removed (equivalent to being shown on any monitor)
		Assert.False(result.ContainsKey(workspaceId));
	}

	[Fact]
	public void RemapStickyMonitorIndices_NoMonitors_ReturnsUnchanged()
	{
		// Given there are no current monitors
		IMonitor m0 = CreateMonitor((HMONITOR)1);
		ImmutableArray<IMonitor> previous = [m0];
		ImmutableArray<IMonitor> current = [];

		WorkspaceId workspaceId = Guid.NewGuid();
		ImmutableDictionary<WorkspaceId, ImmutableArray<int>> stickyMap = ImmutableDictionary<
			WorkspaceId,
			ImmutableArray<int>
		>.Empty.SetItem(workspaceId, [0]);

		// When we remap
		var result = stickyMap.RemapStickyMonitorIndices(previous, current);

		// Then the map is unchanged
		Assert.Equal([0], result[workspaceId]);
	}
}
