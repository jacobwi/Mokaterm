using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Local.Containers;

namespace Mokaterm.Modules.Local.Tests;

public sealed class ContainerShellOptionsTests
{
	[Fact]
	public void Docker_RoundTripsEveryField()
	{
		DockerShellOptions options = new()
		{
			Container = "shop-api-1",
			Context = "remote",
			Host = "ssh://ada@build",
			User = "1000:1000",
			Folder = "/srv/app",
			Shell = ContainerShell.Custom,
			Command = "python3 -i",
		};

		ProtocolOptions stored = options.ApplyTo(ProtocolOptions.Empty);

		Assert.Equal(options, DockerShellOptions.From(stored));
		Assert.Equal("shop-api-1", stored.GetString(DockerShellOptions.ContainerKey));
		Assert.Equal("Custom", stored.GetString(DockerShellOptions.ShellKey));
		Assert.All(stored, entry => Assert.StartsWith(DockerShellOptions.KeyPrefix, entry.Key, StringComparison.Ordinal));
	}

	[Fact]
	public void Kubernetes_RoundTripsEveryField()
	{
		KubernetesShellOptions options = new()
		{
			Pod = "web-7d9f8b6c5-x2k4q",
			Context = "arn:aws:eks:eu-west-1:123456789012:cluster/prod",
			Namespace = "shop",
			Container = "app",
			Shell = ContainerShell.Bash,
			Command = "ignored unless custom",
		};

		ProtocolOptions stored = options.ApplyTo(ProtocolOptions.Empty);

		Assert.Equal(options, KubernetesShellOptions.From(stored));
		Assert.Equal("Bash", stored.GetString(KubernetesShellOptions.ShellKey));
		Assert.All(stored, entry => Assert.StartsWith(KubernetesShellOptions.KeyPrefix, entry.Key, StringComparison.Ordinal));
	}

	[Fact]
	public void TheDefaults_StoreNothing()
	{
		Assert.Empty(DockerShellOptions.Default.ApplyTo(ProtocolOptions.Empty));
		Assert.Empty(KubernetesShellOptions.Default.ApplyTo(ProtocolOptions.Empty));
		Assert.Equal(ContainerShell.Auto, DockerShellOptions.From(ProtocolOptions.Empty).Shell);
		Assert.Equal(ContainerShell.Auto, KubernetesShellOptions.From(ProtocolOptions.Empty).Shell);
	}

	[Fact]
	public void Apply_KeepsKeysThatBelongToAnythingElse_AndDropsBlankValues()
	{
		ProtocolOptions others = ProtocolOptions.Empty.With("local.shell", "pwsh").With(DockerShellOptions.UserKey, "root");

		ProtocolOptions stored = new DockerShellOptions { Container = "db", User = "   " }.ApplyTo(others);

		Assert.Equal("pwsh", stored.GetString("local.shell"));
		Assert.Equal("db", stored.GetString(DockerShellOptions.ContainerKey));
		Assert.Null(stored.GetString(DockerShellOptions.UserKey));
	}

	[Fact]
	public void From_AShellNobodyStored_FallsBackToAuto()
	{
		ProtocolOptions stored = ProtocolOptions.Empty.With(DockerShellOptions.ShellKey, "zsh").With(KubernetesShellOptions.ShellKey, "1");

		Assert.Equal(ContainerShell.Auto, DockerShellOptions.From(stored).Shell);
		Assert.Equal(ContainerShell.Auto, KubernetesShellOptions.From(stored).Shell);
	}

	[Fact]
	public void From_KeepsValuesAsTyped()
	{
		ProtocolOptions stored = ProtocolOptions.Empty.With(DockerShellOptions.ContainerKey, " web ");

		Assert.Equal(" web ", DockerShellOptions.From(stored).Container);
	}

	[Fact]
	public void ToString_SaysWhetherACommandOrAHostIsSet_AndNoMore()
	{
		string docker = new DockerShellOptions { Container = "web", Host = "ssh://ada@secret-host", Command = "env TOKEN=abc123 sh" }.ToString();
		string kubernetes = new KubernetesShellOptions { Pod = "web", Command = "env TOKEN=abc123 sh" }.ToString();

		Assert.Contains("web", docker, StringComparison.Ordinal);
		Assert.DoesNotContain("abc123", docker, StringComparison.Ordinal);
		Assert.DoesNotContain("secret-host", docker, StringComparison.Ordinal);
		Assert.DoesNotContain("abc123", kubernetes, StringComparison.Ordinal);
	}
}
