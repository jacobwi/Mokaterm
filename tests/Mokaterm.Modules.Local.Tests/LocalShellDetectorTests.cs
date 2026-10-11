using Mokaterm.Modules.Local.Shells;
using Mokaterm.Modules.Local.Tests.Fakes;

namespace Mokaterm.Modules.Local.Tests;

public sealed class LocalShellDetectorTests
{
	private static readonly string InstalledPwsh = Path.Combine(TestMachine.ProgramFiles, "PowerShell", "7", "pwsh.exe");

	[Fact]
	public void Detect_FindsEveryKindInOrder()
	{
		TestMachine machine = TestMachine.WithWindowsShells();
		machine.Files.Add(InstalledPwsh);
		machine.Files.Add(Path.Combine(TestMachine.ProgramFiles, "Git", "bin", "bash.exe"));
		machine.Distributions.Add(new WslDistribution("Ubuntu", IsDefault: true));
		machine.Distributions.Add(new WslDistribution("Debian", IsDefault: false));

		IReadOnlyList<LocalShell> shells = LocalShellDetector.Detect(machine.Build());

		Assert.Equal(["pwsh", "powershell", "cmd", "git-bash", "wsl:Ubuntu", "wsl:Debian"], shells.Select(shell => shell.Id));
		Assert.Equal(InstalledPwsh, shells[0].Program);
		Assert.Equal("Ubuntu (WSL)", shells[4].DisplayName);
		Assert.Equal("Ubuntu", shells[4].Distribution);
		Assert.Equal("Ubuntu", shells[4].Address);
		Assert.Equal("cmd", shells[2].Address);
		Assert.All(shells.Where(shell => shell.Kind == LocalShellKind.Wsl), shell => Assert.Equal(TestMachine.System32("wsl.exe"), shell.Program));
	}

	[Fact]
	public void Detect_FindsPowerShellOnPath_WhenItIsNotInProgramFiles()
	{
		TestMachine machine = TestMachine.WithWindowsShells();
		string store = @"C:\Users\ada\AppData\Local\Microsoft\WindowsApps";
		machine.Variables["PATH"] = $@"relative\bin;{store};C:\Windows\System32";
		machine.Files.Add(Path.Combine(store, "pwsh.exe"));

		// A relative PATH entry would be looked up in the app's own folder, so it is never searched.
		machine.Files.Add(Path.Combine("relative", "bin", "pwsh.exe"));

		LocalShell pwsh = LocalShellDetector.Detect(machine.Build())[0];

		Assert.Equal(LocalShellKind.PowerShell, pwsh.Kind);
		Assert.Equal(Path.Combine(store, "pwsh.exe"), pwsh.Program);
	}

	[Fact]
	public void Detect_FindsGitBashWhereItsInstallerSaysItIs()
	{
		TestMachine machine = TestMachine.WithWindowsShells();
		machine.GitFolders.Add(@"D:\Apps\Git");
		machine.Files.Add(Path.Combine(@"D:\Apps\Git", "bin", "bash.exe"));

		LocalShell bash = Assert.Single(LocalShellDetector.Detect(machine.Build()), shell => shell.Kind == LocalShellKind.GitBash);

		Assert.Equal(@"D:\Apps\Git\bin\bash.exe", bash.Program);
	}

	[Fact]
	public void Detect_FindsGitBashFromTheGitOnPath()
	{
		TestMachine machine = TestMachine.WithWindowsShells();
		machine.Variables["PATH"] = @"C:\Windows\System32;E:\PortableGit\cmd";
		machine.Files.Add(@"E:\PortableGit\cmd\git.exe");
		machine.Files.Add(@"E:\PortableGit\bin\bash.exe");

		LocalShell bash = Assert.Single(LocalShellDetector.Detect(machine.Build()), shell => shell.Kind == LocalShellKind.GitBash);

		Assert.Equal(@"E:\PortableGit\bin\bash.exe", bash.Program);
	}

	[Fact]
	public void Detect_OffersNoWsl_WithoutDistributions_OrWithoutWslExe()
	{
		TestMachine noDistributions = TestMachine.WithWindowsShells();
		Assert.DoesNotContain(LocalShellDetector.Detect(noDistributions.Build()), shell => shell.Kind == LocalShellKind.Wsl);

		TestMachine noWslExe = new();
		noWslExe.Distributions.Add(new WslDistribution("Ubuntu", IsDefault: true));
		Assert.Empty(LocalShellDetector.Detect(noWslExe.Build()));
	}

	[Fact]
	public void Detect_OnAMachineWithoutWindows_FindsNothing()
	{
		TestMachine machine = new();
		machine.Variables.Clear();

		Assert.Empty(LocalShellDetector.Detect(machine.Build()));
	}
}
