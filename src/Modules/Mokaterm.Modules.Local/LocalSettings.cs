using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Local.Shells;

namespace Mokaterm.Modules.Local;

/// <summary>
/// What belongs to the app rather than to one login: the shell a login runs when nothing else names one, where shells
/// start, and whether a failing exit code is shown. Stored under <c>local</c> in settings.json.
/// </summary>
public sealed record LocalSettings : ISettingsSection
{
	public static string SectionKey => "local";

	/// <summary>
	/// The shell a host called localhost (or by this computer's name) runs when its login picks none: <c>pwsh</c>,
	/// <c>powershell</c>, <c>cmd</c>, <c>git-bash</c> or <c>wsl:</c> and a distribution's name. Empty takes the first
	/// one installed of PowerShell, Windows PowerShell and the Command Prompt.
	/// </summary>
	[MachineSpecific]
	public string DefaultShell { get; init; } = "";

	/// <summary>
	/// Where a shell starts when its login names no folder. Empty is the user's profile folder, and the Linux home for a
	/// WSL distribution.
	/// </summary>
	[MachineSpecific]
	public string StartingFolder { get; init; } = "";

	/// <summary>
	/// Writes the code into the terminal when a shell exits with anything but 0, which is how a program that fails at
	/// once says why the tab closed.
	/// </summary>
	public bool ReportExitCode { get; init; } = true;

	/// <summary>
	/// A copy every login can be started from. A hand-edited file can hold nulls, and a custom program has no place
	/// here: it needs a path, which only a login carries.
	/// </summary>
	public LocalSettings Clamped()
	{
		string shell = DefaultShell?.Trim() ?? "";
		return this with
		{
			DefaultShell = string.Equals(shell, LocalShellIds.Custom, StringComparison.OrdinalIgnoreCase) ? "" : shell,
			StartingFolder = StartingFolder?.Trim() ?? "",
		};
	}
}
