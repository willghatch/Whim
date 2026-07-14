A "workspace" or <xref:Whim.IWorkspace> in Whim is a collection of windows displayed on a single monitor. The layouts of workspaces are determined by their [layout engines](../../configure/core/layout-engines.md). Each workspace has a single active layout engine, and can cycle through different layout engines. <!-- markdownlint-disable-line MD041 -->

There must be at least as many workspaces defined in the config as there are monitors connected to the system. If there are more workspaces defined than monitors, workspaces named `Workspace {workspaces.Count + 1}` will be created for the extra monitors. If there are fewer workspaces defined than monitors, the extra monitors will not have any workspaces displayed on them.

When Whim exits, the windows of the workspaces which are not shown on a monitor are moved into the workspaces which are shown on a monitor, so that no windows are left hidden. A workspace which is sticky to specific monitors is merged into the workspace shown on one of those monitors.

When Whim exits, it will save the current workspaces and the current positions of each window within them. When Whim is started again, it will attempt to merge the saved workspaces with the workspaces defined in the config.

When Whim starts, the saved windows which Whim left hidden - for example, if it did not exit cleanly - are shown again, so that they are not lost. A window whose saved workspace is no longer defined in the config is added to the workspace of the monitor it is on.
