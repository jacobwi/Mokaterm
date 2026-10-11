using System.Diagnostics.CodeAnalysis;
using Mokaterm.Modules.Local.Pty;

namespace Mokaterm.Modules.Local.Shells;

/// <summary>
/// Turns a shell and a login's options into the program a pseudo terminal starts: its arguments, the folder it starts
/// in and its environment.
/// </summary>
internal static class LocalLaunchBuilder
{
	/// <summary>
	/// The host's own WebView2 settings. The desktop app points <c>WEBVIEW2_USER_DATA_FOLDER</c> at its own browser
	/// profile, and any WebView2 program started from the shell would read it and open, or fight over, that profile.
	/// </summary>
	private const string HostOnlyPrefix = "WEBVIEW2_";

	/// <summary>Names the variables a WSL distribution takes over from Windows, separated by colons.</summary>
	private const string WslSharedVariables = "WSLENV";

	private const string WslHome = "~";

	public static bool TryBuild(
		LocalShell shell,
		LocalConnectionOptions options,
		LocalSettings settings,
		LocalMachine machine,
		string terminalType,
		[NotNullWhen(true)] out PseudoTerminalLaunch? launch,
		[NotNullWhen(false)] out string? problem)
	{
		ArgumentNullException.ThrowIfNull(shell);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(machine);
		launch = null;

		if (!LocalEnvironmentVariables.TryParse(options.EnvironmentVariables, out IReadOnlyList<KeyValuePair<string, string>> added, out problem))
		{
			problem = $"The login's environment variables cannot be used. {problem}";
			return false;
		}

		string folder = LocalVariables.Expand((options.StartingFolder ?? settings.StartingFolder ?? "").Trim(), machine.Variable);
		Dictionary<string, string> environment = InheritedEnvironment(machine);
		List<string> arguments = [];
		string workingDirectory;
		bool wsl = shell.Kind == LocalShellKind.Wsl;
		if (wsl)
		{
			// wsl.exe goes where --cd says, which takes ~, a Linux path or a Windows one; its own folder only has to exist.
			workingDirectory = machine.UserProfile;
			if (shell.Distribution is { } distribution)
			{
				arguments.Add("-d");
				arguments.Add(distribution);
			}

			arguments.Add("--cd");
			arguments.Add(folder.Length == 0 ? WslHome : folder);
			ShareWithWsl(environment, "TERM", terminalType);
		}
		else
		{
			if (!TryResolveFolder(folder, machine, out workingDirectory, out problem))
			{
				return false;
			}

			if (shell.Kind == LocalShellKind.GitBash)
			{
				arguments.Add("--login");
				arguments.Add("-i");
				environment["TERM"] = terminalType;

				// The MSYS2 login profile goes to the home folder unless this is set, which would undo the starting folder.
				environment["CHERE_INVOKING"] = "1";
			}
		}

		foreach ((string name, string value) in added)
		{
			if (value.Length == 0)
			{
				environment.Remove(name);
			}
			else if (wsl)
			{
				ShareWithWsl(environment, name, value);
			}
			else
			{
				environment[name] = value;
			}
		}

		launch = new PseudoTerminalLaunch
		{
			Program = shell.Program,
			Arguments = arguments,
			ExtraArguments = string.IsNullOrWhiteSpace(options.Arguments) ? null : options.Arguments.Trim(),
			WorkingDirectory = workingDirectory,
			Environment = environment,
		};
		problem = null;
		return true;
	}

	/// <summary>
	/// What every program this module starts inherits: this computer's environment without the host's own WebView2
	/// settings. A shell, docker.exe and kubectl.exe all get it, and so does what they start in turn.
	/// </summary>
	public static Dictionary<string, string> InheritedEnvironment(LocalMachine machine)
	{
		ArgumentNullException.ThrowIfNull(machine);
		Dictionary<string, string> environment = new(StringComparer.OrdinalIgnoreCase);
		foreach ((string name, string value) in machine.Variables)
		{
			if (!name.StartsWith(HostOnlyPrefix, StringComparison.OrdinalIgnoreCase))
			{
				environment[name] = value;
			}
		}

		return environment;
	}

	/// <summary>
	/// The folder a Windows shell starts in: the profile when nothing names one, a leading <c>~</c> as the profile, and
	/// otherwise a full path that exists. A relative one is refused, since it would be relative to this app.
	/// </summary>
	private static bool TryResolveFolder(
		string folder,
		LocalMachine machine,
		out string workingDirectory,
		[NotNullWhen(false)] out string? problem)
	{
		workingDirectory = machine.UserProfile;
		problem = null;
		if (folder.Length == 0)
		{
			return true;
		}

		string path = folder == WslHome ? machine.UserProfile
			: folder.StartsWith("~\\", StringComparison.Ordinal) || folder.StartsWith("~/", StringComparison.Ordinal) ? Path.Join(machine.UserProfile, folder[2..])
			: folder;

		if (!Path.IsPathFullyQualified(path))
		{
			problem = $"The starting folder {folder} is not a full path. Give one from its drive letter on, or start it with ~ for your profile folder.";
			return false;
		}

		if (!machine.DirectoryExists(path))
		{
			problem = $"The starting folder {path} does not exist.";
			return false;
		}

		workingDirectory = path;
		return true;
	}

	/// <summary>
	/// Sets a variable and lists it in <c>WSLENV</c>, which is the only way a value from Windows reaches the Linux side.
	/// </summary>
	private static void ShareWithWsl(Dictionary<string, string> environment, string name, string value)
	{
		environment[name] = value;
		string shared = environment.TryGetValue(WslSharedVariables, out string? existing) ? existing : "";
		bool listed = shared
			.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Any(entry => entry.Split('/')[0].Equals(name, StringComparison.OrdinalIgnoreCase));
		if (!listed)
		{
			environment[WslSharedVariables] = shared.Length == 0 ? name : $"{shared.TrimEnd(':')}:{name}";
		}
	}
}
