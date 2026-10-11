using System.ComponentModel;
using Mokaterm.Modules.Local.Containers;
using Mokaterm.Modules.Local.Tests.Fakes;

namespace Mokaterm.Modules.Local.Tests;

/// <summary>The editor's "Choose" against a fake docker and kubectl: what is run, and how each way it ends is told.</summary>
public sealed class ContainerListingTests
{
	private const string Tools = @"C:\Tools";

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Docker_RunsPsAgainstTheLoginsDaemon_AndListsWhatItPrinted()
	{
		FakeCommandRunner runner = new(new LocalCommandResult(0, """{"ID":"f27c9f265ce1","Names":"shop-db-1","Image":"postgres:16","State":"running","Status":"Up 2 days"}""", "", false, false));

		ContainerChoiceList list = await ContainerListing.DockerAsync(new DockerShellOptions { Host = "ssh://ada@build" }, Machine(), runner, Ct);

		Assert.Null(list.Problem);
		Assert.Equal("shop-db-1", Assert.Single(list.Choices).Name);
		Assert.Equal(Path.Combine(Tools, "docker.exe"), runner.Program);
		Assert.Equal(["--host=ssh://ada@build", "ps", "--format={{json .}}"], runner.Arguments);
		Assert.Equal(TimeSpan.FromSeconds(10), runner.Timeout);
		Assert.Equal(ContainerListing.OutputLimitBytes, runner.OutputLimitBytes);
	}

	[Fact]
	public async Task Docker_ADaemonThatIsNotRunning_IsToldInDockersOwnWords()
	{
		FakeCommandRunner runner = new(new LocalCommandResult(
			1,
			"",
			"\nerror during connect: Get \"http://%2F%2F.%2Fpipe%2FdockerDesktopLinuxEngine/v1.51/containers/json\": open //./pipe/dockerDesktopLinuxEngine: The system cannot find the file specified.\nmore",
			false,
			false));

		ContainerChoiceList list = await ContainerListing.DockerAsync(DockerShellOptions.Default, Machine(), runner, Ct);

		Assert.StartsWith("error during connect:", list.Problem, StringComparison.Ordinal);
		Assert.DoesNotContain("more", list.Problem, StringComparison.Ordinal);
		Assert.Empty(list.Choices);
	}

	[Fact]
	public async Task Docker_ASilentFailure_SaysTheExitCode()
	{
		FakeCommandRunner runner = new(new LocalCommandResult(125, "", "  ", false, false));

		ContainerChoiceList list = await ContainerListing.DockerAsync(DockerShellOptions.Default, Machine(), runner, Ct);

		Assert.Equal("docker exited with code 125.", list.Problem);
	}

	[Fact]
	public async Task Docker_ThatTakesTooLong_IsReportedAsNotAnswering()
	{
		FakeCommandRunner runner = new(new LocalCommandResult(null, "", "", true, false));

		ContainerChoiceList list = await ContainerListing.DockerAsync(DockerShellOptions.Default, Machine(), runner, Ct);

		Assert.Equal("docker did not answer within 10 seconds.", list.Problem);
	}

	[Fact]
	public async Task Docker_ThatCannotStart_SaysWhy()
	{
		FakeCommandRunner runner = new(new Win32Exception(5, "Access is denied."));

		ContainerChoiceList list = await ContainerListing.DockerAsync(DockerShellOptions.Default, Machine(), runner, Ct);

		Assert.Equal("docker could not be started: Access is denied.", list.Problem);
	}

	[Fact]
	public async Task Docker_NotInstalled_SaysWhatToInstall_WithoutRunningAnything()
	{
		FakeCommandRunner runner = new(new LocalCommandResult(0, "", "", false, false));

		ContainerChoiceList list = await ContainerListing.DockerAsync(DockerShellOptions.Default, new TestMachine().Build(), runner, Ct);

		Assert.Equal(ContainerCli.DockerMissing, list.Problem);
		Assert.Equal(0, runner.Calls);
	}

	[Fact]
	public async Task Docker_OptionsTheConnectWouldRefuse_AreRefusedBeforeRunning()
	{
		FakeCommandRunner runner = new(new LocalCommandResult(0, "", "", false, false));

		ContainerChoiceList list = await ContainerListing.DockerAsync(new DockerShellOptions { Context = "bad/name" }, Machine(), runner, Ct);

		Assert.NotNull(list.Problem);
		Assert.Equal(0, runner.Calls);
	}

