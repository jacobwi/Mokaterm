using Mokaterm.Modules.Local.Shells;

namespace Mokaterm.Modules.Local.Tests.Fakes;

/// <summary>
/// A computer described by the test: the files it has, its folders, its environment and what its registry lists. The
/// paths look like Windows ones but are only ever compared as text, so the tests mean the same on any machine.
/// </summary>
internal sealed class TestMachine
{
	public const string Windows = @"C:\Windows";

	public const string ProgramFiles = @"C:\Program Files";

	public const string Profile = @"C:\Users\ada";

	public const string Name = "ADA-PC";

	public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

	public HashSet<string> Folders { get; } = new(StringComparer.OrdinalIgnoreCase) { Profile };

	public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase)
	{
		["SystemRoot"] = Windows,
		["ProgramFiles"] = ProgramFiles,
		["USERPROFILE"] = Profile,
		["PATH"] = @"C:\Windows\System32;C:\Windows",
	};

	public List<string> GitFolders { get; } = [];

	public List<WslDistribution> Distributions { get; } = [];

	public static string System32(string file) => Path.Combine(Windows, "System32", file);

	/// <summary>Every Windows has these two and a wsl.exe, installed or not.</summary>
	public static TestMachine WithWindowsShells()
	{
		TestMachine machine = new();
		machine.Files.Add(System32(Path.Combine("WindowsPowerShell", "v1.0", "powershell.exe")));
		machine.Files.Add(System32("cmd.exe"));
		machine.Files.Add(System32("wsl.exe"));
		return machine;
	}

	public LocalMachine Build() => new()
	{
		Variables = Variables,
		UserProfile = Profile,
		MachineName = Name,
		FileExists = Files.Contains,
		DirectoryExists = Folders.Contains,
		GitInstallFolders = () => GitFolders,
		WslDistributions = () => Distributions,
	};
}
