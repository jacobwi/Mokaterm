namespace Mokaterm.Modules.Ssh.Elevation;

/// <summary>Why sudo refused to run a command.</summary>
internal enum SudoFailure
{
	IncorrectPassword,
	PasswordRequired,
	NotAllowed,
	TerminalRequired,
	NotInstalled,
	Other,
}

/// <summary>Reads sudo's stderr (C locale) and turns it into a reason and a message for the user.</summary>
internal static class SudoErrors
{
	private const int CommandNotFoundExitStatus = 127;

	private static readonly string[] IncorrectPasswordMarkers =
	[
		"incorrect password attempt",
		"Sorry, try again",
		"no password was provided",
		"Authentication failure",
	];

	private static readonly string[] NotAllowedMarkers =
	[
		"not in the sudoers file",
		"is not allowed to execute",
		"is not allowed to run sudo",
		"may not run sudo",
		"account validation failure",
		"Account or password is expired",
	];

	private static readonly string[] TerminalRequiredMarkers =
	[
		"must have a tty",
		"a terminal is required",
		"no tty present",
	];

	private static readonly string[] NotInstalledMarkers =
	[
		"sudo: not found",
		"sudo: command not found",
		"sudo: No such file or directory",
	];

	public static SudoFailure Classify(string error, int? exitStatus)
	{
		ArgumentNullException.ThrowIfNull(error);
		if (ContainsAny(error, IncorrectPasswordMarkers))
		{
			return SudoFailure.IncorrectPassword;
		}

		if (error.Contains("a password is required", StringComparison.OrdinalIgnoreCase))
		{
			return SudoFailure.PasswordRequired;
		}

		if (ContainsAny(error, NotAllowedMarkers))
		{
			return SudoFailure.NotAllowed;
		}

		if (ContainsAny(error, TerminalRequiredMarkers))
		{
			return SudoFailure.TerminalRequired;
		}

		if (exitStatus == CommandNotFoundExitStatus || ContainsAny(error, NotInstalledMarkers))
		{
			return SudoFailure.NotInstalled;
		}

		return SudoFailure.Other;
	}

	/// <param name="account">For example <c>abc@10.10.2.3</c>.</param>
	public static string Describe(SudoFailure failure, string account, string error) => failure switch
	{
		SudoFailure.IncorrectPassword => $"sudo did not accept the password for {account}.",
		SudoFailure.PasswordRequired => $"sudo needs a password for {account}.",
		SudoFailure.NotAllowed => $"{account} is not allowed to run commands as root with sudo.",
		SudoFailure.TerminalRequired => $"sudo on {HostOf(account)} only runs from a terminal (requiretty), so file operations cannot run as root.",
		SudoFailure.NotInstalled => $"sudo is not installed on {HostOf(account)}.",
		_ => FirstLine(error) is { Length: > 0 } line ? $"sudo failed: {line}" : "sudo failed.",
	};

	/// <summary>
	/// Splits stderr at <see cref="Shell.RemoteScripts.ElevatedMarker"/>: before it is sudo's own output, after it the
	/// script's. No marker means sudo never ran the script.
	/// </summary>
	public static (bool Elevated, string SudoOutput, string ScriptError) Split(string error)
	{
		ArgumentNullException.ThrowIfNull(error);
		string marker = Shell.RemoteScripts.ElevatedMarker + "\n";
		int index = error.IndexOf(marker, StringComparison.Ordinal);
		return index < 0
			? (false, error, "")
			: (true, error[..index], error[(index + marker.Length)..]);
	}

	internal static string FirstLine(string text)
	{
		foreach (string line in text.Split('\n'))
		{
			string trimmed = line.Trim();
			if (trimmed.Length > 0)
			{
				return trimmed;
			}
		}

		return "";
	}

	private static bool ContainsAny(string text, string[] markers) =>
		markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

	private static string HostOf(string account)
	{
		int at = account.LastIndexOf('@');
		return at < 0 ? account : account[(at + 1)..];
	}
}
