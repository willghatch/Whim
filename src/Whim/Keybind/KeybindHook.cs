using System.Linq;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Whim;

/// <summary>
/// Responsible is responsible for hooking into windows and handling keybinds.
/// </summary>
internal class KeybindHook : IKeybindHook
{
	private const VIRTUAL_KEY _e8Key = (VIRTUAL_KEY)0xE8;
	private const uint _injectedFlag = 0x10;

	private enum DeferredWinKeyState
	{
		Deferred,
		Eaten,
		Replayed,
	}

	private readonly IContext _context;
	private readonly IInternalContext _internalContext;
	private readonly HOOKPROC _lowLevelKeyboardProc;
	private readonly Dictionary<VIRTUAL_KEY, DeferredWinKeyState> _deferredWinKeys = [];
	private UnhookWindowsHookExSafeHandle? _unhookKeyboardHook;
	private bool _disposedValue;

	public KeybindHook(IContext context, IInternalContext internalContext)
	{
		_context = context;
		_internalContext = internalContext;
		_lowLevelKeyboardProc = LowLevelKeyboardProcWrapper;
	}

	public void PostInitialize()
	{
		Logger.Debug("Initializing keybind manager...");
		_unhookKeyboardHook = _internalContext.CoreNativeManager.SetWindowsHookEx(
			WINDOWS_HOOK_ID.WH_KEYBOARD_LL,
			_lowLevelKeyboardProc,
			null,
			0
		);
	}

	private LRESULT LowLevelKeyboardProcWrapper(int nCode, WPARAM wParam, LPARAM lParam)
	{
		try
		{
			return LowLevelKeyboardProc(nCode, wParam, lParam);
		}
		catch (Exception e)
		{
			_context.HandleUncaughtException(nameof(LowLevelKeyboardProc), e);
			return _internalContext.CoreNativeManager.CallNextHookEx(nCode, wParam, lParam);
		}
	}

	/// <summary>
	/// For relevant documentation, see https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc
	/// </summary>
	/// <param name="nCode"></param>
	/// <param name="wParam"></param>
	/// <param name="lParam"></param>
	/// <returns></returns>
	private LRESULT LowLevelKeyboardProc(int nCode, WPARAM wParam, LPARAM lParam)
	{
		Logger.Verbose($"{nCode} {wParam.Value} {lParam.Value}");
		nuint message = (nuint)wParam;
		bool isKeyDown = message == PInvoke.WM_KEYDOWN || message == PInvoke.WM_SYSKEYDOWN;
		bool isKeyUp = message == PInvoke.WM_KEYUP || message == PInvoke.WM_SYSKEYUP;
		if (nCode != 0 || (!isKeyDown && !isKeyUp))
		{
			return _internalContext.CoreNativeManager.CallNextHookEx(nCode, wParam, lParam);
		}

		if (_internalContext.CoreNativeManager.PtrToStructure<KBDLLHOOKSTRUCT>(lParam) is not KBDLLHOOKSTRUCT kbdll)
		{
			return _internalContext.CoreNativeManager.CallNextHookEx(nCode, wParam, lParam);
		}

		VIRTUAL_KEY key = (VIRTUAL_KEY)kbdll.vkCode;
		if (((uint)kbdll.flags & _injectedFlag) != 0)
		{
			return _internalContext.CoreNativeManager.CallNextHookEx(nCode, wParam, lParam);
		}

		WinKeySuppressionMode suppressionMode = _context.KeybindManager.SuppressBareWinKey;
		bool isWinKey = key is VIRTUAL_KEY.VK_LWIN or VIRTUAL_KEY.VK_RWIN;
		if (isWinKey && IsEatingMode(suppressionMode))
		{
			return HandleWinKeyForEatingMode(key, isKeyDown, suppressionMode, nCode, wParam, lParam);
		}

		if (isWinKey && ShouldSendSuppressionTap(suppressionMode, isKeyDown))
		{
			SendKeyTap(GetSuppressionKey(suppressionMode));
		}

		if (isKeyUp)
		{
			return _internalContext.CoreNativeManager.CallNextHookEx(nCode, wParam, lParam);
		}

		// Ignore key modifiers which are a modifier.
		if (_context.KeybindManager.Modifiers.Contains(key))
		{
			return _internalContext.CoreNativeManager.CallNextHookEx(nCode, wParam, lParam);
		}

		if (GetKeybindForKey(key) is Keybind keybind)
		{
			ICommand[] commands = _context.KeybindManager.GetCommands(keybind);
			if (IsEatingMode(suppressionMode) && _deferredWinKeys.Count > 0)
			{
				if (commands.Length > 0)
				{
					EatDeferredWinKeys();
					ExecuteCommands(keybind, commands);
					return (LRESULT)1;
				}

				ReplayDeferredWinKeys();
			}
			else if (commands.Length > 0)
			{
				ExecuteCommands(keybind, commands);
				return (LRESULT)1;
			}
		}

		return _internalContext.CoreNativeManager.CallNextHookEx(nCode, wParam, lParam);
	}

