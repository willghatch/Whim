using System.Linq;
using FluentAssertions;
using static Whim.TestUtils.MonitorTestUtils;

namespace Whim.Tests;

public class MonitorsChangedTransformTests
{
	private static readonly (RECT Rect, HMONITOR Handle) LeftTopMonitorSetup = (
		new RECT()
		{
			left = 0,
			top = 0,
			right = 1920,
			bottom = 1080,
		},
		(HMONITOR)1
	);

	private static readonly (RECT Rect, HMONITOR Handle) LeftBottomMonitorSetup = (
		new RECT()
		{
			left = 0,
			top = 1080,
			right = 1920,
			bottom = 2160,
		},
		(HMONITOR)2
	);

	private static readonly (RECT Rect, HMONITOR Handle) RightMonitorSetup = (
		new RECT()
		{
			left = 1920,
			top = 0,
			right = 3840,
			bottom = 1080,
		},
		(HMONITOR)3
	);

	private static Assert.RaisedEvent<MonitorsChangedEventArgs> DispatchTransformEvent(
		IContext ctx,
		MutableRootSector rootSector,
		Guid[]? layoutWorkspaceIds = null,
		Guid[]? notLayoutWorkspaceIds = null
	) =>
		Assert.Raises<MonitorsChangedEventArgs>(
			h => ctx.Store.MonitorEvents.MonitorsChanged += h,
			h => ctx.Store.MonitorEvents.MonitorsChanged -= h,
			() =>
				CustomAssert.Layout(
					rootSector,
					() => ctx.Store.Dispatch(new MonitorsChangedTransform()),
					layoutWorkspaceIds,
					notLayoutWorkspaceIds
				)
		);

	private static void Setup_TryEnqueue(IInternalContext internalCtx) =>
		internalCtx.CoreNativeManager.IsStaThread().Returns(_ => true, _ => false);

	/// <summary>
	/// Setup the adding of workspaces to the context.
	///
	/// Each added monitor now creates a new workspace pinned to it via
	/// <see cref="AddWorkspaceTransform"/>. Interceptors make the workspaces created during a test
	/// deterministic: the returned workspaces are added to the store in order as
	/// <see cref="AddWorkspaceTransform"/> is dispatched. Each interceptor also reproduces the real
	/// transform's sticky-pin bookkeeping from the intercepted transform's <c>MonitorIndices</c>, so
	/// that the pin a caller passes is observable via <see cref="IMapSector.StickyWorkspaceMonitorIndexMap"/>
	/// (and is remapped by the real transform logic when monitors later change).
	/// </summary>
	/// <param name="ctx"></param>
	/// <param name="rootSector"></param>
	/// <param name="count">The number of deterministic workspaces to make available.</param>
	/// <returns></returns>
	private static IWorkspace[] SetupAddWorkspaces(IContext ctx, MutableRootSector rootSector, int count = 3)
	{
		StoreWrapper store = (StoreWrapper)ctx.Store;
		Workspace[] workspaces = new Workspace[count];

		for (int i = 0; i < count; i++)
		{
			Workspace workspace = CreateWorkspace();
			workspaces[i] = workspace;
			store.AddInterceptor(
				t => t is AddWorkspaceTransform,
				t =>
				{
					AddWorkspaceToStore(rootSector, workspace);

					if (((AddWorkspaceTransform)t).MonitorIndices is { } monitorIndices)
					{
						rootSector.MapSector.StickyWorkspaceMonitorIndexMap =
							rootSector.MapSector.StickyWorkspaceMonitorIndexMap.SetItem(
								workspace.Id,
								[.. monitorIndices]
							);
					}

					return workspace.Id;
				}
			);
		}

		rootSector.WorkspaceSector.HasInitialized = true;
		return [.. workspaces];
	}

	private static void AssertContainsTransform(IContext ctx, Guid workspaceId, int times = 1)
	{
		CustomAssert.Contains(
			ctx.GetTransforms(),
			t => (t as DeactivateWorkspaceTransform) == new DeactivateWorkspaceTransform(workspaceId),
			times
		);
	}

	private static void AssertDoesNotContainTransform(IContext ctx, Guid workspaceId)
	{
		Assert.DoesNotContain(
			ctx.GetTransforms(),
			t => (t as DeactivateWorkspaceTransform) == new DeactivateWorkspaceTransform(workspaceId)
		);
	}

