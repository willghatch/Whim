namespace Whim;

/// <summary>
/// Controls how Whim prevents the Windows key from opening the Start menu.
/// </summary>
public enum WinKeySuppressionMode
{
	/// <summary>Do not suppress the Windows key.</summary>
	None,

	/// <summary>Send an unassigned <c>0xE8</c> key tap when the Windows key is pressed.</summary>
	DownE8,

	/// <summary>Send an unassigned <c>0xE8</c> key tap when the Windows key is released.</summary>
	UpE8,

	/// <summary>Send a Control key tap when the Windows key is pressed.</summary>
	DownControl,

	/// <summary>Send a Control key tap when the Windows key is released.</summary>
	UpControl,

	/// <summary>
	/// Hide the Windows key from Windows when it modifies a Whim keybind, while preserving native
	/// Windows shortcuts and bare Windows key taps.
	/// </summary>
	EatBound,

	/// <summary>
	/// Hide the Windows key from Windows when it modifies a Whim keybind or is pressed by itself,
	/// while preserving native Windows shortcuts.
	/// </summary>
	EatBoundAndBareTap,
}
