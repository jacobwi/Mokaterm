using Mokaterm.Modules.Local.Pty;
using Mokaterm.Modules.Local.Shells;
using Mokaterm.Modules.Local.Tests.Fakes;

namespace Mokaterm.Modules.Local.Tests;

public sealed class LocalLaunchBuilderTests
{
	private const string TerminalType = "xterm-256color";

	private static readonly LocalShell Cmd = new()
	{
		Id = "cmd",
		Kind = LocalShellKind.CommandPrompt,
		DisplayName = "Command Prompt",
		Program = TestMachine.System32("cmd.exe"),
	};

	private static readonly LocalShell Bash = new()
	{
		Id = "git-bash",
		Kind = LocalShellKind.GitBash,
		DisplayName = "Git Bash",
		Program = @"C:\Program Files\Git\bin\bash.exe",
	};

	private static readonly LocalShell Ubuntu = new()
	{
		Id = "wsl:Ubuntu",
		Kind = LocalShellKind.Wsl,
		DisplayName = "Ubuntu (WSL)",
		Program = TestMachine.System32("wsl.exe"),
		Distribution = "Ubuntu",
	};

	[Fact]
	public void TryBuild_AWindowsShell_StartsInTheProfileWithNothingAdded()
	{
		PseudoTerminalLaunch launch = Build(Cmd);

		Assert.Equal(Cmd.Program, launch.Program);
		Assert.Empty(launch.Arguments);
		Assert.Null(launch.ExtraArguments);
		Assert.Equal(TestMachine.Profile, launch.WorkingDirectory);
		Assert.False(launch.Environment.ContainsKey("TERM"));
	}

	[Fact]
	public void TryBuild_LeavesTheHostsWebViewSettingsBehind()
	{
		TestMachine machine = new();
		machine.Variables["WEBVIEW2_USER_DATA_FOLDER"] = @"C:\Users\ada\AppData\Local\Mokaterm\Cache";
		machine.Variables["webview2_additional_browser_arguments"] = "--remote-debugging-port=9222";

		PseudoTerminalLaunch launch = Build(Cmd, machine: machine);

		Assert.DoesNotContain(launch.Environment.Keys, name => name.StartsWith("WEBVIEW2_", StringComparison.OrdinalIgnoreCase));
		Assert.Equal(TestMachine.Profile, launch.Environment["USERPROFILE"]);
	}

	[Fact]
	public void TryBuild_GitBash_IsALoginShellThatKeepsItsStartingFolder()
	{
		PseudoTerminalLaunch launch = Build(Bash, terminalType: "xterm-direct");

		Assert.Equal(["--login", "-i"], launch.Arguments);
		Assert.Equal("xterm-direct", launch.Environment["TERM"]);
		Assert.Equal("1", launch.Environment["CHERE_INVOKING"]);
	}

	[Fact]
	public void TryBuild_Wsl_NamesTheDistributionAndStartsAtHome()
	{
		PseudoTerminalLaunch launch = Build(Ubuntu);

		Assert.Equal(["-d", "Ubuntu", "--cd", "~"], launch.Arguments);

		// wsl.exe's own folder only has to exist; --cd is where the shell starts.
		Assert.Equal(TestMachine.Profile, launch.WorkingDirectory);
		Assert.Equal(TerminalType, launch.Environment["TERM"]);
		Assert.Equal("TERM", launch.Environment["WSLENV"]);
	}

	[Fact]
	public void TryBuild_Wsl_SharesTheLoginsVariablesThroughWslenv_WithoutRepeatingOne()
	{
		TestMachine machine = new();
		machine.Variables["WSLENV"] = "USERPROFILE/up:term";

		PseudoTerminalLaunch launch = Build(
			Ubuntu,
			new LocalConnectionOptions { StartingFolder = "/srv/app", EnvironmentVariables = "EDITOR=vim\nGOFLAGS=-mod=mod" },
			machine: machine);

		Assert.Equal(["-d", "Ubuntu", "--cd", "/srv/app"], launch.Arguments);
		Assert.Equal("USERPROFILE/up:term:EDITOR:GOFLAGS", launch.Environment["WSLENV"]);
		Assert.Equal("-mod=mod", launch.Environment["GOFLAGS"]);
	}

