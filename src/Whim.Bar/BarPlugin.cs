using System;
using System.Collections.Generic;
using System.Text.Json;
using Windows.Win32.Graphics.Dwm;

namespace Whim.Bar;

/// <inheritdoc/>
/// <summary>
/// Create the bar plugin.
/// </summary>
/// <param name="context"></param>
/// <param name="barConfig"></param>
public class BarPlugin(IContext context, BarConfig barConfig) : IBarPlugin
{
	private readonly IContext _context = context;

	private readonly Dictionary<IMonitor, BarWindow> _monitorBarMap = [];
	private bool _disposedValue;

	/// <summary>
	/// <c>whim.bar</c>
	/// </summary>
	public string Name => "whim.bar";

	/// <inheritdoc/>
	public BarConfig Config => barConfig;

	/// <inheritdoc />
	public void PreInitialize()
	{
		_context.FilterManager.AddTitleMatchFilter("Whim Bar");
		_context.Store.Dispatch(new AddProxyLayoutEngineTransform(layout => new BarLayoutEngine(Config, layout)));

		Config.Initialize();
	}

	/// <inheritdoc />
	public void PostInitialize()
	{
		// Publish the configured font size so that widget styles which cannot inherit the bar's
		// FontSize (the workspace and active-layout buttons) can read it via ThemeResource. This
		// must happen before the bar windows - and their widgets - are created.
		if (Microsoft.UI.Xaml.Application.Current is { } application)
		{
			application.Resources["bar:font_size"] = Config.FontSize;
		}

		foreach (IMonitor monitor in _context.Store.Pick(Pickers.PickAllMonitors()))
		{
			EnsureBarWindow(monitor);
		}

		ShowAll();

		// Subscribe only now that the bars exist. The store fires the initial MonitorsChanged
		// (with every monitor as "added") during Store.Initialize, which runs between PreInitialize
		// and here; reacting to it would create a second set of bar windows and orphan this one.
		_context.Store.MonitorEvents.MonitorsChanged += MonitorManager_MonitorsChanged;
	}

	private void MonitorManager_MonitorsChanged(object? sender, MonitorsChangedEventArgs e)
	{
		// Remove the removed monitors
		foreach (IMonitor monitor in e.RemovedMonitors)
		{
			_monitorBarMap.TryGetValue(monitor, out BarWindow? value);
			_context.NativeManager.TryEnqueue(() => value?.Close());
			_monitorBarMap.Remove(monitor);
		}

		// Add the new monitors
		foreach (IMonitor monitor in e.AddedMonitors)
		{
			EnsureBarWindow(monitor);
		}

		ShowAll();
	}

	/// <summary>
	/// Creates a bar window for the monitor if it does not already have one. Guards against creating
	/// duplicate bars for a monitor that is already tracked.
	/// </summary>
	private void EnsureBarWindow(IMonitor monitor)
	{
		if (_monitorBarMap.ContainsKey(monitor))
		{
			return;
		}

		_monitorBarMap[monitor] = new BarWindow(_context, Config, monitor);
	}

	/// <summary>
	/// Show all the bar windows.
	/// </summary>
	private void ShowAll()
	{
		using DeferWindowPosHandle deferPosHandle = _context.NativeManager.DeferWindowPos();
		foreach (BarWindow barWindow in _monitorBarMap.Values)
		{
			barWindow.UpdateRect();
			IWindowState state = barWindow.WindowState;

			deferPosHandle.DeferWindowPos(
				new DeferWindowPosState(state.Window.Handle, state.WindowSize, state.Rectangle),
				forceTwoPasses: true
			);
			_context.NativeManager.SetWindowCorners(
				state.Window.Handle,
				DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND
			);
		}
	}

	/// <inheritdoc />
	public void LoadState(JsonElement pluginSavedState) { }

	/// <inheritdoc />
	public JsonElement? SaveState() => null;

	/// <inheritdoc />
	protected virtual void Dispose(bool disposing)
	{
		if (!_disposedValue)
		{
			if (disposing)
			{
				foreach (BarWindow barWindow in _monitorBarMap.Values)
				{
					barWindow.Dispose();
					barWindow.Close();
				}

				_monitorBarMap.Clear();
				_context.Store.MonitorEvents.MonitorsChanged -= MonitorManager_MonitorsChanged;
			}

			// free unmanaged resources (unmanaged objects) and override finalizer
			// set large fields to null
			_disposedValue = true;
		}
	}

	/// <inheritdoc />
	public void Dispose()
	{
		// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	/// <inheritdoc />
	public IPluginCommands PluginCommands => new PluginCommands(Name);
}