	private IKeybind? GetKeybindForKey(VIRTUAL_KEY eventKey)
	{
		List<VIRTUAL_KEY> pressedModifiers = [];
		foreach (VIRTUAL_KEY modifier in _context.KeybindManager.Modifiers)
		{
			if (IsModifierPressed(modifier) || _deferredWinKeys.ContainsKey(modifier))
			{
				pressedModifiers.Add(modifier);
			}
		}

		return new Keybind(pressedModifiers, eventKey);
	}

	private bool IsModifierPressed(VIRTUAL_KEY key) =>
		(_internalContext.CoreNativeManager.GetKeyState((int)key) & 0x8000) == 0x8000;

	private LRESULT HandleWinKeyForEatingMode(
		VIRTUAL_KEY key,
		bool isKeyDown,
		WinKeySuppressionMode mode,
		int nCode,
		WPARAM wParam,
		LPARAM lParam
	)
	{
		if (isKeyDown)
		{
			if (!_deferredWinKeys.ContainsKey(key))
			{
				_deferredWinKeys[key] = DeferredWinKeyState.Deferred;
			}

			return (LRESULT)1;
		}

		if (!_deferredWinKeys.Remove(key, out DeferredWinKeyState state))
		{
			return _internalContext.CoreNativeManager.CallNextHookEx(nCode, wParam, lParam);
		}

		if (state == DeferredWinKeyState.Replayed)
		{
			return _internalContext.CoreNativeManager.CallNextHookEx(nCode, wParam, lParam);
		}

		if (state == DeferredWinKeyState.Deferred && mode == WinKeySuppressionMode.EatBound)
		{
			SendKeyTap(key);
		}

		return (LRESULT)1;
	}

	private static bool IsEatingMode(WinKeySuppressionMode mode) =>
		mode is WinKeySuppressionMode.EatBound or WinKeySuppressionMode.EatBoundAndBareTap;

	private static bool ShouldSendSuppressionTap(WinKeySuppressionMode mode, bool isKeyDown) =>
		((mode is WinKeySuppressionMode.DownE8 or WinKeySuppressionMode.DownControl) && isKeyDown)
		|| ((mode is WinKeySuppressionMode.UpE8 or WinKeySuppressionMode.UpControl) && !isKeyDown);

	private static VIRTUAL_KEY GetSuppressionKey(WinKeySuppressionMode mode) =>
		mode is WinKeySuppressionMode.DownControl or WinKeySuppressionMode.UpControl ? VIRTUAL_KEY.VK_CONTROL : _e8Key;

	private void EatDeferredWinKeys()
	{
		foreach (VIRTUAL_KEY key in _deferredWinKeys.Keys.ToArray())
		{
			if (_deferredWinKeys[key] == DeferredWinKeyState.Deferred)
			{
				_deferredWinKeys[key] = DeferredWinKeyState.Eaten;
			}
		}
	}

	private void ReplayDeferredWinKeys()
	{
		List<INPUT> inputs = [];
		foreach (VIRTUAL_KEY key in _deferredWinKeys.Keys.ToArray())
		{
			if (_deferredWinKeys[key] == DeferredWinKeyState.Deferred)
			{
				inputs.Add(CreateKeyboardInput(key, default));
				_deferredWinKeys[key] = DeferredWinKeyState.Replayed;
			}
		}

		if (inputs.Count > 0)
		{
			SendInputs([.. inputs]);
		}
	}

	private void SendKeyTap(VIRTUAL_KEY key)
	{
		INPUT down = CreateKeyboardInput(key, default);
		INPUT up = CreateKeyboardInput(key, KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP);
		SendInputs([down, up]);
	}

	private void SendInputs(INPUT[] inputs)
	{
		unsafe
		{
			_internalContext.CoreNativeManager.SendInput(inputs, sizeof(INPUT));
		}
	}

	private static INPUT CreateKeyboardInput(VIRTUAL_KEY key, KEYBD_EVENT_FLAGS flags) =>
		new()
		{
			type = INPUT_TYPE.INPUT_KEYBOARD,
			Anonymous = new INPUT._Anonymous_e__Union()
			{
				ki = new KEYBDINPUT() { wVk = key, dwFlags = flags },
			},
		};

	private static void ExecuteCommands(Keybind keybind, ICommand[] commands)
	{
		Logger.Verbose(keybind.ToString());
		foreach (ICommand command in commands)
		{
			command.TryExecute();
		}
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_disposedValue)
		{
			if (disposing)
			{
				// dispose managed state (managed objects)
				_unhookKeyboardHook?.Dispose();
			}

			// free unmanaged resources (unmanaged objects) and override finalizer
			// set large fields to null
			_disposedValue = true;
		}
	}

	public void Dispose()
	{
		// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
