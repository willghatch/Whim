A "workspace" or <xref:Whim.IWorkspace> in Whim is a collection of windows displayed on a single monitor. The layouts of workspaces are determined by their [layout engines](../../configure/core/layout-engines.md). Each workspace has a single active layout engine, and can cycle through different layout engines. <!-- markdownlint-disable-line MD041 -->

Defining workspaces in the config is optional. Each monitor that does not have a configured workspace is given a workspace named `Workspace {n}` which is pinned (sticky) to that monitor. Likewise, when a monitor is added while Whim is running, it gains a new workspace pinned to it. Configured workspaces are assigned to monitors first; any leftover monitors receive these per-monitor defaults.

When a monitor is unplugged while Whim is running, Whim remembers the workspaces which were on it. If that monitor is reconnected during the same session, those workspaces are moved back onto it and the one which was showing is shown again. Workspaces which have since been deleted, or which you have moved onto another monitor in the meantime, are left as they are. This memory is not saved between Whim runs. Reconnecting the primary monitor is not tracked in this way.

When Whim exits, the windows of the workspaces which are not shown on a monitor are moved into the workspaces which are shown on a monitor, so that no windows are left hidden. A workspace which is sticky to specific monitors is merged into the workspace shown on one of those monitors.

When Whim exits, it will save the current workspaces and the current positions of each window within them. When Whim is started again, it will attempt to merge the saved workspaces with the workspaces defined in the config.

When Whim starts, the saved windows which Whim left hidden - for example, if it did not exit cleanly - are shown again, so that they are not lost. A window whose saved workspace is no longer defined in the config is added to the workspace of the monitor it is on.
