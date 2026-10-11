using Mokaterm.Modules.Local.Containers;
using Mokaterm.Modules.Local.Pty;
using Mokaterm.Modules.Local.Pty.Windows;
using Mokaterm.Modules.Local.Tests.Fakes;

namespace Mokaterm.Modules.Local.Tests;

public sealed class KubernetesExecLaunchTests
{
	private const string Kubectl = @"C:\Tools\kubectl.exe";

	[Fact]
	public void TryBuild_TheAddressAlone_OpensBashOrShInThatPod()
	{
		PseudoTerminalLaunch launch = Build("web-7d9f8b6c5-x2k4q", KubernetesShellOptions.Default, out string pod);

		Assert.Equal("web-7d9f8b6c5-x2k4q", pod);
		Assert.Equal(Kubectl, launch.Program);
		Assert.Equal(["exec", "--stdin", "--tty", "web-7d9f8b6c5-x2k4q", "--", "sh", "-c", ContainerShellCommand.AutoScript], launch.Arguments);
		Assert.Null(launch.ExtraArguments);
		Assert.Equal(TestMachine.Profile, launch.WorkingDirectory);
	}

	[Fact]
	public void TryBuild_EveryOption_InNameEqualsValueForm()
	{
		KubernetesShellOptions options = new()
		{
			Pod = "api-0",
			Context = "kind kind",
			Namespace = "shop",
			Container = "app",
			Shell = ContainerShell.Sh,
		};

		PseudoTerminalLaunch launch = Build("ignored-address", options, out string pod);

		Assert.Equal("api-0", pod);
		Assert.Equal(["--context=kind kind", "--namespace=shop", "exec", "--stdin", "--tty", "api-0", "--container=app", "--", "sh"], launch.Arguments);
		Assert.Equal(
			"C:\\Tools\\kubectl.exe \"--context=kind kind\" --namespace=shop exec --stdin --tty api-0 --container=app -- sh",
			WindowsCommandLine.Build(launch.Program, launch.Arguments, launch.ExtraArguments));
	}

	[Fact]
	public void TryBuild_ACustomCommand_FollowsTheDoubleDashAsTyped()
	{
		PseudoTerminalLaunch launch = Build("api-0", new KubernetesShellOptions { Shell = ContainerShell.Custom, Command = "/busybox/sh -l" }, out _);

		Assert.Equal("--", launch.Arguments[^1]);
		Assert.Equal("/busybox/sh -l", launch.ExtraArguments);
	}

	[Fact]
	public void TryBuild_ACloudContext_PassesAsItIs()
	{
		PseudoTerminalLaunch launch = Build("api-0", new KubernetesShellOptions { Context = "arn:aws:eks:eu-west-1:123456789012:cluster/prod" }, out _);

		Assert.Equal("--context=arn:aws:eks:eu-west-1:123456789012:cluster/prod", launch.Arguments[0]);
	}

	[Theory]
	[InlineData("Web-1", null, null, null)]
	[InlineData("-it", null, null, null)]
	[InlineData("api-0", "Shop", null, null)]
	[InlineData("api-0", null, "app_main", null)]
	[InlineData("api-0", null, null, "line\nbreak")]
	public void TryBuild_ANameKubernetesWouldNotHave_IsRefused(string pod, string? space, string? container, string? context)
	{
		KubernetesShellOptions options = new() { Namespace = space, Container = container, Context = context };

		bool built = KubernetesExecLaunch.TryBuild(pod, options, Kubectl, new TestMachine().Build(), out PseudoTerminalLaunch? launch, out string? target, out string? problem);

		Assert.False(built);
		Assert.Null(launch);
		Assert.Null(target);
		Assert.NotNull(problem);
	}

	[Fact]
	public void TryBuild_ACustomShellWithoutACommand_IsRefused()
	{
		bool built = KubernetesExecLaunch.TryBuild(
			"api-0",
			new KubernetesShellOptions { Shell = ContainerShell.Custom },
			Kubectl,
			new TestMachine().Build(),
			out _,
			out _,
			out string? problem);

		Assert.False(built);
		Assert.Equal(DockerExecLaunch.NoCommand, problem);
	}

	[Fact]
	public void TryListArguments_AsksTheSameClusterAndNamespace_ForJson()
	{
		Assert.True(KubernetesExecLaunch.TryListArguments(new KubernetesShellOptions { Context = "prod", Namespace = "shop" }, out IReadOnlyList<string>? arguments, out _));

		Assert.Equal(["--context=prod", "--namespace=shop", "get", "pods", "--output=json"], arguments);
	}

	private static PseudoTerminalLaunch Build(string address, KubernetesShellOptions options, out string pod)
	{
		bool built = KubernetesExecLaunch.TryBuild(address, options, Kubectl, new TestMachine().Build(), out PseudoTerminalLaunch? launch, out string? target, out string? problem);

		Assert.True(built, problem);
		pod = target!;
		return launch!;
	}
}
