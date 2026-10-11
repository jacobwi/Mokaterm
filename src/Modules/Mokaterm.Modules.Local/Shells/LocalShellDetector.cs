namespace Mokaterm.Modules.Local.Shells;

/// <summary>
/// Finds the shells this computer has: PowerShell 7, Windows PowerShell, the Command Prompt, Git Bash and every WSL
/// distribution, in that order, which is also the order the default falls back through.
/// </summary>
internal static class LocalShellDetector
{
	public static IReadOnlyList<LocalShell> Detect(LocalMachine machine)
	{
		ArgumentNullException.ThrowIfNull(machine);
		List<LocalShell> shells = [];
		if (FindPowerShell(machine) is { } pwsh)
		{
			shells.Add(new LocalShell
			{
				Id = LocalShellIds.PowerShell,
				Kind = LocalShellKind.PowerShell,
				DisplayName = "PowerShell",
				Program = pwsh,
			});
		}

		string? system = machine.Variable("SystemRoot") is { } windows ? Path.Combine(windows, "System32") : null;
		if (system is not null)
		{
			AddIfPresent(shells, machine, new LocalShell
			{
				Id = LocalShellIds.WindowsPowerShell,
				Kind = LocalShellKind.WindowsPowerShell,
				DisplayName = "Windows PowerShell",
				Program = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe"),
			});

			// By its own path rather than %ComSpec%, which can name another command processor.
			AddIfPresent(shells, machine, new LocalShell
			{
				Id = LocalShellIds.CommandPrompt,
				Kind = LocalShellKind.CommandPrompt,
				DisplayName = "Command Prompt",
				Program = Path.Combine(system, "cmd.exe"),
			});
		}

		if (FindGitBash(machine) is { } bash)
		{
			shells.Add(new LocalShell
			{
				Id = LocalShellIds.GitBash,
				Kind = LocalShellKind.GitBash,
				DisplayName = "Git Bash",
				Program = bash,
			});
		}

		// Every Windows has a wsl.exe, installed or not, so the distributions are what says WSL is there.
		string? wsl = system is null ? null : Path.Combine(system, "wsl.exe");
		if (wsl is not null && machine.FileExists(wsl))
		{
			foreach (WslDistribution distribution in machine.WslDistributions())
			{
				shells.Add(new LocalShell
				{
					Id = LocalShellIds.ForDistribution(distribution.Name),
					Kind = LocalShellKind.Wsl,
					DisplayName = $"{distribution.Name} (WSL)",
					Program = wsl,
					Distribution = distribution.Name,
				});
			}
		}

		return shells;
	}

	/// <summary>
	/// The installer's own folder first, then PATH, which also holds the Store's alias for a Store install. Only the
	/// stable line is looked for in Program Files; a preview build is found when it is on PATH.
	/// </summary>
	private static string? FindPowerShell(LocalMachine machine)
	{
		if (machine.Variable("ProgramFiles") is { } programFiles)
		{
			string installed = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
			if (machine.FileExists(installed))
			{
				return installed;
			}
		}

		return LocalProgramPath.FindOnPath("pwsh.exe", machine);
	}

	/// <summary>
	/// <c>bin\bash.exe</c> under Git's folder, which is the one that sets up Git's PATH before starting the real bash.
	/// Git's folder comes from its installer's registry entry, the usual install folders, or a git.exe on PATH.
	/// </summary>
	private static string? FindGitBash(LocalMachine machine)
	{
		List<string> roots = [.. machine.GitInstallFolders()];
		foreach (string variable in new[] { "ProgramFiles", "ProgramFiles(x86)" })
		{
			if (machine.Variable(variable) is { } programFiles)
			{
				roots.Add(Path.Combine(programFiles, "Git"));
			}
		}

		if (machine.Variable("LOCALAPPDATA") is { } localAppData)
		{
			roots.Add(Path.Combine(localAppData, "Programs", "Git"));
		}

		// Git's installer puts its cmd folder on PATH (or its bin folder), one level under the root.
		if (LocalProgramPath.FindOnPath("git.exe", machine) is { } git
			&& Path.GetDirectoryName(git) is { } gitFolder
			&& Path.GetFileName(gitFolder) is { } leaf
			&& (leaf.Equals("cmd", StringComparison.OrdinalIgnoreCase) || leaf.Equals("bin", StringComparison.OrdinalIgnoreCase))
			&& Path.GetDirectoryName(gitFolder) is { } root)
		{
			roots.Add(root);
		}

		return roots
			.Select(root => Path.Combine(root, "bin", "bash.exe"))
			.FirstOrDefault(machine.FileExists);
	}

	private static void AddIfPresent(List<LocalShell> shells, LocalMachine machine, LocalShell shell)
	{
		if (machine.FileExists(shell.Program))
		{
			shells.Add(shell);
		}
	}
}
