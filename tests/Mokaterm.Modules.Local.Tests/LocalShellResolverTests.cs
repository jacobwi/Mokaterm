using Mokaterm.Modules.Local.Shells;
using Mokaterm.Modules.Local.Tests.Fakes;

namespace Mokaterm.Modules.Local.Tests;

public sealed class LocalShellResolverTests
{
	[Theory]
	[InlineData("pwsh", "pwsh")]
	[InlineData("PWSH.exe", "pwsh")]
	[InlineData("powershell", "powershell")]
	[InlineData("cmd", "cmd")]
	[InlineData("cmd.exe", "cmd")]
	[InlineData("git-bash", "git-bash")]
	[InlineData("gitbash", "git-bash")]
	[InlineData("ubuntu", "wsl:Ubuntu")]
	[InlineData("Debian", "wsl:Debian")]
	[InlineData("wsl:debian", "wsl:Debian")]
	[InlineData("wsl", "wsl:Ubuntu")]
	public void TryResolve_TheHostsAddressNamesTheShell(string address, string expected)
	{
		Assert.True(Resolve(address, out LocalShell? shell, out string? problem), problem);

		Assert.Equal(expected, shell.Id);
	}

	[Theory]
	[InlineData("localhost")]
	[InlineData("127.0.0.1")]
	[InlineData("::1")]
	[InlineData("ada-pc")]
	public void TryResolve_AnAddressThatNamesThisComputer_RunsTheDefault(string address)
	{
		Assert.True(Resolve(address, out LocalShell? automatic, out _));
		Assert.Equal("pwsh", automatic.Id);

		Assert.True(Resolve(address, out LocalShell? chosen, out _, settings: new LocalSettings { DefaultShell = "wsl:Debian" }));
		Assert.Equal("wsl:Debian", chosen.Id);
	}

	[Fact]
	public void TryResolve_ADefaultThatIsNotHere_FallsBackToTheFirstWindowsShell()
	{
		Assert.True(Resolve("localhost", out LocalShell? shell, out _, settings: new LocalSettings { DefaultShell = "wsl:Gone" }));

		Assert.Equal("pwsh", shell.Id);
	}

	[Fact]
	public void TryResolve_TheLoginsOwnShell_WinsOverTheAddress()
	{
		Assert.True(Resolve("pwsh", out LocalShell? shell, out _, new LocalConnectionOptions { Shell = "cmd" }));

		Assert.Equal("cmd", shell.Id);
	}

	[Fact]
	public void TryResolve_AnAddressThatNamesNothing_SaysWhatItCouldName()
	{
		Assert.False(Resolve("fish", out _, out string? problem));

		Assert.Contains("No shell on this computer is called fish", problem, StringComparison.Ordinal);
		Assert.Contains("localhost", problem, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("wsl:Arch", "The WSL distribution Arch is not installed on this computer.")]
	[InlineData("git-bash", "Git Bash is not installed on this computer.")]
	public void TryResolve_AShellThatIsNotHere_SaysWhichOne(string chosen, string expected)
	{
		TestMachine machine = Machine();
		machine.Files.Remove(Path.Combine(TestMachine.ProgramFiles, "Git", "bin", "bash.exe"));

		Assert.False(Resolve("localhost", out _, out string? problem, new LocalConnectionOptions { Shell = chosen }, machine: machine));

		Assert.Equal(expected, problem);
	}

	[Fact]
	public void TryResolve_ACustomProgram_IsFoundOnPathWithItsExtension()
	{
		TestMachine machine = Machine();
		machine.Variables["PATH"] = @"C:\Windows\System32;D:\tools";
		machine.Files.Add(@"D:\tools\nu.exe");

		Assert.True(Resolve("localhost", out LocalShell? shell, out _, new LocalConnectionOptions { Shell = "custom", Program = "\"nu\"" }, machine: machine));

		Assert.Equal(LocalShellKind.Custom, shell.Kind);
		Assert.Equal(@"D:\tools\nu.exe", shell.Program);
		Assert.Equal("nu.exe", shell.DisplayName);
	}

	[Fact]
	public void TryResolve_ACustomProgram_ExpandsVariablesInAFullPath()
	{
		TestMachine machine = Machine();
		machine.Files.Add(@"C:\Users\ada\bin\tool.exe");

		Assert.True(Resolve("x", out LocalShell? shell, out _, new LocalConnectionOptions { Shell = "custom", Program = @"%USERPROFILE%\bin\tool.exe" }, machine: machine));

		Assert.Equal(@"C:\Users\ada\bin\tool.exe", shell.Program);
	}

	[Theory]
	[InlineData(null, "Name the program this login runs in its options.")]
	[InlineData("missing.exe", "missing.exe was not found. Give its full path, or the name of a program on PATH.")]
	[InlineData(@"tools\nu.exe", @"tools\nu.exe was not found. Give its full path, or the name of a program on PATH.")]
	[InlineData("build.cmd", "build.cmd is a script, which needs a shell to run it. Pick the Command Prompt or PowerShell and give the script in the arguments.")]
	public void TryResolve_ACustomProgramThatCannotRun_SaysWhy(string? program, string expected)
	{
		TestMachine machine = Machine();
		machine.Files.Add(@"C:\Windows\System32\build.cmd");

		Assert.False(Resolve("localhost", out _, out string? problem, new LocalConnectionOptions { Shell = "custom", Program = program }, machine: machine));

		Assert.Equal(expected, problem);
	}

	[Fact]
	public void TryResolve_OnAComputerWithNoShell_SaysSo()
	{
		TestMachine empty = new();
		empty.Variables.Clear();

		Assert.False(Resolve("localhost", out _, out string? problem, machine: empty));

		Assert.Equal("No shell was found on this computer.", problem);
	}

	private static TestMachine Machine()
	{
		TestMachine machine = TestMachine.WithWindowsShells();
		machine.Files.Add(Path.Combine(TestMachine.ProgramFiles, "PowerShell", "7", "pwsh.exe"));
		machine.Files.Add(Path.Combine(TestMachine.ProgramFiles, "Git", "bin", "bash.exe"));
		machine.Distributions.Add(new WslDistribution("Ubuntu", IsDefault: true));
		machine.Distributions.Add(new WslDistribution("Debian", IsDefault: false));
		return machine;
	}

	private static bool Resolve(
		string address,
		[System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out LocalShell? shell,
		[System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? problem,
		LocalConnectionOptions? options = null,
		LocalSettings? settings = null,
		TestMachine? machine = null)
	{
		LocalMachine built = (machine ?? Machine()).Build();
		return LocalShellResolver.TryResolve(
			options ?? LocalConnectionOptions.Default,
			address,
			settings ?? new LocalSettings(),
			LocalShellDetector.Detect(built),
			built,
			out shell,
			out problem);
	}
}