	[Fact]
	public async Task Docker_ACutListing_KeepsWhatItHas_AndSaysSo()
	{
		FakeCommandRunner runner = new(new LocalCommandResult(0, """{"ID":"f27c9f265ce1","Names":"web","State":"running"}""", "", false, true));

		ContainerChoiceList list = await ContainerListing.DockerAsync(DockerShellOptions.Default, Machine(), runner, Ct);

		Assert.True(list.IsCut);
		Assert.Single(list.Choices);
	}

	[Fact]
	public async Task Kubernetes_RunsGetPodsInTheLoginsNamespace()
	{
		FakeCommandRunner runner = new(new LocalCommandResult(
			0,
			"""{"items":[{"metadata":{"name":"api-0","namespace":"shop"},"spec":{"containers":[{"name":"api"}]},"status":{"phase":"Running"}}]}""",
			"",
			false,
			false));

		ContainerChoiceList list = await ContainerListing.KubernetesAsync(new KubernetesShellOptions { Namespace = "shop" }, Machine(), runner, Ct);

		Assert.Equal("api-0", Assert.Single(list.Choices).Name);
		Assert.Equal(Path.Combine(Tools, "kubectl.exe"), runner.Program);
		Assert.Equal(["--namespace=shop", "get", "pods", "--output=json"], runner.Arguments);
	}

	[Fact]
	public async Task Kubernetes_ACutListing_IsAProblem_BecauseCutJsonSaysNothing()
	{
		FakeCommandRunner runner = new(new LocalCommandResult(0, "{\"items\":[", "", false, true));

		ContainerChoiceList list = await ContainerListing.KubernetesAsync(KubernetesShellOptions.Default, Machine(), runner, Ct);

		Assert.Contains("Type the pod's name", list.Problem, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Kubernetes_NotInstalled_SaysWhatToInstall()
	{
		FakeCommandRunner runner = new(new LocalCommandResult(0, "", "", false, false));

		ContainerChoiceList list = await ContainerListing.KubernetesAsync(KubernetesShellOptions.Default, new TestMachine().Build(), runner, Ct);

		Assert.Equal(ContainerCli.KubectlMissing, list.Problem);
		Assert.Equal(0, runner.Calls);
	}

	[Theory]
	[InlineData(null, null)]
	[InlineData("", null)]
	[InlineData("\n \n", null)]
	[InlineData("\u001b[31merror:\u001b[0m expired\n", "[31merror:[0m expired")]
	public void FirstLine_TakesTheFirstLineWithText_WithoutControlCharacters(string? text, string? expected) =>
		Assert.Equal(expected, ContainerListing.FirstLine(text));

	[Fact]
	public void FirstLine_PassesOverKubectlsTraceLines_ForItsSentence()
	{
		const string Kubectl = """
			E1011 04:29:01.009226   39456 memcache.go:265] "Unhandled Error" err="couldn't get current server API group list"
			E1011 04:29:01.012331   39456 memcache.go:265] "Unhandled Error" err="couldn't get current server API group list"
			Unable to connect to the server: dial tcp [::1]:8080: connectex: No connection could be made.
			""";

		Assert.Equal("Unable to connect to the server: dial tcp [::1]:8080: connectex: No connection could be made.", ContainerListing.FirstLine(Kubectl));
		Assert.StartsWith("E1011", ContainerListing.FirstLine("E1011 04:29:01.009226   39456 memcache.go:265] only traces"), StringComparison.Ordinal);
	}

	[Fact]
	public void FirstLine_ALongComplaint_IsCut()
	{
		string line = ContainerListing.FirstLine(new string('x', 1000))!;

		Assert.Equal(403, line.Length);
		Assert.EndsWith("...", line, StringComparison.Ordinal);
	}

	private static Shells.LocalMachine Machine()
	{
		TestMachine machine = new();
		machine.Variables["PATH"] = $"{TestMachine.Windows};{Tools}";
		machine.Files.Add(Path.Combine(Tools, "docker.exe"));
		machine.Files.Add(Path.Combine(Tools, "kubectl.exe"));
		return machine.Build();
	}
}
