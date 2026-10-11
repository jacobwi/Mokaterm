using Mokaterm.Modules.Local.Containers;
using Mokaterm.Modules.Local.Pty;
using Mokaterm.Modules.Local.Pty.Windows;
using Mokaterm.Modules.Local.Tests.Fakes;

namespace Mokaterm.Modules.Local.Tests;

public sealed class DockerExecLaunchTests
{
	private const string Docker = @"C:\Program Files\Docker\Docker\resources\bin\docker.exe";

	private const string TerminalType = "xterm-256color";

	[Fact]
	public void TryBuild_TheAddressAlone_OpensBashOrShInThatContainer()
	{
		PseudoTerminalLaunch launch = Build("shop-api-1", DockerShellOptions.Default, out string container);

		Assert.Equal("shop-api-1", container);
		Assert.Equal(Docker, launch.Program);
		Assert.Equal(
			["exec", "--interactive", "--tty", "--env=TERM=xterm-256color", "shop-api-1", "sh", "-c", ContainerShellCommand.AutoScript],
			launch.Arguments);
		Assert.Null(launch.ExtraArguments);
		Assert.Equal(TestMachine.Profile, launch.WorkingDirectory);
	}

	[Fact]
	public void TryBuild_TheCommandLine_QuotesTheScriptAsOneArgument()
	{
		PseudoTerminalLaunch launch = Build("web", DockerShellOptions.Default, out _);

		string line = WindowsCommandLine.Build(launch.Program, launch.Arguments, launch.ExtraArguments);

		Assert.Equal(
			"\"C:\\Program Files\\Docker\\Docker\\resources\\bin\\docker.exe\" exec --interactive --tty --env=TERM=xterm-256color web sh -c \"command -v bash >/dev/null && exec bash || exec sh\"",
			line);
	}

	[Fact]
	public void TryBuild_TheContainerOption_WinsOverTheAddress()
	{
		Build("build-server", new DockerShellOptions { Container = " db " }, out string container);

		Assert.Equal("db", container);
	}

	[Fact]
	public void TryBuild_EveryOption_InNameEqualsValueForm()
	{
		DockerShellOptions options = new()
		{
			Context = "remote-box",
			User = "www-data:www-data",
			Folder = "/var/www/my site",
			Shell = ContainerShell.Bash,
		};

		PseudoTerminalLaunch launch = Build("web", options, out _);

		Assert.Equal(
			["--context=remote-box", "exec", "--interactive", "--tty", "--user=www-data:www-data", "--workdir=/var/www/my site", "--env=TERM=xterm-256color", "web", "bash"],
			launch.Arguments);
		Assert.Contains("\"--workdir=/var/www/my site\"", WindowsCommandLine.Build(launch.Program, launch.Arguments, null), StringComparison.Ordinal);
	}

	[Fact]
	public void TryBuild_AHost_GoesBeforeExec()
	{
		PseudoTerminalLaunch launch = Build("web", new DockerShellOptions { Host = "ssh://ada@build", Shell = ContainerShell.Sh }, out _);

		Assert.Equal(["--host=ssh://ada@build", "exec", "--interactive", "--tty", "--env=TERM=xterm-256color", "web", "sh"], launch.Arguments);
	}

	[Fact]
	public void TryBuild_ACustomCommand_FollowsTheContainerAsTyped()
	{
		PseudoTerminalLaunch launch = Build("web", new DockerShellOptions { Shell = ContainerShell.Custom, Command = "  python3 -c \"print(1)\"  " }, out _);

		Assert.Equal("web", launch.Arguments[^1]);
		Assert.Equal("python3 -c \"print(1)\"", launch.ExtraArguments);
		Assert.EndsWith("web python3 -c \"print(1)\"", WindowsCommandLine.Build(launch.Program, launch.Arguments, launch.ExtraArguments), StringComparison.Ordinal);
	}

	[Fact]
	public void TryBuild_LeavesTheHostsWebViewSettingsBehind()
	{
		TestMachine machine = new();
		machine.Variables["WEBVIEW2_USER_DATA_FOLDER"] = @"C:\Users\ada\AppData\Local\Mokaterm\Cache";

		PseudoTerminalLaunch launch = Build("web", DockerShellOptions.Default, out _, machine);

		Assert.DoesNotContain(launch.Environment.Keys, name => name.StartsWith("WEBVIEW2_", StringComparison.OrdinalIgnoreCase));
		Assert.Equal(TestMachine.Profile, launch.Environment["USERPROFILE"]);
	}

