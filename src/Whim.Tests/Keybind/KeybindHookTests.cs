using System.Linq;
using AutoFixture;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Whim.Tests;

public class KeybindHookCustomization : ICustomization
{
	public void Customize(IFixture fixture)
	{
		IInternalContext internalCtx = fixture.Freeze<IInternalContext>();
		internalCtx.CoreNativeManager.CallNextHookEx(0, 0, 0).Returns((LRESULT)0);
	}
}

public class KeybindHookTests
{
	private class FakeSafeHandle(bool isInvalid) : UnhookWindowsHookExSafeHandle(default, default)
	{
		public bool HasDisposed { get; private set; }

		private readonly bool _isInvalid = isInvalid;
		public override bool IsInvalid => _isInvalid;

		protected override bool ReleaseHandle()
		{
			return true;
		}

		protected override void Dispose(bool disposing)
		{
			HasDisposed = true;
			base.Dispose(disposing);
		}
	}

	private class CaptureKeybindHook
	{
		public HOOKPROC? LowLevelKeyboardProc { get; private set; }
		public FakeSafeHandle? Handle { get; private set; }

		public static CaptureKeybindHook Create(IInternalContext internalCtx)
		{
			CaptureKeybindHook captureKeybindHook = new();
			internalCtx
				.CoreNativeManager.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, Arg.Any<HOOKPROC>(), null, 0)
				.Returns(
					(callInfo) =>
					{
						captureKeybindHook.LowLevelKeyboardProc = callInfo.ArgAt<HOOKPROC>(1);
						captureKeybindHook.Handle = new FakeSafeHandle(false);
						return captureKeybindHook.Handle;
					}
				);

