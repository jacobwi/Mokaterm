namespace Mokaterm.Modules.Local.Shells;

/// <summary>What a shell is, which decides the arguments and the environment it starts with.</summary>
internal enum LocalShellKind
{
	/// <summary>PowerShell 7 and later, <c>pwsh.exe</c>.</summary>
	PowerShell,

	/// <summary>Windows PowerShell 5.1, the <c>powershell.exe</c> every Windows ships.</summary>
	WindowsPowerShell,

	CommandPrompt,

	/// <summary>Git for Windows' bash, started as a login shell.</summary>
	GitBash,

	/// <summary>A WSL distribution, through <c>wsl.exe</c>.</summary>
	Wsl,

	/// <summary>A program the login names itself.</summary>
	Custom,
}