	[Fact]
	public void TryBuild_TheLoginsVariables_SetAndRemove()
	{
		TestMachine machine = new();
		machine.Variables["NOISY"] = "1";

		PseudoTerminalLaunch launch = Build(Cmd, new LocalConnectionOptions { EnvironmentVariables = "NOISY=\nPROMPT=$P$G " }, machine: machine);

		Assert.False(launch.Environment.ContainsKey("NOISY"));
		Assert.Equal("$P$G", launch.Environment["PROMPT"]);
	}

	[Fact]
	public void TryBuild_TypedArguments_AreKeptForTheCommandLine()
	{
		PseudoTerminalLaunch launch = Build(Cmd, new LocalConnectionOptions { Arguments = "  /k \"title x\"  " });

		Assert.Equal("/k \"title x\"", launch.ExtraArguments);
	}

	[Theory]
	[InlineData("~", TestMachine.Profile)]
	[InlineData(@"~\src", @"C:\Users\ada\src")]
	[InlineData("~/src", @"C:\Users\ada\src")]
	[InlineData(@"%USERPROFILE%\src", @"C:\Users\ada\src")]
	[InlineData(@"D:\work", @"D:\work")]
	public void TryBuild_TheStartingFolder_ExpandsTheProfileAndVariables(string folder, string expected)
	{
		TestMachine machine = new();
		machine.Folders.Add(@"C:\Users\ada\src");
		machine.Folders.Add(@"D:\work");

		PseudoTerminalLaunch launch = Build(Cmd, new LocalConnectionOptions { StartingFolder = folder }, machine: machine);

		Assert.Equal(expected, launch.WorkingDirectory);
	}

	[Fact]
	public void TryBuild_TheSettingsFolder_IsUsedWhenTheLoginNamesNone()
	{
		TestMachine machine = new();
		machine.Folders.Add(@"D:\work");

		PseudoTerminalLaunch launch = Build(Cmd, settings: new LocalSettings { StartingFolder = @"D:\work" }, machine: machine);

		Assert.Equal(@"D:\work", launch.WorkingDirectory);
	}

	[Theory]
	[InlineData("src", "The starting folder src is not a full path. Give one from its drive letter on, or start it with ~ for your profile folder.")]
	[InlineData(@"D:\gone", @"The starting folder D:\gone does not exist.")]
	public void TryBuild_AFolderThatCannotBeUsed_SaysWhy(string folder, string expected)
	{
		Assert.False(LocalLaunchBuilder.TryBuild(
			Cmd,
			new LocalConnectionOptions { StartingFolder = folder },
			new LocalSettings(),
			new TestMachine().Build(),
			TerminalType,
			out _,
			out string? problem));

		Assert.Equal(expected, problem);
	}

	[Fact]
	public void TryBuild_VariablesThatDoNotParse_AreRefused()
	{
		Assert.False(LocalLaunchBuilder.TryBuild(
			Cmd,
			new LocalConnectionOptions { EnvironmentVariables = "JUST_A_NAME" },
			new LocalSettings(),
			new TestMachine().Build(),
			TerminalType,
			out _,
			out string? problem));

		Assert.StartsWith("The login's environment variables cannot be used. Line 1", problem, StringComparison.Ordinal);
	}

	[Fact]
	public void Launch_ToString_PrintsNeitherTheTypedTextNorTheEnvironment()
	{
		TestMachine machine = new();
		machine.Variables["API_TOKEN"] = "s3cret-env";

		string printed = Build(Cmd, new LocalConnectionOptions { Arguments = "/k s3cret-arg" }, machine: machine).ToString();

		Assert.DoesNotContain("s3cret", printed, StringComparison.Ordinal);
		Assert.Contains("cmd.exe", printed, StringComparison.Ordinal);
	}

	private static PseudoTerminalLaunch Build(
		LocalShell shell,
		LocalConnectionOptions? options = null,
		LocalSettings? settings = null,
		TestMachine? machine = null,
		string terminalType = TerminalType)
	{
		Assert.True(
			LocalLaunchBuilder.TryBuild(
				shell,
				options ?? LocalConnectionOptions.Default,
				settings ?? new LocalSettings(),
				(machine ?? new TestMachine()).Build(),
				terminalType,
				out PseudoTerminalLaunch? launch,
				out string? problem),
			problem);
		return launch;
	}
}
