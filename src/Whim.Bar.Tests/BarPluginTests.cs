using Microsoft.UI.Dispatching;
using NSubstitute;
using Whim.TestUtils;
using Xunit;

namespace Whim.Bar.Tests;

public class BarPluginTests
{
	[Theory, AutoSubstituteData]
	public void MonitorManager_MonitorsChanged_RemovedMonitors(IContext context, IMonitor monitor)
	{
		// Given a fully initialized bar plugin on a machine with no monitors (so PostInitialize
		// creates no bar windows, which cannot be instantiated in a headless test)
		BarConfig barConfig = new([], [], []);
		BarPlugin barPlugin = new(context, barConfig);
		NativeManagerUtils.SetupTryEnqueue(context);
		context.Store.Pick(Arg.Any<PurePicker<IReadOnlyList<IMonitor>>>()).Returns((IReadOnlyList<IMonitor>)[]);
		barPlugin.PreInitialize();
		barPlugin.PostInitialize();

		// When MonitorManager_MonitorsChanged is called with a removed monitor which is not in the map
		context.Store.MonitorEvents.MonitorsChanged += Raise.EventWith(
			new MonitorsChangedEventArgs()
			{
				AddedMonitors = [],
				UnchangedMonitors = [],
				RemovedMonitors = [monitor],
			}
		);

		// Then an exception is not thrown.
		barPlugin.Dispose();
		context.NativeManager.Received(1).TryEnqueue(Arg.Any<DispatcherQueueHandler>());
	}

	[Theory, AutoSubstituteData]
	public void MonitorsChanged_IgnoredBeforePostInitialize(IContext context, IMonitor monitor)
	{
		// Given a bar plugin that has only been pre-initialized. During startup the store fires the
		// initial MonitorsChanged (with every monitor as "added") between PreInitialize and
		// PostInitialize. If the plugin reacted to it, it would create a set of bar windows that
		// PostInitialize then creates again, orphaning the first set as visible duplicate bars.
		BarConfig barConfig = new([], [], []);
		BarPlugin barPlugin = new(context, barConfig);
		NativeManagerUtils.SetupTryEnqueue(context);
		barPlugin.PreInitialize();

		// When a MonitorsChanged event is raised before PostInitialize
		context.Store.MonitorEvents.MonitorsChanged += Raise.EventWith(
			new MonitorsChangedEventArgs()
			{
				AddedMonitors = [],
				UnchangedMonitors = [],
				RemovedMonitors = [monitor],
			}
		);

		// Then the plugin does not react to it yet - it only subscribes once its own bars exist.
		barPlugin.Dispose();
		context.NativeManager.DidNotReceive().TryEnqueue(Arg.Any<DispatcherQueueHandler>());
	}
}