	[Fact]
	public void TryBuild_TurnsDockersHintsOff_UnlessTheUserChose()
	{
		TestMachine chose = new();
		chose.Variables["docker_cli_hints"] = "true";

		Assert.Equal("false", Build("web", DockerShellOptions.Default, out _).Environment[DockerExecLaunch.HintsVariable]);
		Assert.Equal("true", Build("web", DockerShellOptions.Default, out _, chose).Environment[DockerExecLaunch.HintsVariable]);
	}

	[Theory]
	[InlineData("-rf", null)]
	[InlineData("my container", null)]
	[InlineData("", null)]
	[InlineData("web", "--privileged")]
	public void TryBuild_AContainerDockerWouldNotName_IsRefused(string address, string? container)
	{
		bool built = DockerExecLaunch.TryBuild(
			address,
			new DockerShellOptions { Container = container },
			Docker,
			new TestMachine().Build(),
			TerminalType,
			out PseudoTerminalLaunch? launch,
			out string? target,
			out string? problem);

		Assert.False(built);
		Assert.Null(launch);
		Assert.Null(target);
		Assert.NotNull(problem);
	}

	[Theory]
	[InlineData("Name a Docker context or a Docker host, not both", "ctx", "ssh://a@b", null, null, ContainerShell.Auto, null)]
	[InlineData("A Docker context is named", "my/context", null, null, null, ContainerShell.Auto, null)]
	[InlineData("A Docker host is an address", null, "http://server", null, null, ContainerShell.Auto, null)]
	[InlineData("A user is a name or a uid", null, null, "root user", null, ContainerShell.Auto, null)]
	[InlineData("is not an absolute path", null, null, null, "srv/app", ContainerShell.Auto, null)]
	[InlineData("Name the command", null, null, null, null, ContainerShell.Custom, "   ")]
	public void TryBuild_AnOptionDockerWouldRefuse_IsRefusedFirst(
		string expected,
		string? context,
		string? host,
		string? user,
		string? folder,
		ContainerShell shell,
		string? command)
	{
		DockerShellOptions options = new() { Context = context, Host = host, User = user, Folder = folder, Shell = shell, Command = command };

		bool built = DockerExecLaunch.TryBuild("web", options, Docker, new TestMachine().Build(), TerminalType, out _, out _, out string? problem);

		Assert.False(built);
		Assert.Contains(expected, problem, StringComparison.Ordinal);
	}

	[Fact]
	public void TryBuild_AWindowsContainersFolder_IsAbsoluteToo()
	{
		PseudoTerminalLaunch launch = Build("iis", new DockerShellOptions { Folder = @"C:\inetpub", Shell = ContainerShell.Custom, Command = "cmd" }, out _);

		Assert.Contains(@"--workdir=C:\inetpub", launch.Arguments);
	}

	[Fact]
	public void TryListArguments_AsksTheSameDaemon_ForOneJsonObjectPerLine()
	{
		Assert.True(DockerExecLaunch.TryListArguments(DockerShellOptions.Default, out IReadOnlyList<string>? plain, out _));
		Assert.True(DockerExecLaunch.TryListArguments(new DockerShellOptions { Context = "remote" }, out IReadOnlyList<string>? remote, out _));

		Assert.Equal(["ps", "--format={{json .}}"], plain);
		Assert.Equal(["--context=remote", "ps", "--format={{json .}}"], remote);
		Assert.Equal("docker ps \"--format={{json .}}\"", WindowsCommandLine.Build("docker", plain, null));
	}

	[Fact]
	public void TryListArguments_RefusesWhatTheConnectWould()
	{
		Assert.False(DockerExecLaunch.TryListArguments(new DockerShellOptions { Context = "a", Host = "tcp://b:2376" }, out _, out string? problem));
		Assert.NotNull(problem);
	}

	private static PseudoTerminalLaunch Build(string address, DockerShellOptions options, out string container, TestMachine? machine = null)
	{
		bool built = DockerExecLaunch.TryBuild(
			address,
			options,
			Docker,
			(machine ?? new TestMachine()).Build(),
			TerminalType,
			out PseudoTerminalLaunch? launch,
			out string? target,
			out string? problem);

		Assert.True(built, problem);
		container = target!;
		return launch!;
	}
}
