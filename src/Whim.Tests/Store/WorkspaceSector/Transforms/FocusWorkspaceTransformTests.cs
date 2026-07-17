namespace Whim.Tests;

public class FocusWorkspaceTransformTests
{
	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void Success_UseLastFocusedWindowHandle(IContext ctx, IInternalContext internalCtx, MutableRootSector root)
	{
		// Given a last focused window handle is defined for the workspace, and we're not on the main thread
		HWND handle = (HWND)123;
		Workspace workspace = CreateWorkspace() with { LastFocusedWindowHandle = handle };
		IMonitor monitor = CreateMonitor((HMONITOR)123);

		PopulateMonitorWorkspaceMap(root, monitor, workspace);

		internalCtx.CoreNativeManager.IsStaThread().Returns(false);

		// When
		var result = ctx.Store.Dispatch(new FocusWorkspaceTransform(workspace.Id));

		// Then
		Assert.True(result.IsSuccessful);
		Assert.Equal(handle, root.WorkspaceSector.WindowHandleToFocus);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void Success_NoLastFocusedWindowHandle(IContext ctx, MutableRootSector root, List<object> transforms)
	{
		// Given
		Workspace workspace = CreateWorkspace();
		IMonitor monitor = CreateMonitor((HMONITOR)123);

		PopulateMonitorWorkspaceMap(root, monitor, workspace);

		// When
		var result = ctx.Store.Dispatch(new FocusWorkspaceTransform(workspace.Id));

		// Then
		Assert.True(result.IsSuccessful);
		Assert.Contains(new FocusMonitorDesktopTransform(monitor.Handle), transforms);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void Success_NoLastFocusedWindowHandle_FocusesFirstWindow(
		IContext ctx,
		MutableRootSector root,
		List<object> transforms,
		IWindow window
	)
	{
		// Given a workspace which has a (non-minimized) window but no recorded last-focused window,
		// as happens when a monitor is focused whose workspace was never interacted with
		window.Handle.Returns((HWND)123);
		Workspace workspace = CreateWorkspace() with
		{
			WindowPositions = ImmutableDictionary<HWND, WindowPosition>.Empty.Add(
				window.Handle,
				new WindowPosition(WindowSize.Normal, new Rectangle<int>())
			),
		};
		IMonitor monitor = CreateMonitor((HMONITOR)123);
		PopulateMonitorWorkspaceMap(root, monitor, workspace);
		AddWindowToSector(root, window);

		// When
		var result = ctx.Store.Dispatch(new FocusWorkspaceTransform(workspace.Id));

		// Then the window is focused rather than the desktop
		Assert.True(result.IsSuccessful);
		Assert.Equal(window.Handle, root.WorkspaceSector.WindowHandleToFocus);
		Assert.DoesNotContain(transforms, t => t is FocusMonitorDesktopTransform);
	}

	[Theory, AutoSubstituteData<StoreCustomization>]
	internal void Failure_PickMonitorByWorkspace(IContext ctx, MutableRootSector root)
	{
		// Given
		Workspace workspace = CreateWorkspace();
		AddWorkspacesToStore(root, workspace);

		// When
		var result = ctx.Store.Dispatch(new FocusWorkspaceTransform(workspace.Id));

		// Then
		Assert.False(result.IsSuccessful);
	}
}
