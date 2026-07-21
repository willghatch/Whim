using System;
using System.Collections.Generic;
using System.Linq;

namespace Whim.Bar;

/// <summary>
/// View model containing the workspaces for a given monitor.
/// </summary>
internal class WorkspaceWidgetViewModel : IDisposable
{
	private readonly IContext _context;
	private bool _disposedValue;

	/// <summary>
	/// The monitor which contains the workspaces.
	/// </summary>
	public IMonitor Monitor { get; }

	/// <summary>
	/// The workspaces for the monitor.
	/// </summary>
	public VeryObservableCollection<WorkspaceModel> Workspaces { get; } = [];

	/// <summary>
	/// Creates a new instance of <see cref="WorkspaceWidgetViewModel"/>.
	/// </summary>
	/// <param name="context"></param>
	/// <param name="monitor"></param>
	public WorkspaceWidgetViewModel(IContext context, IMonitor monitor)
	{
		_context = context;
		Monitor = monitor;

		_context.Store.WorkspaceEvents.WorkspaceAdded += WorkspaceEvents_WorkspaceAdded;
		_context.Store.WorkspaceEvents.WorkspaceRemoved += WorkspaceEvents_WorkspaceRemoved;
		_context.Store.MapEvents.MonitorWorkspaceChanged += MapEvents_MonitorWorkspaceChanged;
		_context.Store.WorkspaceEvents.WorkspaceRenamed += WorkspaceEvents_WorkspaceRenamed;
		_context.Store.WorkspaceEvents.WorkspaceOrderChanged += WorkspaceEvents_WorkspaceOrderChanged;

		UpdateWorkspacesCollection();
	}

	/// <summary>
	/// Rebuilds <see cref="Workspaces"/> from the workspaces which can be shown on this monitor, but
	/// only if that set has actually changed. Returns <see langword="true"/> if the collection was
	/// rebuilt, otherwise <see langword="false"/> so callers can update lighter-weight state (such as
	/// the active flag) without discarding the existing models.
	/// </summary>
	private bool UpdateWorkspacesCollection()
	{
		IReadOnlyList<IWorkspace> workspaces =
			_context.Store.Pick(Pickers.PickStickyWorkspacesByMonitor(Monitor.Handle)).ValueOrDefault ?? [];

		if (workspaces.Select(w => w.Id).SequenceEqual(Workspaces.Select(m => m.Workspace.Id)))
		{
			return false;
		}

		Workspaces.Clear();

		foreach (IWorkspace workspace in workspaces)
		{
			IMonitor? monitorForWorkspace = _context
				.Store.Pick(Pickers.PickMonitorByWorkspace(workspace.Id))
				.ValueOrDefault;

			Workspaces.Add(
				new WorkspaceModel(_context, this, workspace, Monitor.Handle == monitorForWorkspace?.Handle)
			);
		}

		return true;
	}

	private void WorkspaceEvents_WorkspaceAdded(object? sender, WorkspaceEventArgs args) =>
		UpdateWorkspacesCollection();

	private void WorkspaceEvents_WorkspaceRemoved(object? sender, WorkspaceEventArgs args) =>
		UpdateWorkspacesCollection();

	private void WorkspaceEvents_WorkspaceOrderChanged(object? sender, WorkspaceOrderChangedEventArgs args) =>
		UpdateWorkspacesCollection();

	private void MapEvents_MonitorWorkspaceChanged(object? sender, MonitorWorkspaceChangedEventArgs args)
	{
		if (args.Monitor.Handle != Monitor.Handle)
		{
			return;
		}

		// Moving a sticky workspace on or off this monitor changes which workspaces belong on the bar,
		// so refresh membership first. If it was unchanged, this is an ordinary workspace switch and we
		// only need to move the active marker.
		if (UpdateWorkspacesCollection())
		{
			return;
		}

		foreach (WorkspaceModel workspaceModel in Workspaces)
		{
			workspaceModel.ActiveOnMonitor = workspaceModel.Workspace.Id == args.CurrentWorkspace.Id;
		}
	}

	private void WorkspaceEvents_WorkspaceRenamed(object? sender, WorkspaceRenamedEventArgs e)
	{
		WorkspaceModel? workspace = Workspaces.FirstOrDefault(m => m.Workspace.Id == e.Workspace.Id);
		if (workspace == null)
		{
			return;
		}

		workspace.Workspace_Renamed(sender, e);
	}

	/// <inheritdoc/>
	protected virtual void Dispose(bool disposing)
	{
		if (!_disposedValue)
		{
			if (disposing)
			{
				// dispose managed state (managed objects)
				_context.Store.WorkspaceEvents.WorkspaceAdded -= WorkspaceEvents_WorkspaceAdded;
				_context.Store.WorkspaceEvents.WorkspaceRemoved -= WorkspaceEvents_WorkspaceRemoved;
				_context.Store.MapEvents.MonitorWorkspaceChanged -= MapEvents_MonitorWorkspaceChanged;
				_context.Store.WorkspaceEvents.WorkspaceRenamed -= WorkspaceEvents_WorkspaceRenamed;
				_context.Store.WorkspaceEvents.WorkspaceOrderChanged -= WorkspaceEvents_WorkspaceOrderChanged;
			}

			// free unmanaged resources (unmanaged objects) and override finalizer
			// set large fields to null
			_disposedValue = true;
		}
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