			return captureKeybindHook;
		}
	}

	private static void SetupKey(
		IContext ctx,
		IInternalContext internalCtx,
		VIRTUAL_KEY[] modifiers,
		VIRTUAL_KEY key,
		ICommand[] commands
	)
	{
		foreach (VIRTUAL_KEY modifier in modifiers)
		{
			internalCtx.CoreNativeManager.GetKeyState((int)modifier).Returns((short)-32768);
		}

		internalCtx
			.CoreNativeManager.PtrToStructure<KBDLLHOOKSTRUCT>(Arg.Any<nint>())
			.Returns(new KBDLLHOOKSTRUCT { vkCode = (uint)key });

		ctx.KeybindManager.GetCommands(Arg.Any<IKeybind>()).Returns(commands);
		ctx.KeybindManager.Modifiers.Returns(modifiers);
	}

	[Theory, AutoSubstituteData<KeybindHookCustomization>]
	internal void PostInitialize(IContext ctx, IInternalContext internalCtx)
	{
		// Given
		CaptureKeybindHook capture = CaptureKeybindHook.Create(internalCtx);
		KeybindHook keybindHook = new(ctx, internalCtx);

		// When
		keybindHook.PostInitialize();

		// Then
		internalCtx
			.CoreNativeManager.Received(1)
			.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, Arg.Any<HOOKPROC>(), null, 0);
	}

	[InlineAutoSubstituteData<KeybindHookCustomization>(1)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(-1)]
	[Theory]
	internal void LowLevelKeyboardProc_InvalidNCode(int nCode, IContext ctx, IInternalContext internalCtx)
	{
		// Given
		CaptureKeybindHook capture = CaptureKeybindHook.Create(internalCtx);
		KeybindHook keybindHook = new(ctx, internalCtx);

		// When
		keybindHook.PostInitialize();
		capture.LowLevelKeyboardProc?.Invoke(nCode, 0, 0);

		// Then
		internalCtx.CoreNativeManager.DidNotReceive().PtrToStructure<KBDLLHOOKSTRUCT>(Arg.Any<nint>());
		internalCtx.CoreNativeManager.Received(1).CallNextHookEx(nCode, 0, 0);
	}

	[Theory, AutoSubstituteData<KeybindHookCustomization>]
	internal void LowLevelKeyboardProc_NotPtrToStructure(IContext ctx, IInternalContext internalCtx)
	{
		// Given
		CaptureKeybindHook capture = CaptureKeybindHook.Create(internalCtx);
		KeybindHook keybindHook = new(ctx, internalCtx);
		internalCtx
			.CoreNativeManager.PtrToStructure<KBDLLHOOKSTRUCT>(Arg.Any<nint>())
			.Returns(null as KBDLLHOOKSTRUCT?);

		// When
		keybindHook.PostInitialize();
		capture.LowLevelKeyboardProc?.Invoke(0, PInvoke.WM_KEYDOWN, 0);

		// Then
		internalCtx.CoreNativeManager.Received(1).PtrToStructure<KBDLLHOOKSTRUCT>(Arg.Any<nint>());
		internalCtx.CoreNativeManager.Received(1).CallNextHookEx(0, PInvoke.WM_KEYDOWN, 0);
	}

	// Values other than WM_KEYDOWN, WM_KEYUP, WM_SYSKEYDOWN, and WM_SYSKEYUP
	[InlineAutoSubstituteData<KeybindHookCustomization>(0x0099)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(0x0102)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(0x0103)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(0x0106)]
	[Theory]
	internal void LowLevelKeyboardProc_ValidNCodeButInvalidWParam(
		uint wParam,
		IContext ctx,
		IInternalContext internalCtx
	)
	{
		// Given
		CaptureKeybindHook capture = CaptureKeybindHook.Create(internalCtx);
		KeybindHook keybindHook = new(ctx, internalCtx);

		// When
		keybindHook.PostInitialize();
		capture.LowLevelKeyboardProc?.Invoke(0, wParam, 0);

		// Then
		internalCtx.CoreNativeManager.DidNotReceive().PtrToStructure<KBDLLHOOKSTRUCT>(Arg.Any<nint>());
		internalCtx.CoreNativeManager.Received(1).CallNextHookEx(0, wParam, 0);
	}

	[InlineAutoSubstituteData<KeybindHookCustomization>(VIRTUAL_KEY.VK_LSHIFT)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(VIRTUAL_KEY.VK_RSHIFT)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(VIRTUAL_KEY.VK_LMENU)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(VIRTUAL_KEY.VK_RMENU)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(VIRTUAL_KEY.VK_LCONTROL)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(VIRTUAL_KEY.VK_RCONTROL)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(VIRTUAL_KEY.VK_LWIN)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(VIRTUAL_KEY.VK_RWIN)]
	[Theory]
	internal void LowLevelKeyboardProc_IgnoredKey(VIRTUAL_KEY key, IContext ctx, IInternalContext internalCtx)
	{
		// Given
		CaptureKeybindHook capture = CaptureKeybindHook.Create(internalCtx);
		KeybindHook keybindHook = new(ctx, internalCtx);
		SetupKey(ctx, internalCtx, [key], VIRTUAL_KEY.None, []);

		// When
		keybindHook.PostInitialize();
		capture.LowLevelKeyboardProc?.Invoke(0, PInvoke.WM_KEYDOWN, 0);

		// Then
		internalCtx.CoreNativeManager.Received(1).CallNextHookEx(0, PInvoke.WM_KEYDOWN, 0);
	}

	[Theory]
	[InlineAutoSubstituteData<KeybindHookCustomization>(VIRTUAL_KEY.VK_LSHIFT)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(VIRTUAL_KEY.VK_RSHIFT)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(VIRTUAL_KEY.VK_LMENU)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(VIRTUAL_KEY.VK_RMENU)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(VIRTUAL_KEY.VK_LCONTROL)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(VIRTUAL_KEY.VK_RCONTROL)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(VIRTUAL_KEY.VK_LWIN)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(VIRTUAL_KEY.VK_RWIN)]
	internal void LowLevelKeyboardProc_OnlyModifierPressed_IgnoresKey(
		VIRTUAL_KEY modifier,
		IContext ctx,
		IInternalContext internalCtx
	)
	{
		// Given: Only the modifier is pressed
		CaptureKeybindHook capture = CaptureKeybindHook.Create(internalCtx);
		KeybindHook keybindHook = new(ctx, internalCtx);

		// Setup so only the modifier is pressed
		ctx.KeybindManager.Modifiers.Returns([modifier]);
		internalCtx.CoreNativeManager.GetKeyState((int)modifier).Returns((short)-32768);
		internalCtx
			.CoreNativeManager.PtrToStructure<KBDLLHOOKSTRUCT>(Arg.Any<nint>())
			.Returns(new KBDLLHOOKSTRUCT { vkCode = (uint)modifier });

		// When
		keybindHook.PostInitialize();
		LRESULT? result = capture.LowLevelKeyboardProc?.Invoke(0, PInvoke.WM_KEYDOWN, 0);

		// Then: Should call next hook
		internalCtx.CoreNativeManager.Received(1).CallNextHookEx(0, PInvoke.WM_KEYDOWN, 0);
		Assert.Equal(0, (nint)result!);
	}

	[Theory, AutoSubstituteData<KeybindHookCustomization>]
	internal void LowLevelKeyboardProc_WinKeySuppressionOff_PassesThroughWithoutDummyInput(
		IContext ctx,
		IInternalContext internalCtx
	)
	{
		// Given
		CaptureKeybindHook capture = CaptureKeybindHook.Create(internalCtx);
		KeybindHook keybindHook = new(ctx, internalCtx);

		ctx.KeybindManager.SuppressBareWinKey.Returns(WinKeySuppressionMode.None);
		ctx.KeybindManager.Modifiers.Returns([VIRTUAL_KEY.VK_LWIN]);
		internalCtx
			.CoreNativeManager.PtrToStructure<KBDLLHOOKSTRUCT>(Arg.Any<nint>())
			.Returns(new KBDLLHOOKSTRUCT { vkCode = (uint)VIRTUAL_KEY.VK_LWIN });

		// When
		keybindHook.PostInitialize();
		LRESULT? result = capture.LowLevelKeyboardProc?.Invoke(0, PInvoke.WM_KEYDOWN, 0);

		// Then
		internalCtx.CoreNativeManager.DidNotReceive().SendInput(Arg.Any<INPUT[]>(), Arg.Any<int>());
		internalCtx.CoreNativeManager.Received(1).CallNextHookEx(0, PInvoke.WM_KEYDOWN, 0);
		Assert.Equal(0, (nint)result!);
	}

	[Theory]
	[InlineAutoSubstituteData<KeybindHookCustomization>(WinKeySuppressionMode.DownE8, 0x0100, 0xE8)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(WinKeySuppressionMode.UpE8, 0x0101, 0xE8)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(WinKeySuppressionMode.DownControl, 0x0100, 0x11)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(WinKeySuppressionMode.UpControl, 0x0101, 0x11)]
	internal void LowLevelKeyboardProc_InjectingMode_SendsConfiguredKeyAtConfiguredTime(
		WinKeySuppressionMode mode,
		uint eventMessage,
		VIRTUAL_KEY suppressionKey,
		IContext ctx,
		IInternalContext internalCtx
	)
	{
		// Given
		CaptureKeybindHook capture = CaptureKeybindHook.Create(internalCtx);
		KeybindHook keybindHook = new(ctx, internalCtx);

		ctx.KeybindManager.SuppressBareWinKey.Returns(mode);
		ctx.KeybindManager.Modifiers.Returns([VIRTUAL_KEY.VK_LWIN]);
		internalCtx
			.CoreNativeManager.PtrToStructure<KBDLLHOOKSTRUCT>(Arg.Any<nint>())
			.Returns(new KBDLLHOOKSTRUCT { vkCode = (uint)VIRTUAL_KEY.VK_LWIN });

		// When
		keybindHook.PostInitialize();
		LRESULT? result = capture.LowLevelKeyboardProc?.Invoke(0, eventMessage, 0);

		// Then
		internalCtx
			.CoreNativeManager.Received(1)
			.SendInput(
				Arg.Is<INPUT[]>(inputs =>
					inputs.Length == 2
					&& inputs[0].type == INPUT_TYPE.INPUT_KEYBOARD
					&& inputs[0].Anonymous.ki.wVk == suppressionKey
					&& inputs[1].type == INPUT_TYPE.INPUT_KEYBOARD
					&& inputs[1].Anonymous.ki.wVk == suppressionKey
					&& inputs[1].Anonymous.ki.dwFlags == KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP
				),
				Arg.Any<int>()
			);
		internalCtx.CoreNativeManager.Received(1).CallNextHookEx(0, eventMessage, 0);
		Assert.Equal(0, (nint)result!);
	}

	[Theory]
	[InlineAutoSubstituteData<KeybindHookCustomization>(WinKeySuppressionMode.DownE8, 0x0101)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(WinKeySuppressionMode.UpE8, 0x0100)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(WinKeySuppressionMode.DownControl, 0x0101)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(WinKeySuppressionMode.UpControl, 0x0100)]
	internal void LowLevelKeyboardProc_InjectingMode_DoesNotSendAtOtherTime(
		WinKeySuppressionMode mode,
		uint eventMessage,
		IContext ctx,
		IInternalContext internalCtx
	)
	{
		// Given
		CaptureKeybindHook capture = CaptureKeybindHook.Create(internalCtx);
		KeybindHook keybindHook = new(ctx, internalCtx);
		ctx.KeybindManager.SuppressBareWinKey.Returns(mode);
		ctx.KeybindManager.Modifiers.Returns([VIRTUAL_KEY.VK_LWIN]);
		internalCtx
			.CoreNativeManager.PtrToStructure<KBDLLHOOKSTRUCT>(Arg.Any<nint>())
			.Returns(new KBDLLHOOKSTRUCT { vkCode = (uint)VIRTUAL_KEY.VK_LWIN });

		// When
		keybindHook.PostInitialize();
		capture.LowLevelKeyboardProc?.Invoke(0, eventMessage, 0);

		// Then
		internalCtx.CoreNativeManager.DidNotReceive().SendInput(Arg.Any<INPUT[]>(), Arg.Any<int>());
	}

	[Theory, AutoSubstituteData<KeybindHookCustomization>]
	internal void LowLevelKeyboardProc_EatBound_EatsWinAndBoundKey(
		IContext ctx,
		IInternalContext internalCtx,
		ICommand command
	)
	{
		// Given
		CaptureKeybindHook capture = CaptureKeybindHook.Create(internalCtx);
		KeybindHook keybindHook = new(ctx, internalCtx);
		ctx.KeybindManager.SuppressBareWinKey.Returns(WinKeySuppressionMode.EatBound);
		ctx.KeybindManager.Modifiers.Returns([VIRTUAL_KEY.VK_LWIN]);
		internalCtx
			.CoreNativeManager.PtrToStructure<KBDLLHOOKSTRUCT>(Arg.Any<nint>())
			.Returns(
				new KBDLLHOOKSTRUCT { vkCode = (uint)VIRTUAL_KEY.VK_LWIN },
				new KBDLLHOOKSTRUCT { vkCode = (uint)VIRTUAL_KEY.VK_A },
				new KBDLLHOOKSTRUCT { vkCode = (uint)VIRTUAL_KEY.VK_LWIN }
			);
		ctx.KeybindManager.GetCommands(new Keybind([VIRTUAL_KEY.VK_LWIN], VIRTUAL_KEY.VK_A)).Returns([command]);

		// When
		keybindHook.PostInitialize();
		LRESULT? winDown = capture.LowLevelKeyboardProc?.Invoke(0, PInvoke.WM_KEYDOWN, 0);
		LRESULT? keyDown = capture.LowLevelKeyboardProc?.Invoke(0, PInvoke.WM_KEYDOWN, 0);
		LRESULT? winUp = capture.LowLevelKeyboardProc?.Invoke(0, PInvoke.WM_KEYUP, 0);

		// Then
		Assert.Equal(1, (nint)winDown!);
		Assert.Equal(1, (nint)keyDown!);
		Assert.Equal(1, (nint)winUp!);
		command.Received(1).TryExecute();
		internalCtx.CoreNativeManager.DidNotReceive().SendInput(Arg.Any<INPUT[]>(), Arg.Any<int>());
	}

	[Theory, AutoSubstituteData<KeybindHookCustomization>]
	internal void LowLevelKeyboardProc_EatBound_ReplaysWinDownForUnboundKey(IContext ctx, IInternalContext internalCtx)
	{
		// Given
		CaptureKeybindHook capture = CaptureKeybindHook.Create(internalCtx);
		KeybindHook keybindHook = new(ctx, internalCtx);
		ctx.KeybindManager.SuppressBareWinKey.Returns(WinKeySuppressionMode.EatBound);
		ctx.KeybindManager.Modifiers.Returns([VIRTUAL_KEY.VK_LWIN]);
		internalCtx
			.CoreNativeManager.PtrToStructure<KBDLLHOOKSTRUCT>(Arg.Any<nint>())
			.Returns(
				new KBDLLHOOKSTRUCT { vkCode = (uint)VIRTUAL_KEY.VK_LWIN },
				new KBDLLHOOKSTRUCT { vkCode = (uint)VIRTUAL_KEY.VK_R },
				new KBDLLHOOKSTRUCT { vkCode = (uint)VIRTUAL_KEY.VK_LWIN }
			);
		ctx.KeybindManager.GetCommands(Arg.Any<IKeybind>()).Returns([]);

		// When
		keybindHook.PostInitialize();
		LRESULT? winDown = capture.LowLevelKeyboardProc?.Invoke(0, PInvoke.WM_KEYDOWN, 0);
		LRESULT? keyDown = capture.LowLevelKeyboardProc?.Invoke(0, PInvoke.WM_KEYDOWN, 0);
		LRESULT? winUp = capture.LowLevelKeyboardProc?.Invoke(0, PInvoke.WM_KEYUP, 0);

		// Then
		Assert.Equal(1, (nint)winDown!);
		Assert.Equal(0, (nint)keyDown!);
		Assert.Equal(0, (nint)winUp!);
		internalCtx
			.CoreNativeManager.Received(1)
			.SendInput(
				Arg.Is<INPUT[]>(inputs =>
					inputs.Length == 1
					&& inputs[0].Anonymous.ki.wVk == VIRTUAL_KEY.VK_LWIN
					&& inputs[0].Anonymous.ki.dwFlags == default
				),
				Arg.Any<int>()
			);
	}

	[Theory]
	[InlineAutoSubstituteData<KeybindHookCustomization>(WinKeySuppressionMode.EatBound, 1)]
	[InlineAutoSubstituteData<KeybindHookCustomization>(WinKeySuppressionMode.EatBoundAndBareTap, 0)]
	internal void LowLevelKeyboardProc_EatingMode_HandlesBareTap(
		WinKeySuppressionMode mode,
		int expectedInputs,
		IContext ctx,
		IInternalContext internalCtx
	)
	{
		// Given
		CaptureKeybindHook capture = CaptureKeybindHook.Create(internalCtx);
		KeybindHook keybindHook = new(ctx, internalCtx);
		ctx.KeybindManager.SuppressBareWinKey.Returns(mode);
		ctx.KeybindManager.Modifiers.Returns([VIRTUAL_KEY.VK_LWIN]);
		internalCtx
			.CoreNativeManager.PtrToStructure<KBDLLHOOKSTRUCT>(Arg.Any<nint>())
			.Returns(new KBDLLHOOKSTRUCT { vkCode = (uint)VIRTUAL_KEY.VK_LWIN });

		// When
		keybindHook.PostInitialize();
		LRESULT? winDown = capture.LowLevelKeyboardProc?.Invoke(0, PInvoke.WM_KEYDOWN, 0);
		LRESULT? winUp = capture.LowLevelKeyboardProc?.Invoke(0, PInvoke.WM_KEYUP, 0);

		// Then
		Assert.Equal(1, (nint)winDown!);
		Assert.Equal(1, (nint)winUp!);
		internalCtx.CoreNativeManager.Received(expectedInputs).SendInput(Arg.Any<INPUT[]>(), Arg.Any<int>());
	}

	public static readonly TheoryData<VIRTUAL_KEY[], VIRTUAL_KEY, Keybind> KeybindsToExecute = new()
	{
		// Standard modifier combinations
		{
			new VIRTUAL_KEY[] { VIRTUAL_KEY.VK_LSHIFT },
			VIRTUAL_KEY.VK_A,
			new Keybind([VIRTUAL_KEY.VK_LSHIFT], VIRTUAL_KEY.VK_A)
		},
		{
			new VIRTUAL_KEY[] { VIRTUAL_KEY.VK_RSHIFT },
			VIRTUAL_KEY.VK_A,
			new Keybind([VIRTUAL_KEY.VK_RSHIFT], VIRTUAL_KEY.VK_A)
		},
		{
			new VIRTUAL_KEY[] { VIRTUAL_KEY.VK_LMENU, VIRTUAL_KEY.VK_LCONTROL },
			VIRTUAL_KEY.VK_A,
			new Keybind(modifiers: [VIRTUAL_KEY.VK_LMENU, VIRTUAL_KEY.VK_LCONTROL], VIRTUAL_KEY.VK_A)
		},
		{
			new VIRTUAL_KEY[] { VIRTUAL_KEY.VK_RMENU, VIRTUAL_KEY.VK_RCONTROL },
			VIRTUAL_KEY.VK_A,
			new Keybind([VIRTUAL_KEY.VK_RMENU, VIRTUAL_KEY.VK_RCONTROL], VIRTUAL_KEY.VK_A)
		},
		{
			new VIRTUAL_KEY[] { VIRTUAL_KEY.VK_LWIN },
			VIRTUAL_KEY.VK_A,
			new Keybind([VIRTUAL_KEY.VK_LWIN], VIRTUAL_KEY.VK_A)
		},
		{
			new VIRTUAL_KEY[] { VIRTUAL_KEY.VK_RWIN },
			VIRTUAL_KEY.VK_A,
			new Keybind([VIRTUAL_KEY.VK_RWIN], VIRTUAL_KEY.VK_A)
		},
		// Non-standard modifier combinations
		{
			new VIRTUAL_KEY[] { VIRTUAL_KEY.VK_LWIN, VIRTUAL_KEY.VK_LSHIFT, VIRTUAL_KEY.VK_LCONTROL },
			VIRTUAL_KEY.VK_A,
			new Keybind([VIRTUAL_KEY.VK_LWIN, VIRTUAL_KEY.VK_LSHIFT, VIRTUAL_KEY.VK_LCONTROL], VIRTUAL_KEY.VK_A)
		},
		{
			new VIRTUAL_KEY[] { VIRTUAL_KEY.VK_RWIN, VIRTUAL_KEY.VK_RMENU },
			VIRTUAL_KEY.VK_A,
			new Keybind([VIRTUAL_KEY.VK_RWIN, VIRTUAL_KEY.VK_RMENU], VIRTUAL_KEY.VK_A)
		},
		{
			new VIRTUAL_KEY[] { VIRTUAL_KEY.VK_LSHIFT, VIRTUAL_KEY.VK_RMENU },
			VIRTUAL_KEY.VK_A,
			new Keybind([VIRTUAL_KEY.VK_LSHIFT, VIRTUAL_KEY.VK_RMENU], VIRTUAL_KEY.VK_A)
		},
		{
			new VIRTUAL_KEY[] { VIRTUAL_KEY.VK_LWIN, VIRTUAL_KEY.VK_LCONTROL, VIRTUAL_KEY.VK_RMENU },
			VIRTUAL_KEY.VK_A,
			new Keybind([VIRTUAL_KEY.VK_LWIN, VIRTUAL_KEY.VK_LCONTROL, VIRTUAL_KEY.VK_RMENU], VIRTUAL_KEY.VK_A)
		},
		// Additional non-standard modifier combinations
		{
			new VIRTUAL_KEY[] { VIRTUAL_KEY.VK_LSHIFT, VIRTUAL_KEY.VK_SPACE },
			VIRTUAL_KEY.VK_B,
			new Keybind([VIRTUAL_KEY.VK_LSHIFT, VIRTUAL_KEY.VK_SPACE], VIRTUAL_KEY.VK_B)
		},
		{
			new VIRTUAL_KEY[] { VIRTUAL_KEY.VK_LCONTROL, VIRTUAL_KEY.VK_OEM_2 },
			VIRTUAL_KEY.VK_C,
			new Keybind([VIRTUAL_KEY.VK_LCONTROL, VIRTUAL_KEY.VK_OEM_2], VIRTUAL_KEY.VK_C)
		},
		{
			new VIRTUAL_KEY[] { VIRTUAL_KEY.VK_RMENU, VIRTUAL_KEY.VK_TAB },
			VIRTUAL_KEY.VK_D,
			new Keybind([VIRTUAL_KEY.VK_RMENU, VIRTUAL_KEY.VK_TAB], VIRTUAL_KEY.VK_D)
		},
		{
			new VIRTUAL_KEY[] { VIRTUAL_KEY.VK_LWIN, VIRTUAL_KEY.VK_ESCAPE },
			VIRTUAL_KEY.VK_E,
			new Keybind([VIRTUAL_KEY.VK_LWIN, VIRTUAL_KEY.VK_ESCAPE], VIRTUAL_KEY.VK_E)
		},
		{
			new VIRTUAL_KEY[] { VIRTUAL_KEY.VK_RSHIFT, VIRTUAL_KEY.VK_OEM_PLUS },
			VIRTUAL_KEY.VK_F,
			new Keybind([VIRTUAL_KEY.VK_RSHIFT, VIRTUAL_KEY.VK_OEM_PLUS], VIRTUAL_KEY.VK_F)
		},
		{
			new VIRTUAL_KEY[] { VIRTUAL_KEY.VK_OEM_COMMA, VIRTUAL_KEY.VK_OEM_PERIOD },
			VIRTUAL_KEY.VK_G,
			new Keybind([VIRTUAL_KEY.VK_OEM_COMMA, VIRTUAL_KEY.VK_OEM_PERIOD], VIRTUAL_KEY.VK_G)
		},
		{
			new VIRTUAL_KEY[] { VIRTUAL_KEY.VK_F1, VIRTUAL_KEY.VK_F2 },
			VIRTUAL_KEY.VK_H,
			new Keybind([VIRTUAL_KEY.VK_F1, VIRTUAL_KEY.VK_F2], VIRTUAL_KEY.VK_H)
		},
	};

	[MemberData(nameof(KeybindsToExecute))]
	[Theory]
	internal void LowLevelKeyboardProc_ValidKeybind(VIRTUAL_KEY[] modifiers, VIRTUAL_KEY key, Keybind keybind)
	{
		// Given
		IContext ctx = Substitute.For<IContext>();
		IInternalContext internalCtx = Substitute.For<IInternalContext>();

		CaptureKeybindHook capture = CaptureKeybindHook.Create(internalCtx);
		KeybindHook keybindHook = new(ctx, internalCtx);
		ICommand[] commands = [.. Enumerable.Range(0, 3).Select(_ => Substitute.For<ICommand>())];
		SetupKey(ctx, internalCtx, modifiers, key, [.. commands.Select(c => c)]);

		// When
		keybindHook.PostInitialize();
		LRESULT? result = capture.LowLevelKeyboardProc?.Invoke(0, PInvoke.WM_SYSKEYDOWN, 0);

		// Then
		internalCtx.CoreNativeManager.DidNotReceive().CallNextHookEx(0, PInvoke.WM_KEYDOWN, 0);
		Assert.Equal(1, (nint)result!);
		ctx.KeybindManager.Received(1).GetCommands(keybind);
		foreach (ICommand command in commands)
		{
			command.Received(1).TryExecute();
		}
	}

	[Theory, AutoSubstituteData<KeybindHookCustomization>]
	internal void LowLevelKeyboardProc_NoModifiers(IContext ctx, IInternalContext internalCtx)
	{
		// Given
		CaptureKeybindHook capture = CaptureKeybindHook.Create(internalCtx);
		KeybindHook keybindHook = new(ctx, internalCtx);
		SetupKey(ctx, internalCtx, [], VIRTUAL_KEY.VK_A, []);

		// When
		keybindHook.PostInitialize();
		LRESULT? result = capture.LowLevelKeyboardProc?.Invoke(0, PInvoke.WM_KEYDOWN, 0);

		// Then
		internalCtx.CoreNativeManager.Received(1).CallNextHookEx(0, PInvoke.WM_KEYDOWN, 0);
		Assert.Equal(0, (nint)result!);
		ctx.KeybindManager.Received(1).GetCommands(new Keybind([], VIRTUAL_KEY.VK_A));
	}

	[Theory, AutoSubstituteData<KeybindHookCustomization>]
	internal void LowLevelKeyboardProc_NoCommands(IContext ctx, IInternalContext internalCtx)
	{
		// Given
		CaptureKeybindHook capture = CaptureKeybindHook.Create(internalCtx);
		KeybindHook keybindHook = new(ctx, internalCtx);
		SetupKey(ctx, internalCtx, [VIRTUAL_KEY.VK_LWIN], VIRTUAL_KEY.VK_U, []);

		// When
		keybindHook.PostInitialize();
		LRESULT? result = capture.LowLevelKeyboardProc?.Invoke(0, PInvoke.WM_KEYDOWN, 0);

		// Then
		internalCtx.CoreNativeManager.Received(1).CallNextHookEx(0, PInvoke.WM_KEYDOWN, 0);
		Assert.Equal(0, (nint)result!);
		ctx.KeybindManager.Received(1).GetCommands(new Keybind([VIRTUAL_KEY.VK_LWIN], VIRTUAL_KEY.VK_U));
	}

	[Theory, AutoSubstituteData<KeybindHookCustomization>]
	internal void LowLevelKeyboardProc_HandleException(IContext ctx, IInternalContext internalCtx)
	{
		// Given
		CaptureKeybindHook capture = CaptureKeybindHook.Create(internalCtx);
		KeybindHook keybindHook = new(ctx, internalCtx);

		// When
		keybindHook.PostInitialize();
		internalCtx
			.CoreNativeManager.PtrToStructure<KBDLLHOOKSTRUCT>(Arg.Any<nint>())
			.Returns(_ => throw new Exception());
		LRESULT? result = capture.LowLevelKeyboardProc?.Invoke(0, PInvoke.WM_SYSKEYDOWN, 0);

		// Then
		internalCtx.CoreNativeManager.DidNotReceive().CallNextHookEx(0, PInvoke.WM_KEYDOWN, 0);
	}

	[Theory, AutoSubstituteData<KeybindHookCustomization>]
	internal void Dispose_HookIsNull(IContext ctx, IInternalContext internalCtx)
	{
		// Given
		CaptureKeybindHook capture = CaptureKeybindHook.Create(internalCtx);
		KeybindHook keybindHook = new(ctx, internalCtx);

		// When
		keybindHook.Dispose();

		// Then
		Assert.Null(capture.Handle);
	}

	[Theory, AutoSubstituteData<KeybindHookCustomization>]
	internal void Dispose(IContext ctx, IInternalContext internalCtx)
	{
		// Given
		CaptureKeybindHook capture = CaptureKeybindHook.Create(internalCtx);
		KeybindHook keybindHook = new(ctx, internalCtx);
		SetupKey(ctx, internalCtx, [], VIRTUAL_KEY.VK_A, []);

		// When
		keybindHook.PostInitialize();
		keybindHook.Dispose();

		// Then
		Assert.True(capture.Handle?.HasDisposed);
	}
}
