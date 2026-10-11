using System.Globalization;
using Mokaterm.Modules.Ssh.Elevation;

namespace Mokaterm.Modules.Ssh.Admin;

/// <summary>Sentences the system view says in more than one place.</summary>
internal static class AdminTexts
{
	public const string RootModeNeeded = "Turn on root mode to read this.";

	public static string TimedOut(TimeSpan timeout) => timeout.TotalMinutes >= 1
		? string.Create(CultureInfo.CurrentCulture, $"The command did not finish within {timeout.TotalMinutes:0} minutes, so it was stopped.")
		: string.Create(CultureInfo.CurrentCulture, $"The command did not finish within {timeout.TotalSeconds:0} seconds, so it was stopped.");

	/// <summary>Why sudo did not run a command, in the system view's words where the file browser's would mislead.</summary>
	public static string SudoRefused(SudoFailure failure, string account, string sudoOutput) => failure switch
	{
		SudoFailure.TerminalRequired => $"sudo for {account} only runs from a terminal (requiretty), so this view cannot run anything as root.",
		_ => SudoErrors.Describe(failure, account, sudoOutput),
	};
}
