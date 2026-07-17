# Keybinds

The keybinds configuration has a list of bindings that you can use to bind commands to key combinations.

## Bindings

A key modifier is a key that is pressed in combination with another key to perform a specific action. The `bindings` property is a list of keybinds that map a command to a key combination.

The format of a keybind is `Mod1+Mod2+...+Key`, where `Mod1`, `Mod2`, etc. are key modifiers and `Key` is the key that is pressed in combination with the modifiers.

Key modifiers are typically one of the following:

- `Ctrl`
- `Control`
- `LCtrl`
- `LControl`
- `RCtrl`
- `RControl`
- `Shift`
- `LShift`
- `RShift`
- `Alt`
- `LAlt`
- `RAlt`
- `Win`
- `LWin`
- `RWin`

Keybinds can also be any other key, though it's recommended to use keys which aren't typically used for other purposes.

The associated key for each modifier can be any of the <xref:Windows.Win32.UI.Input.KeyboardAndMouse.VIRTUAL_KEY>s, without the `VK*` prefix.

## Commands

A command is a string that represents a command that can be executed by Whim. The command can be a built-in command, a plugin command, or a custom command. For more, see the [Commands](commands.md) page.

## Unify Key Modifiers

To treat key modifiers like `LWin` and `RWin` the same, set `unify_key_modifiers` to `true`.

## Suppress Bare Win Key

To prevent Windows from opening the Start menu when the Windows key is pressed and released without another key, set `suppress_bare_win_key` to `true`. This preserves the original behavior of sending an unassigned `0xE8` key tap when the Windows key is pressed. Set it to `false` to disable suppression.

Named modes provide other suppression strategies:

- `down-control` and `up-control` send a Control key tap when the Windows key is pressed or released.
- `down-e8` and `up-e8` send an unassigned `0xE8` key tap when the Windows key is pressed or released.
- `eat-bound` hides the Windows key from Windows when it is used in a Whim keybind. Native unbound shortcuts and bare Windows key taps still pass through.
- `eat-bound-and-bare-tap` also hides bare Windows key taps, while native unbound shortcuts still pass through.

## Center Cursor on Monitor Switch

To move the mouse cursor to the center of a monitor when it is focused via a keybind (for example `whim.core.focus_next_monitor`), set `center_cursor_on_monitor_switch` to `true`. Defaults to `false`.

## Keybinds Example

```yaml
keybinds:
  entries:
    - command: whim.core.focus_next_monitor
      keybind: LCtrl+LShift+LAlt+K

    - command: whim.core.focus_previous_monitor
      keybind: LCtrl+LShift+LAlt+J

    - command: whim.custom.next_layout_engine
      keybind: LCtrl+LShift+LAlt+L

    - command: whim.core.cycle_layout_engine.next
      keybind: LCtrl+LShift+LAlt+L

    - command: whim.core.cycle_layout_engine.previous
      keybind: LCtrl+LShift+LAlt+Win+L

    - command: whim.command_palette.find_focus_window
      keybind: Win+LCtrl+F

    - command: whim.core.exit_whim
      keybind: Win+LCtrl+Q

  unify_key_modifiers: true
  suppress_bare_win_key: false
  center_cursor_on_monitor_switch: false
```
