using System.Diagnostics.CodeAnalysis;

namespace Mokaterm.Modules.Local.Shells;

/// <summary>
/// Decides what a login runs. Its own shell wins; without one, the host's address names the shell, the way a serial
/// login's address names its port; and an address that names this computer (localhost, its own name) runs the
/// default shell from Settings.
/// </summary>
internal static class LocalShellResolver
{
	/// <summary>Addresses that mean this computer itself rather than a shell on it.</summary>
	private static readonly string[] ThisComputer = ["localhost", "127.0.0.1", "::1"];

	public static bool TryResolve(
		LocalConnectionOptions options,
		string hostAddress,
		LocalSettings settings,
		IReadOnlyList<LocalShell> shells,
		LocalMachine machine,
		[NotNullWhen(true)] out LocalShell? shell,
		[NotNullWhen(false)] out string? problem)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(shells);
		ArgumentNullException.ThrowIfNull(machine);

		if (options.IsCustom)
		{
			return TryCustom(options.Program, machine, out shell, out problem);
		}

		if (options.Shell?.Trim() is { Length: > 0 } chosen)
		{
			shell = Find(shells, chosen);
			problem = shell is null ? $"{Capitalized(LocalShellIds.Describe(chosen))} is not installed on this computer." : null;
			return shell is not null;
		}

		string address = hostAddress?.Trim() ?? "";
		if (NamesThisComputer(address, machine))
		{
			return TryDefault(settings, shells, out shell, out problem);
		}

		shell = Find(shells, address);
		problem = shell is null
			? $"No shell on this computer is called {address}. Call the host by a shell's name (pwsh, powershell, cmd, git-bash or a WSL distribution), call it localhost for the default shell, or pick a shell in the login's options."
			: null;
		return shell is not null;
	}

	/// <summary>
	/// The shell an id, an address or a WSL distribution's name stands for. <c>wsl</c> alone is the default
	/// distribution, and a program's file name (<c>pwsh.exe</c>, <c>cmd.exe</c>) counts as its id.
	/// </summary>
	public static LocalShell? Find(IReadOnlyList<LocalShell> shells, string name)
	{
		ArgumentNullException.ThrowIfNull(shells);
		string key = name?.Trim() ?? "";
		if (key.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
		{
			key = key[..^4];
		}

		if (key.Length == 0)
		{
			return null;
		}

		if (key.Equals("gitbash", StringComparison.OrdinalIgnoreCase))
		{
			key = LocalShellIds.GitBash;
		}

		// The detector puts the default distribution first.
		if (key.Equals(LocalShellIds.Wsl, StringComparison.OrdinalIgnoreCase))
		{
			return shells.FirstOrDefault(shell => shell.Kind == LocalShellKind.Wsl);
		}

		if (LocalShellIds.TryGetDistribution(key, out string? distribution))
		{
			key = distribution;
		}

		return shells.FirstOrDefault(shell => shell.Kind != LocalShellKind.Wsl && shell.Id.Equals(key, StringComparison.OrdinalIgnoreCase))
			?? shells.FirstOrDefault(shell => shell.Distribution is { } named && named.Equals(key, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// The shell Settings names when it is here, otherwise the first of PowerShell 7, Windows PowerShell and the
	/// Command Prompt: a default that went missing is not worth refusing a connect over.
	/// </summary>
	public static LocalShell? Default(LocalSettings settings, IReadOnlyList<LocalShell> shells)
	{
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(shells);
		return (settings.DefaultShell is { Length: > 0 } id ? Find(shells, id) : null)
			?? shells.FirstOrDefault(shell => shell.Kind is LocalShellKind.PowerShell or LocalShellKind.WindowsPowerShell or LocalShellKind.CommandPrompt)
			?? (shells.Count > 0 ? shells[0] : null);
	}

	public static bool NamesThisComputer(string address, LocalMachine machine)
	{
		ArgumentNullException.ThrowIfNull(machine);
		return ThisComputer.Contains(address, StringComparer.OrdinalIgnoreCase)
			|| (machine.MachineName.Length > 0 && address.Equals(machine.MachineName, StringComparison.OrdinalIgnoreCase));
	}

	private static bool TryDefault(
		LocalSettings settings,
		IReadOnlyList<LocalShell> shells,
		[NotNullWhen(true)] out LocalShell? shell,
		[NotNullWhen(false)] out string? problem)
	{
		shell = Default(settings, shells);
		problem = shell is null ? "No shell was found on this computer." : null;
		return shell is not null;
	}

	private static bool TryCustom(
		string? program,
		LocalMachine machine,
		[NotNullWhen(true)] out LocalShell? shell,
		[NotNullWhen(false)] out string? problem)
	{
		shell = null;
		string? path = LocalConnectionOptions.ProgramPath(program);
		if (path is null)
		{
			problem = "Name the program this login runs in its options.";
			return false;
		}

		if (LocalProgramPath.IsScript(path))
		{
			problem = $"{path} is a script, which needs a shell to run it. Pick the Command Prompt or PowerShell and give the script in the arguments.";
			return false;
		}

		if (LocalProgramPath.Find(path, machine) is not { } found)
		{
			problem = $"{path} was not found. Give its full path, or the name of a program on PATH.";
			return false;
		}

		shell = new LocalShell
		{
			Id = LocalShellIds.Custom,
			Kind = LocalShellKind.Custom,
			DisplayName = Path.GetFileName(found),
			Program = found,
		};
		problem = null;
		return true;
	}

	private static string Capitalized(string text) =>
		text.Length == 0 ? text : string.Concat(char.ToUpperInvariant(text[0]).ToString(), text.AsSpan(1));
}