	private static void AssertPrimaryMonitor(MutableRootSector rootSector, HMONITOR handle)
	{
		Assert.Equal(handle, rootSector.MonitorSector.PrimaryMonitorHandle);
		Assert.Equal(handle, rootSector.MonitorSector.ActiveMonitorHandle);
		Assert.Equal(handle, rootSector.MonitorSector.LastWhimActiveMonitorHandle);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void WorkspaceSectorNotInitialized(
		IContext ctx,
		IInternalContext internalCtx,
		MutableRootSector rootSector
	)
	{
		// Given we've populated monitors
		SetupAddWorkspaces(ctx, rootSector);
		SetupMultipleMonitors(internalCtx, [RightMonitorSetup, LeftTopMonitorSetup, LeftBottomMonitorSetup]);

		rootSector.WorkspaceSector.HasInitialized = false;

		// When something happens
		ctx.Store.Dispatch(new MonitorsChangedTransform());

		// Then no workspaces are created
		Assert.Empty(rootSector.WorkspaceSector.WorkspaceOrder);
		Assert.Empty(rootSector.MapSector.MonitorWorkspaceMap);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void MonitorsRemoved(IContext ctx, IInternalContext internalCtx, MutableRootSector rootSector)
	{
		// Given we've populated monitors
		Setup_TryEnqueue(internalCtx);
		IWorkspace[] workspaces = SetupAddWorkspaces(ctx, rootSector);

		SetupMultipleMonitors(internalCtx, [RightMonitorSetup, LeftTopMonitorSetup, LeftBottomMonitorSetup]);

		ctx.Store.Dispatch(new MonitorsChangedTransform());

		// When a monitor is removed
		Setup_TryEnqueue(internalCtx);
		SetupMultipleMonitors(internalCtx, [RightMonitorSetup, LeftTopMonitorSetup]);

		// Then the resulting event will have a monitor removed
		var raisedEvent = DispatchTransformEvent(
			ctx,
			rootSector,
			[workspaces[0].Id, workspaces[2].Id],
			[workspaces[1].Id]
		);

		Assert.Empty(raisedEvent.Arguments.AddedMonitors);
		Assert.Single(raisedEvent.Arguments.RemovedMonitors);
		Assert.Equal(2, raisedEvent.Arguments.UnchangedMonitors.Count());

		Assert.Equal(2, rootSector.MapSector.MonitorWorkspaceMap.Count);

		Assert.Equal(workspaces[0].Id, rootSector.MapSector.MonitorWorkspaceMap[LeftTopMonitorSetup.Handle]);
		Assert.Equal(workspaces[2].Id, rootSector.MapSector.MonitorWorkspaceMap[RightMonitorSetup.Handle]);
		Assert.DoesNotContain(LeftBottomMonitorSetup.Handle, rootSector.MapSector.MonitorWorkspaceMap.Keys);

		AssertContainsTransform(ctx, workspaces[0].Id);
		AssertContainsTransform(ctx, workspaces[1].Id, 2);
		AssertContainsTransform(ctx, workspaces[2].Id);

		AssertPrimaryMonitor(rootSector, LeftTopMonitorSetup.Handle);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void MonitorsAdded(IContext ctx, IInternalContext internalCtx, MutableRootSector rootSector)
	{
		// Given we've populated monitors
		Setup_TryEnqueue(internalCtx);
		IWorkspace[] workspaces = SetupAddWorkspaces(ctx, rootSector);

		SetupMultipleMonitors(internalCtx, [RightMonitorSetup, LeftTopMonitorSetup]);
		ctx.Store.Dispatch(new MonitorsChangedTransform());

		// When a monitor is added
		Setup_TryEnqueue(internalCtx);
		SetupMultipleMonitors(internalCtx, [RightMonitorSetup, LeftTopMonitorSetup, LeftBottomMonitorSetup]);

		// Then the resulting event will have a monitor added
		var raisedEvent = DispatchTransformEvent(
			ctx,
			rootSector,
			[workspaces[0].Id, workspaces[1].Id, workspaces[2].Id]
		);

		Assert.Single(raisedEvent.Arguments.AddedMonitors);
		Assert.Empty(raisedEvent.Arguments.RemovedMonitors);
		Assert.Equal(2, raisedEvent.Arguments.UnchangedMonitors.Count());

		Assert.Equal(3, rootSector.MapSector.MonitorWorkspaceMap.Count);

		// The added monitor gets a brand-new workspace (created via the interceptor), and the existing
		// monitors keep theirs.
		Assert.Equal(workspaces[0].Id, rootSector.MapSector.MonitorWorkspaceMap[LeftTopMonitorSetup.Handle]);
		Assert.Equal(workspaces[1].Id, rootSector.MapSector.MonitorWorkspaceMap[RightMonitorSetup.Handle]);
		Assert.Equal(workspaces[2].Id, rootSector.MapSector.MonitorWorkspaceMap[LeftBottomMonitorSetup.Handle]);

		// Each workspace is pinned (sticky) to its monitor's index. LeftTop is index 0, LeftBottom is
		// index 1, and Right's pin was remapped to index 2 when LeftBottom was inserted before it.
		Assert.Equal(3, rootSector.MapSector.StickyWorkspaceMonitorIndexMap.Count);
		rootSector.MapSector.StickyWorkspaceMonitorIndexMap[workspaces[0].Id].Should().BeEquivalentTo([0]);
		rootSector.MapSector.StickyWorkspaceMonitorIndexMap[workspaces[2].Id].Should().BeEquivalentTo([1]);
		rootSector.MapSector.StickyWorkspaceMonitorIndexMap[workspaces[1].Id].Should().BeEquivalentTo([2]);

		AssertContainsTransform(ctx, workspaces[0].Id);
		AssertContainsTransform(ctx, workspaces[1].Id);
		AssertDoesNotContainTransform(ctx, workspaces[2].Id);

		AssertPrimaryMonitor(rootSector, LeftTopMonitorSetup.Handle);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void MonitorsAdded_PrimaryChanged(IContext ctx, IInternalContext internalCtx, MutableRootSector rootSector)
	{
		// Given we've populated monitors. Four deterministic workspaces are needed: one for each of
		// the two initial monitors, plus one each for the two monitors added in the second change (the
		// re-added LeftTop, now non-primary, and the new LeftBottom).
		Setup_TryEnqueue(internalCtx);
		IWorkspace[] workspaces = SetupAddWorkspaces(ctx, rootSector, count: 4);

		SetupMultipleMonitors(internalCtx, [RightMonitorSetup, LeftTopMonitorSetup]);
		ctx.Store.Dispatch(new MonitorsChangedTransform());

		// When a monitor is added, and the new monitor changes to become the primary monitor
		Setup_TryEnqueue(internalCtx);
		SetupMultipleMonitors(
			internalCtx,
			[RightMonitorSetup, LeftTopMonitorSetup, LeftBottomMonitorSetup],
			LeftBottomMonitorSetup.Handle
		);

		// Then the resulting event will have:
		// - two monitors added (the new one and the previous primary as a non-primary)
		// - one monitor removed (the previous primary as a non-primary)
		var raisedEvent = DispatchTransformEvent(
			ctx,
			rootSector,
			[workspaces[1].Id, workspaces[2].Id, workspaces[3].Id]
		);

		Assert.Equal(2, raisedEvent.Arguments.AddedMonitors.Count());
		Assert.Single(raisedEvent.Arguments.RemovedMonitors);
		Assert.Single(raisedEvent.Arguments.UnchangedMonitors);

		Assert.Equal(3, rootSector.MapSector.MonitorWorkspaceMap.Count);

		// Right is unchanged and keeps its original workspace. LeftTop was removed and re-added (its
		// primary flag changed), and LeftBottom is new, so both get brand-new pinned workspaces.
		Assert.Equal(workspaces[2].Id, rootSector.MapSector.MonitorWorkspaceMap[LeftTopMonitorSetup.Handle]);
		Assert.Equal(workspaces[1].Id, rootSector.MapSector.MonitorWorkspaceMap[RightMonitorSetup.Handle]);
		Assert.Equal(workspaces[3].Id, rootSector.MapSector.MonitorWorkspaceMap[LeftBottomMonitorSetup.Handle]);

		// Each shown workspace is pinned to its monitor's index: LeftTop 0, LeftBottom 1, and Right's
		// pin was remapped to index 2 when LeftBottom was inserted before it.
		rootSector.MapSector.StickyWorkspaceMonitorIndexMap[workspaces[2].Id].Should().BeEquivalentTo([0]);
		rootSector.MapSector.StickyWorkspaceMonitorIndexMap[workspaces[3].Id].Should().BeEquivalentTo([1]);
		rootSector.MapSector.StickyWorkspaceMonitorIndexMap[workspaces[1].Id].Should().BeEquivalentTo([2]);

		AssertContainsTransform(ctx, workspaces[0].Id, 2);
		AssertContainsTransform(ctx, workspaces[1].Id);
		AssertDoesNotContainTransform(ctx, workspaces[2].Id);

		AssertPrimaryMonitor(rootSector, LeftBottomMonitorSetup.Handle);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void MonitorsAdded_Initialization(IContext ctx, IInternalContext internalCtx, MutableRootSector rootSector)
	{
		// Given we have no monitors
		IWorkspace[] workspaces = SetupAddWorkspaces(ctx, rootSector);

		// When we add monitors
		SetupMultipleMonitors(internalCtx, [RightMonitorSetup, LeftTopMonitorSetup]);
		var raisedEvent = DispatchTransformEvent(ctx, rootSector, [workspaces[0].Id, workspaces[1].Id]);

		// Then the resulting event will have a monitor added, and the other monitors in the sector will be set.
		// Every monitor is added from an empty set, so each gets a new workspace pinned to its index.
		Assert.Equal(2, raisedEvent.Arguments.AddedMonitors.Count());

		Assert.Equal(LeftTopMonitorSetup.Handle, ctx.Store.Pick(Pickers.PickLastWhimActiveMonitor()).Handle);
		Assert.Equal(LeftTopMonitorSetup.Handle, ctx.Store.Pick(Pickers.PickActiveMonitor()).Handle);

		Assert.Equal(workspaces[0].Id, rootSector.MapSector.MonitorWorkspaceMap[LeftTopMonitorSetup.Handle]);
		Assert.Equal(workspaces[1].Id, rootSector.MapSector.MonitorWorkspaceMap[RightMonitorSetup.Handle]);

		Assert.Equal(2, rootSector.MapSector.StickyWorkspaceMonitorIndexMap.Count);
		rootSector.MapSector.StickyWorkspaceMonitorIndexMap[workspaces[0].Id].Should().BeEquivalentTo([0]);
		rootSector.MapSector.StickyWorkspaceMonitorIndexMap[workspaces[1].Id].Should().BeEquivalentTo([1]);

		Assert.DoesNotContain(ctx.GetTransforms(), t => t is DeactivateWorkspaceTransform);

		AssertPrimaryMonitor(rootSector, LeftTopMonitorSetup.Handle);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void MonitorsAdded_Initialization_AddWorkspaces(
		IContext ctx,
		IInternalContext internalCtx,
		MutableRootSector rootSector
	)
	{
		// Given we have no monitors
		IWorkspace[] workspaces = SetupAddWorkspaces(ctx, rootSector);

		// When we add monitors
		SetupMultipleMonitors(internalCtx, [RightMonitorSetup, LeftTopMonitorSetup]);
		var raisedEvent = DispatchTransformEvent(ctx, rootSector, [workspaces[0].Id, workspaces[1].Id]);

		// Then the resulting event will have a monitor added, and the other monitors in the sector will be set.
		Assert.Equal(2, raisedEvent.Arguments.AddedMonitors.Count());

		Assert.Equal(LeftTopMonitorSetup.Handle, ctx.Store.Pick(Pickers.PickLastWhimActiveMonitor()).Handle);
		Assert.Equal(LeftTopMonitorSetup.Handle, ctx.Store.Pick(Pickers.PickActiveMonitor()).Handle);

		Assert.Equal(workspaces[0].Id, rootSector.MapSector.MonitorWorkspaceMap[LeftTopMonitorSetup.Handle]);
		Assert.Equal(workspaces[1].Id, rootSector.MapSector.MonitorWorkspaceMap[RightMonitorSetup.Handle]);

		Assert.Equal(2, rootSector.MapSector.StickyWorkspaceMonitorIndexMap.Count);
		rootSector.MapSector.StickyWorkspaceMonitorIndexMap[workspaces[0].Id].Should().BeEquivalentTo([0]);
		rootSector.MapSector.StickyWorkspaceMonitorIndexMap[workspaces[1].Id].Should().BeEquivalentTo([1]);

		Assert.DoesNotContain(ctx.GetTransforms(), t => t is DeactivateWorkspaceTransform);

		AssertPrimaryMonitor(rootSector, LeftTopMonitorSetup.Handle);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void MonitorsUnchanged(IContext ctx, IInternalContext internalCtx, MutableRootSector rootSector)
	{
		// Given there are no changes in the monitors.
		Setup_TryEnqueue(internalCtx);
		IWorkspace[] workspaces = SetupAddWorkspaces(ctx, rootSector);

		SetupMultipleMonitors(internalCtx, [RightMonitorSetup, LeftTopMonitorSetup, LeftBottomMonitorSetup]);

		// When we dispatch the same transform twice, the first from a clean store
		ctx.Store.Dispatch(new MonitorsChangedTransform());

		// Then the second dispatch should receive all monitors as unchanged.
		Setup_TryEnqueue(internalCtx);
		var raisedEvent = DispatchTransformEvent(
			ctx,
			rootSector,
			notLayoutWorkspaceIds: [workspaces[0].Id, workspaces[1].Id, workspaces[2].Id]
		);

		Assert.Empty(raisedEvent.Arguments.AddedMonitors);
		Assert.Empty(raisedEvent.Arguments.RemovedMonitors);
		Assert.Equal(3, raisedEvent.Arguments.UnchangedMonitors.Count());

		Assert.Equal(3, rootSector.MapSector.MonitorWorkspaceMap.Count);

		Assert.Equal(workspaces[0].Id, rootSector.MapSector.MonitorWorkspaceMap[LeftTopMonitorSetup.Handle]);
		Assert.Equal(workspaces[2].Id, rootSector.MapSector.MonitorWorkspaceMap[RightMonitorSetup.Handle]);
		Assert.Equal(workspaces[1].Id, rootSector.MapSector.MonitorWorkspaceMap[LeftBottomMonitorSetup.Handle]);

		AssertDoesNotContainTransform(ctx, workspaces[0].Id);
		AssertDoesNotContainTransform(ctx, workspaces[1].Id);
		AssertDoesNotContainTransform(ctx, workspaces[2].Id);

		AssertPrimaryMonitor(rootSector, LeftTopMonitorSetup.Handle);
	}

	// The device name a non-primary monitor is remembered by is its szDevice, which
	// MonitorTestUtils.SetupMultipleMonitors sets to "DISPLAY {handle}".
	private static string RightMonitorName => $"DISPLAY {(int)RightMonitorSetup.Handle}";

	/// <summary>
	/// Sets up two monitors (LeftTop primary, Right non-primary), each with its own pinned workspace,
	/// by dispatching an initial monitors-changed. Returns the deterministic workspaces made available.
	/// After this: LeftTop shows workspaces[0] (pinned to index 0), Right shows workspaces[1] (pinned
	/// to index 1).
	/// </summary>
	private static IWorkspace[] Setup_TwoMonitorsEachWithWorkspace(
		IContext ctx,
		IInternalContext internalCtx,
		MutableRootSector rootSector,
		int workspaceCount = 3
	)
	{
		Setup_TryEnqueue(internalCtx);
		IWorkspace[] workspaces = SetupAddWorkspaces(ctx, rootSector, count: workspaceCount);

		SetupMultipleMonitors(internalCtx, [LeftTopMonitorSetup, RightMonitorSetup]);
		ctx.Store.Dispatch(new MonitorsChangedTransform());

		return workspaces;
	}

	/// <summary>
	/// When a monitor is unplugged, the workspace it was showing is remembered; when it is reconnected,
	/// that workspace is moved back onto it and pinned to it, without creating a new workspace.
	/// </summary>
	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void ReconnectedMonitor_RestoresRememberedWorkspace(
		IContext ctx,
		IInternalContext internalCtx,
		MutableRootSector rootSector
	)
	{
		// Given two monitors, each with a pinned workspace
		IWorkspace[] workspaces = Setup_TwoMonitorsEachWithWorkspace(ctx, internalCtx, rootSector);

		// When the Right monitor is unplugged
		Setup_TryEnqueue(internalCtx);
		SetupMultipleMonitors(internalCtx, [LeftTopMonitorSetup]);
		ctx.Store.Dispatch(new MonitorsChangedTransform());

		// Then its workspace is remembered, and is no longer shown on a monitor
		Assert.True(rootSector.MonitorSector.UnpluggedMonitorWorkspaces.ContainsKey(RightMonitorName));
		Assert.Equal(workspaces[1].Id, rootSector.MonitorSector.UnpluggedMonitorWorkspaces[RightMonitorName][0]);
		Assert.False(rootSector.MapSector.MonitorWorkspaceMap.ContainsKey(RightMonitorSetup.Handle));

		// When the Right monitor is reconnected
		Setup_TryEnqueue(internalCtx);
		SetupMultipleMonitors(internalCtx, [LeftTopMonitorSetup, RightMonitorSetup]);
		ctx.Store.Dispatch(new MonitorsChangedTransform());

		// Then the remembered workspace is moved back onto it, pinned to it, and no new workspace was made
		Assert.Equal(workspaces[1].Id, rootSector.MapSector.MonitorWorkspaceMap[RightMonitorSetup.Handle]);
		Assert.Equal(workspaces[0].Id, rootSector.MapSector.MonitorWorkspaceMap[LeftTopMonitorSetup.Handle]);
		rootSector.MapSector.StickyWorkspaceMonitorIndexMap[workspaces[1].Id].Should().BeEquivalentTo([1]);
		Assert.Equal(2, rootSector.WorkspaceSector.Workspaces.Count);

		// And the memory has been consumed
		Assert.False(rootSector.MonitorSector.UnpluggedMonitorWorkspaces.ContainsKey(RightMonitorName));
	}

	/// <summary>
	/// Every workspace pinned to an unplugged monitor is remembered and re-pinned to it on reconnect,
	/// and the workspace that was showing is the one shown again.
	/// </summary>
	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void ReconnectedMonitor_RestoresAllPinnedWorkspaces(
		IContext ctx,
		IInternalContext internalCtx,
		MutableRootSector rootSector
	)
	{
		// Given two monitors each with a pinned workspace, plus a second workspace pinned to Right
		// (index 1) which is not currently shown
		IWorkspace[] workspaces = Setup_TwoMonitorsEachWithWorkspace(ctx, internalCtx, rootSector);

		Workspace extra = CreateWorkspace();
		AddWorkspaceToStore(rootSector, extra);
		rootSector.MapSector.StickyWorkspaceMonitorIndexMap =
			rootSector.MapSector.StickyWorkspaceMonitorIndexMap.SetItem(extra.Id, [1]);

		// When the Right monitor is unplugged
		Setup_TryEnqueue(internalCtx);
		SetupMultipleMonitors(internalCtx, [LeftTopMonitorSetup]);
		ctx.Store.Dispatch(new MonitorsChangedTransform());

		// Then both of Right's workspaces are remembered, with the previously shown one first
		ImmutableArray<WorkspaceId> remembered = rootSector.MonitorSector.UnpluggedMonitorWorkspaces[RightMonitorName];
		Assert.Equal(workspaces[1].Id, remembered[0]);
		remembered.Should().BeEquivalentTo([workspaces[1].Id, extra.Id]);

		// When the Right monitor is reconnected
		Setup_TryEnqueue(internalCtx);
		SetupMultipleMonitors(internalCtx, [LeftTopMonitorSetup, RightMonitorSetup]);
		ctx.Store.Dispatch(new MonitorsChangedTransform());

		// Then both are re-pinned to Right, and the previously shown one is shown again
		Assert.Equal(workspaces[1].Id, rootSector.MapSector.MonitorWorkspaceMap[RightMonitorSetup.Handle]);
		rootSector.MapSector.StickyWorkspaceMonitorIndexMap[workspaces[1].Id].Should().BeEquivalentTo([1]);
		rootSector.MapSector.StickyWorkspaceMonitorIndexMap[extra.Id].Should().BeEquivalentTo([1]);
	}

	/// <summary>
	/// If a remembered workspace was deleted while the monitor was unplugged, reconnecting the monitor
	/// gracefully falls back to creating a new workspace.
	/// </summary>
	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void ReconnectedMonitor_WithDeletedRememberedWorkspace_CreatesNewWorkspace(
		IContext ctx,
		IInternalContext internalCtx,
		MutableRootSector rootSector
	)
	{
		// Given two monitors each with a pinned workspace
		IWorkspace[] workspaces = Setup_TwoMonitorsEachWithWorkspace(ctx, internalCtx, rootSector);

		// When the Right monitor is unplugged
		Setup_TryEnqueue(internalCtx);
		SetupMultipleMonitors(internalCtx, [LeftTopMonitorSetup]);
		ctx.Store.Dispatch(new MonitorsChangedTransform());

		// And Right's remembered workspace is deleted
		rootSector.WorkspaceSector.Workspaces = rootSector.WorkspaceSector.Workspaces.Remove(workspaces[1].Id);
		rootSector.WorkspaceSector.WorkspaceOrder = rootSector.WorkspaceSector.WorkspaceOrder.Remove(workspaces[1].Id);

		// When the Right monitor is reconnected
		Setup_TryEnqueue(internalCtx);
		SetupMultipleMonitors(internalCtx, [LeftTopMonitorSetup, RightMonitorSetup]);
		ctx.Store.Dispatch(new MonitorsChangedTransform());

		// Then a new workspace is created for Right, and the memory is consumed
		Assert.Equal(workspaces[2].Id, rootSector.MapSector.MonitorWorkspaceMap[RightMonitorSetup.Handle]);
		Assert.False(rootSector.MonitorSector.UnpluggedMonitorWorkspaces.ContainsKey(RightMonitorName));
	}

	/// <summary>
	/// A remembered workspace which the user moved onto a different monitor while the monitor was
	/// unplugged is left where it is; the reconnected monitor gets a new workspace instead.
	/// </summary>
	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void ReconnectedMonitor_WhenRememberedWorkspaceShownElsewhere_CreatesNewWorkspace(
		IContext ctx,
		IInternalContext internalCtx,
		MutableRootSector rootSector
	)
	{
		// Given two monitors each with a pinned workspace
		IWorkspace[] workspaces = Setup_TwoMonitorsEachWithWorkspace(ctx, internalCtx, rootSector);

		// When the Right monitor is unplugged
		Setup_TryEnqueue(internalCtx);
		SetupMultipleMonitors(internalCtx, [LeftTopMonitorSetup]);
		ctx.Store.Dispatch(new MonitorsChangedTransform());

		// And Right's remembered workspace is now shown on the LeftTop monitor instead
		rootSector.MapSector.MonitorWorkspaceMap = rootSector.MapSector.MonitorWorkspaceMap.SetItem(
			LeftTopMonitorSetup.Handle,
			workspaces[1].Id
		);

		// When the Right monitor is reconnected
		Setup_TryEnqueue(internalCtx);
		SetupMultipleMonitors(internalCtx, [LeftTopMonitorSetup, RightMonitorSetup]);
		ctx.Store.Dispatch(new MonitorsChangedTransform());

		// Then the remembered workspace stays on LeftTop, and Right gets a new workspace
		Assert.Equal(workspaces[1].Id, rootSector.MapSector.MonitorWorkspaceMap[LeftTopMonitorSetup.Handle]);
		Assert.Equal(workspaces[2].Id, rootSector.MapSector.MonitorWorkspaceMap[RightMonitorSetup.Handle]);
		Assert.False(rootSector.MonitorSector.UnpluggedMonitorWorkspaces.ContainsKey(RightMonitorName));
	}

	/// <summary>
	/// The primary monitor is not remembered on removal: its name is masked to "DISPLAY", which is not
	/// a stable identity for a physical monitor, so it must not be used as a restore key.
	/// </summary>
	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void PrimaryMonitorRemoval_IsNotRemembered(
		IContext ctx,
		IInternalContext internalCtx,
		MutableRootSector rootSector
	)
	{
		// Given two monitors, LeftTop being primary
		IWorkspace[] _ = Setup_TwoMonitorsEachWithWorkspace(ctx, internalCtx, rootSector);

		// When the primary (LeftTop) monitor is removed, forcing Right to become primary
		Setup_TryEnqueue(internalCtx);
		SetupMultipleMonitors(internalCtx, [RightMonitorSetup], RightMonitorSetup.Handle);
		ctx.Store.Dispatch(new MonitorsChangedTransform());

		// Then the removed primary's masked "DISPLAY" name is not remembered
		Assert.False(rootSector.MonitorSector.UnpluggedMonitorWorkspaces.ContainsKey("DISPLAY"));
	}
}
