using Mokaterm.Abstractions.Security;

namespace Mokaterm.Abstractions.Settings;

public sealed record SecuritySettings : ISettingsSection
{
	/// <summary>0 turns auto-lock off; a day is as far out as the page goes.</summary>
	public const int MinAutoLockMinutes = 0;

	/// <inheritdoc cref="MinAutoLockMinutes"/>
	public const int MaxAutoLockMinutes = 1440;

	/// <summary>0 leaves a copied secret on the clipboard.</summary>
	public const int MinClipboardClearSeconds = 0;

	/// <inheritdoc cref="MinClipboardClearSeconds"/>
	public const int MaxClipboardClearSeconds = 600;

	public static string SectionKey => "security";

	/// <summary>Lock the vault after this many minutes without input. 0 turns auto-lock off.</summary>
	public int AutoLockMinutes { get; init; } = 15;

	/// <summary>Desktop only: lock when the window is minimized or hidden.</summary>
	public bool LockWhenHidden { get; init; }

	/// <summary>Clear the clipboard this many seconds after copying a secret. 0 leaves it.</summary>
	public int ClipboardClearSeconds { get; init; } = 30;

	public HostKeyPolicy HostKeyPolicy { get; init; } = HostKeyPolicy.Ask;

	/// <summary>Keep a sudo password in memory for the session after the first prompt.</summary>
	public bool RememberSudoPasswordForSession { get; init; } = true;

	/// <summary>A copy with both timers inside the range the settings page offers.</summary>
	public SecuritySettings Clamped() => this with
	{
		AutoLockMinutes = Math.Clamp(AutoLockMinutes, MinAutoLockMinutes, MaxAutoLockMinutes),
		ClipboardClearSeconds = Math.Clamp(ClipboardClearSeconds, MinClipboardClearSeconds, MaxClipboardClearSeconds),
	};
}
