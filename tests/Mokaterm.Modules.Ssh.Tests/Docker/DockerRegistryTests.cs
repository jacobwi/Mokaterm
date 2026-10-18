using System.Text;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Tests.Fakes;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>
/// Registry login and logout as command lines, the client config read as keys and helpers only, and where a login's
/// credentials are said to be. The <c>auth</c> values in these configs must never come out of anything.
/// </summary>
public sealed class DockerRegistryTests
{
	// base64 of alice:s3cr3t-token, as docker writes it into config.json without a credential helper.
	private const string Auth = "YWxpY2U6czNjcjN0LXRva2Vu";

	private const string Token = "eyJhbGciOiJSUzI1NiJ9.identity-token-value";

	private static readonly string Config = $$"""
		{
			"auths": {
				"ghcr.io": { "auth": "{{Auth}}" },
				"https://index.docker.io/v1/": {},
				"registry.example.com:5000": { "identitytoken": "{{Token}}", "email": "ops@example.com" }
			},
			"credHelpers": { "123456789012.dkr.ecr.eu-west-1.amazonaws.com": "ecr-login", "bad": "has spaces" },
			"credsStore": "pass",
			"detachKeys": "ctrl-e,e",
			"proxies": { "default": { "httpProxy": "http://proxy:3128" } }
		}
		""";

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public void Login_PutsThePasswordOnStdin_NeverOnTheLine()
	{
		using SecretBuffer password = SecretBuffer.FromString("s3cr3t-token");

		DockerCall call = DockerRegistryCommands.Login("ghcr.io", "alice", password);

		Assert.Equal("docker login --username=alice --password-stdin ghcr.io", call.Display);
		Assert.Equal(call.Display, DockerCommandLine.Build(call));
		Assert.Same(password, call.Input);
		Assert.DoesNotContain("s3cr3t", call.ToString(), StringComparison.Ordinal);
		Assert.Contains("Input = (set)", call.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public void Login_WithoutARegistry_IsDockerHub()
	{
		using SecretBuffer password = SecretBuffer.FromString("x");

		Assert.Equal("docker login --username=bob@example.com --password-stdin", DockerRegistryCommands.Login(null, "bob@example.com", password).Display);
		Assert.Equal("docker login --username=bob --password-stdin", DockerRegistryCommands.Login("  ", "bob", password).Display);
	}

	[Theory]
	[InlineData("-ghcr.io", "alice")]
	[InlineData("ghcr.io; rm -rf /", "alice")]
	[InlineData("ghcr.io", "al ice")]
	[InlineData("ghcr.io", "")]
	public void Login_RefusesWhatACommandLineCannotCarry(string registry, string user)
	{
		using SecretBuffer password = SecretBuffer.FromString("x");

		Assert.Throws<ArgumentException>(() => DockerRegistryCommands.Login(registry, user, password));
	}

	[Fact]
	public void Login_RefusesAnEmptyPassword()
	{
		using SecretBuffer empty = SecretBuffer.FromBytes([]);

		Assert.Equal("Type the password or token.", DockerRegistryCommands.PasswordProblem(empty));
		Assert.Throws<ArgumentException>(() => DockerRegistryCommands.Login("ghcr.io", "alice", empty));
	}

	[Fact]
	public void Logout_NamesTheKeyAsTheConfigHasIt()
	{
		Assert.Equal("docker logout https://index.docker.io/v1/", DockerRegistryCommands.Logout(DockerNames.DockerHubRegistry).Display);
		Assert.Equal("docker logout registry.example.com:5000", DockerRegistryCommands.Logout("registry.example.com:5000").Display);
		Assert.Throws<ArgumentException>(() => DockerRegistryCommands.Logout("--all"));
	}

	[Fact]
	public void Login_ThroughSudo_ReadsItsInputAfterTheMarker()
	{
		using SecretBuffer password = SecretBuffer.FromString("x");

		(string script, IReadOnlyList<string> arguments) = DockerCommandLine.ForScript(DockerRegistryCommands.Login("ghcr.io", "alice", password));

		Assert.StartsWith(DockerCommandLine.StdinReader, script, StringComparison.Ordinal);
		Assert.EndsWith(DockerCommandLine.ElevatedScript, script, StringComparison.Ordinal);
		Assert.Equal(["docker", "login", "--username=alice", "--password-stdin", "ghcr.io"], arguments);
		Assert.DoesNotContain('\r', script);
	}

	[Theory]
	[InlineData("ghcr.io")]
	[InlineData("registry.example.com:5000")]
	[InlineData("https://index.docker.io/v1/")]
	[InlineData("http://10.0.0.5:5000")]
	[InlineData("[::1]:5000")]
	[InlineData("localhost:5000")]
	public void RegistryRule_TakesWhatDockerTakes(string registry) => Assert.Null(DockerNames.RegistryProblem(registry));

	[Theory]
	[InlineData("")]
	[InlineData("ghcr.io/$(id)")]
	[InlineData("ghcr io")]
	[InlineData("-x")]
	public void RegistryRule_RefusesTheRest(string registry) => Assert.NotNull(DockerNames.RegistryProblem(registry));

	[Theory]
	[InlineData("https://index.docker.io/v1/", "docker.io")]
	[InlineData("docker.io", "docker.io")]
	[InlineData("registry-1.docker.io", "docker.io")]
	[InlineData("https://GHCR.io", "ghcr.io")]
	[InlineData("registry.example.com:5000/v2/", "registry.example.com:5000")]
	public void Hosts_CompareConfigKeysAndReferencesAlike(string registry, string host) => Assert.Equal(host, DockerRegistryNames.Host(registry));

	[Fact]
	public void ConfigRead_KeepsKeysAndHelpers_AndNeverAnAuthValue()
	{
		DockerOutput output = ConfigOutput("alice", "/home/alice/.docker/config.json", Config);

		DockerClientConfig config = DockerClientConfigReader.Read(output);

		Assert.True(config.Exists);
		Assert.Equal("alice", config.User);
		Assert.Equal("/home/alice/.docker/config.json", config.Path);
		Assert.Equal("pass", config.CredentialsStore);
		Assert.Equal(["ghcr.io", "https://index.docker.io/v1/", "registry.example.com:5000", "123456789012.dkr.ecr.eu-west-1.amazonaws.com"], config.Registries.Select(entry => entry.Key));
		Assert.True(config.Find("ghcr.io")!.StoredInFile);
		Assert.False(config.Find("docker.io")!.StoredInFile);
		Assert.True(config.Find("docker.io")!.LoggedIn);
		Assert.True(config.Find("registry.example.com:5000")!.StoredInFile);
		DockerRegistryEntry ecr = config.Find("123456789012.dkr.ecr.eu-west-1.amazonaws.com")!;
		Assert.Equal("ecr-login", ecr.Helper);
		Assert.False(ecr.LoggedIn);
		Assert.DoesNotContain(config.Registries, entry => entry.Key == "bad" && entry.Helper is not null);

		string everything = Everything(config);
		Assert.DoesNotContain(Auth, everything, StringComparison.Ordinal);
		Assert.DoesNotContain(Token, everything, StringComparison.Ordinal);
		Assert.DoesNotContain("s3cr3t", everything, StringComparison.Ordinal);
	}

	[Fact]
	public void ConfigRead_WipesTheBytesItWasGiven()
	{
		DockerOutput output = ConfigOutput("alice", "/home/alice/.docker/config.json", Config);

		_ = DockerClientConfigReader.Read(output);

		Assert.True(output.RawOutput.Span.IndexOfAnyExcept((byte)0) < 0);
	}

	[Fact]
	public void ConfigRead_ThatIsNotJson_SaysSoWithoutQuotingIt()
	{
		DockerOutput output = ConfigOutput("root", "/root/.docker/config.json", "{ \"auths\": { \"ghcr.io\": { \"auth\": \"" + Auth + "\" } ");

		DockerCommandException failure = Assert.Throws<DockerCommandException>(() => DockerClientConfigReader.Read(output));

		Assert.Equal("/root/.docker/config.json is not valid JSON, so the registries in it cannot be listed.", failure.Message);
		Assert.Null(failure.InnerException);
		Assert.True(output.RawOutput.Span.IndexOfAnyExcept((byte)0) < 0);
	}

	[Fact]
	public void ConfigRead_CutShort_IsRefused()
	{
		DockerOutput output = ConfigOutput("alice", "/home/alice/.docker/config.json", Config) with { OutputTruncated = true };

		Assert.Throws<DockerCommandException>(() => DockerClientConfigReader.Read(output));
	}

	[Fact]
	public void ConfigRead_WithNoFile_IsLoggedInNowhere()
	{
		DockerOutput output = new() { Command = "sh", ExitCode = 0, RawOutput = Encoding.UTF8.GetBytes("alice\n/home/alice/.docker/config.json\n") };

		DockerClientConfig config = DockerClientConfigReader.Read(output);

		Assert.False(config.Exists);
		Assert.Empty(config.Registries);
	}

	[Fact]
	public async Task ConfigScript_BlanksEveryCredentialOnTheServer_BeforeItIsSent()
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		await using TempFolder home = new();
		Directory.CreateDirectory(Path.Combine(home.Path, ".docker"));
		await File.WriteAllTextAsync(Path.Combine(home.Path, ".docker", "config.json"), Config.ReplaceLineEndings("\n"), Ct);
		DockerCall call = DockerRegistryCommands.ReadClientConfig();
		string script = PosixShellLocator.UnixPathPrefix
			+ "HOME=" + Mokaterm.Abstractions.Commands.PosixShell.Quote(home.Path.Replace('\\', '/')) + "\nunset DOCKER_CONFIG\n"
			+ call.Arguments[0];

		(int exit, byte[] bytes, string error) = await PosixShellLocator.RunAsync(shell, script, []).WaitAsync(TimeSpan.FromSeconds(30), Ct);

		Assert.True(exit == 0, error);
		string sent = Encoding.UTF8.GetString(bytes);
		Assert.DoesNotContain(Auth, sent, StringComparison.Ordinal);
		Assert.DoesNotContain(Token, sent, StringComparison.Ordinal);
		Assert.Contains("@@mt:config", sent, StringComparison.Ordinal);
		DockerClientConfig config = DockerClientConfigReader.Read(new DockerOutput { Command = "sh", ExitCode = 0, RawOutput = bytes });
		Assert.True(config.Find("ghcr.io")!.StoredInFile);
		Assert.False(config.Find("docker.io")!.StoredInFile);
		Assert.Equal("ecr-login", config.Find("123456789012.dkr.ecr.eu-west-1.amazonaws.com")!.Helper);
	}

	[Fact]
	public async Task ConfigRead_ThroughTheClient_TakesTheBytesAndNotText()
	{
		FakeDockerShell shell = new();
		DockerCall call = DockerRegistryCommands.ReadClientConfig();
		Assert.True(call.BinaryOutput);
		_ = shell.Answer(DockerCommandLine.Build(call), 0, "alice\n/home/alice/.docker/config.json\n@@mt:config\n" + Config);

		DockerOutput output = await shell.Client().RunAsync(call, Ct);
		DockerClientConfig config = DockerClientConfigReader.Read(output);

		Assert.Equal("", output.Output);
		Assert.Equal(4, config.Registries.Count);
	}

	[Fact]
	public void Wording_SaysWhereTheCredentialsAre()
	{
		DockerClientConfig config = DockerClientConfigReader.Read(ConfigOutput("root", "/root/.docker/config.json", Config));

		Assert.Equal("root's docker", DockerRegistryWording.Whose(config));
		Assert.Equal("Root's docker is logged in to ghcr.io. Stored in /root/.docker/config.json, base64 and not encrypted.", DockerRegistryWording.LoginState(config, "ghcr.io"));
		Assert.Equal("Root's docker is logged in to Docker Hub. Kept by the credential store docker-credential-pass.", DockerRegistryWording.LoginState(config, "docker.io"));
		Assert.StartsWith("Root's docker asks docker-credential-ecr-login", DockerRegistryWording.LoginState(config, "123456789012.dkr.ecr.eu-west-1.amazonaws.com"), StringComparison.Ordinal);
		Assert.StartsWith("Root's docker is not logged in to quay.io", DockerRegistryWording.LoginState(config, "quay.io"), StringComparison.Ordinal);
	}

	[Fact]
	public void StorageWarning_IsWhatDockerSaid()
	{
		DockerOutput output = new()
		{
			Command = "docker login",
			ExitCode = 0,
			Output = "Login Succeeded\n",
			Error = "WARNING! Your credentials are stored unencrypted in '/home/alice/.docker/config.json'.\nConfigure a credential helper to remove this warning.\n",
		};

		Assert.Equal(
			"WARNING! Your credentials are stored unencrypted in '/home/alice/.docker/config.json'.\nConfigure a credential helper to remove this warning.",
			DockerRegistryCommands.StorageWarning(output));
		Assert.Equal("", DockerRegistryCommands.StorageWarning(output with { Error = "" }));
	}

	private static DockerOutput ConfigOutput(string user, string path, string json) => new()
	{
		Command = "sh",
		ExitCode = 0,
		RawOutput = Encoding.UTF8.GetBytes($"{user}\n{path}\n@@mt:config\n{json.ReplaceLineEndings("\n")}"),
	};

	private static string Everything(DockerClientConfig config) => string.Join(
		"\n",
		[
			config.ToString(),
			config.User,
			config.Path,
			config.CredentialsStore ?? "",
			.. config.Registries.SelectMany(entry => (string[])[entry.ToString(), entry.Key, entry.Display, entry.Host, entry.Helper ?? ""]),
		]);
}
