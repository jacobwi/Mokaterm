using Microsoft.Extensions.Time.Testing;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Tests.Fakes;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

// Compose drift: the files' hashes from config --hash against the config-hash label on each container. Changed is
// claimed only where both are known and differ; a failed read, an unset variable, a missing label or another
// compose's hash all leave a service unknown.
public sealed class ComposeDriftTests
{
	private const string WebHash = "1111111111111111111111111111111111111111111111111111111111111111";
	private const string ApiHash = "2222222222222222222222222222222222222222222222222222222222222222";
	private const string OldHash = "9999999999999999999999999999999999999999999999999999999999999999";

	private static readonly ComposeTool Plugin = new(DockerProgram.ComposePlugin, "2.29.7");

	private static readonly ComposeTarget Shop = new(Plugin, "shop", "/srv/shop", ["/srv/shop/compose.yaml"], []);

	private static readonly DateTimeOffset ReadAt = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public void TheHashCommand_AsksForEveryService_Quoted()
	{
		Assert.Equal("cd -- /srv/shop && docker compose -p shop -f /srv/shop/compose.yaml config --hash '*'", ComposeCommands.ConfigHashes(Shop).Display);
		Assert.Equal("cd -- /srv/shop && docker compose -p shop -f /srv/shop/compose.yaml config --no-interpolate --format json", ComposeCommands.ConfigUninterpolated(Shop).Display);
	}

	[Fact]
	public void HashLines_AreReadAsServiceAndHash_AndNothingElse()
	{
		IReadOnlyDictionary<string, string> hashes = ComposeHashes.ParseLines($"web {WebHash}\r\napi {ApiHash}\nWARN[0000] something\nshort deadbeef\n\n");

		Assert.Equal(2, hashes.Count);
		Assert.Equal(WebHash, hashes["web"]);
		Assert.Equal(ApiHash, hashes["api"]);
	}

	[Fact]
	public void AServiceWhoseLabelDiffers_IsChanged_AndOneThatMatches_IsCurrent()
	{
		ComposeDrift drift = Compare(Hashes(), [Container("shop-web-1", "web", OldHash), Container("shop-api-1", "api", ApiHash)])!;

		Assert.Equal(ComposeDriftState.Changed, drift.State);
		Assert.Equal(ComposeDriftState.Changed, drift.Find("web")!.State);
		Assert.Equal(ComposeDriftState.Current, drift.Find("api")!.State);
		Assert.Equal("The compose files no longer define web the way it was brought up. Up makes it again.", drift.ChangedText());
	}

	[Fact]
	public void EveryLabelMatching_IsCurrent()
	{
		ComposeDrift drift = Compare(Hashes(), [Container("shop-web-1", "web", WebHash), Container("shop-api-1", "api", ApiHash)])!;

		Assert.Equal(ComposeDriftState.Current, drift.State);
		Assert.Null(drift.UnknownText());
	}

	[Fact]
	public void OneChangedReplica_MakesTheServiceChanged()
	{
		ComposeDrift drift = Compare(Hashes(), [Container("shop-web-1", "web", WebHash), Container("shop-web-2", "web", OldHash)])!;

		Assert.Equal(ComposeDriftState.Changed, drift.Find("web")!.State);
		Assert.StartsWith("shop-web-2 was made", drift.Find("web")!.Detail, StringComparison.Ordinal);
	}

	[Fact]
	public void AFailedRead_LeavesEveryServiceUnknown_WithComposesReason()
	{
		ComposeHashes failed = ComposeHashes.Failed("docker compose config --hash said: no configuration file provided: not found", ReadAt, Plugin.Display);

		ComposeDrift drift = Compare(failed, [Container("shop-web-1", "web", OldHash)])!;

		Assert.Equal(ComposeDriftState.Unknown, drift.State);
		Assert.Equal("Whether the files changed since the last up cannot be told. docker compose config --hash said: no configuration file provided: not found", drift.UnknownText());
	}

	[Fact]
	public void AServiceOnAnUnsetVariable_IsUnknown_AndTheRestStillCompare()
	{
		ComposeHashes hashes = Hashes() with
		{
			Uncertain = new Dictionary<string, string> { ["api"] = ComposeHashes.Uses("api", ["SMTP_PASSWORD"]) },
		};

		ComposeDrift drift = Compare(hashes, [Container("shop-web-1", "web", WebHash), Container("shop-api-1", "api", OldHash)])!;

		Assert.Equal(ComposeDriftState.Unknown, drift.State);
		Assert.Equal(ComposeDriftState.Unknown, drift.Find("api")!.State);
		Assert.Equal(
			"web runs what the files define. Whether api changed cannot be told: api uses SMTP_PASSWORD, which is not set where the manager runs compose, so its hash may differ from the one up worked out.",
			drift.UnknownText());
	}

	[Fact]
	public void NoLabel_OrAnotherMajorComposesHash_IsUnknown()
	{
		ComposeDrift drift = Compare(Hashes(), [Container("shop-web-1", "web", null), Container("shop-api-1", "api", OldHash, "1.29.2")])!;

		Assert.Equal(ComposeDriftState.Unknown, drift.Find("web")!.State);
		Assert.Equal("shop-api-1 was made by compose 1.29.2, which works its hash out differently from 2.29.7.", drift.Find("api")!.Detail);
		Assert.Equal(ComposeDriftState.Unknown, drift.State);
	}

	[Fact]
	public void AServiceTheFilesNoLongerHave_IsUnknown_NeverChanged()
	{
		ComposeDrift drift = Compare(Hashes(), [Container("shop-worker-1", "worker", OldHash)])!;

		Assert.Equal(ComposeDriftState.Unknown, drift.Find("worker")!.State);
	}

	[Fact]
	public void HashesFromAnotherCompose_OrNone_AreNotCompared()
	{
		Assert.Null(Compare(null, [Container("shop-web-1", "web", OldHash)]));
		Assert.Null(ComposeDrift.Compare(Hashes() with { Tool = "docker-compose 2.20.2" }, [Container("shop-web-1", "web", OldHash)], Plugin));
	}

	[Fact]
	public void OneOffContainers_AreLeftOut()
	{
		DockerContainer oneOff = Container("shop-web-run-1a2b", "web", OldHash, extra: ",com.docker.compose.oneoff=True");

		ComposeDrift drift = Compare(Hashes(), [Container("shop-web-1", "web", WebHash), oneOff])!;

		Assert.Equal(ComposeDriftState.Current, drift.State);
	}

	[Fact]
	public void TheServicesThatNameAVariable_AreFoundInTheUninterpolatedJson()
	{
		const string Json = """
			{"name":"shop","services":{
			  "web":{"image":"nginx:1.27-alpine","environment":{"PRICE":"$$5"}},
			  "api":{"image":"api","environment":{"SMTP_PASSWORD":"${SMTP_PASSWORD}","OTHER":"$SMTP_PASSWORD_FILE"}},
			  "db":{"image":"postgres","environment":{"POSTGRES_PASSWORD":"${DB_PASSWORD:-x}"}},
			  "mail":{"image":"mail","command":"send $SMTP_PASSWORD now"}
			}}
			""";

		IReadOnlyDictionary<string, string> found = ComposeHashes.ServicesUsing(Json, ["SMTP_PASSWORD", "DB_PASSWORD"]);

		Assert.Equal(["api", "db", "mail"], found.Keys.Order(StringComparer.Ordinal));
		Assert.StartsWith("api uses SMTP_PASSWORD,", found["api"], StringComparison.Ordinal);
		Assert.True(ComposeHashes.HasServices(Json));
		Assert.False(ComposeHashes.HasServices("not json"));
	}

	[Fact]
	public void Compose_ItsWarningsBeforeTheError_AreSkipped()
	{
		string said = DockerComposeDrift.Complaint("time=\"2026\" level=warning msg=\"The \\\"X\\\" variable is not set.\"\nWARN[0000] obsolete version\nenv file /srv/shop/api.env not found: stat /srv/shop/api.env: no such file or directory\n");

		Assert.Equal("env file /srv/shop/api.env not found: stat /srv/shop/api.env: no such file or directory", said);
	}

	[Fact]
	public async Task AReadThatFails_IsKeptAsTheReason_AndARefusedOption_TooAsync()
	{
		FakeDockerShell shell = new FakeDockerShell()
			.Answer(ComposeCommands.ConfigHashes(Shop).Display, 1, error: "unknown flag: --hash\n");
		DockerComposeDrift drift = new(new FakeTimeProvider(ReadAt));

		ComposeHashes? hashes = await drift.ReadAsync(shell.Client(), Shop, Ct);

		Assert.Equal("docker compose config --hash said: unknown flag: --hash", hashes!.Problem);
		Assert.Same(hashes, drift.Find("shop"));
		Assert.False(drift.IsStale("shop", Plugin, DockerComposeDrift.ProjectAge));
	}

	[Fact]
	public async Task AnUnsetVariable_AsksWhichServicesNameIt()
	{
		FakeDockerShell shell = new FakeDockerShell()
			.Answer(ComposeCommands.ConfigHashes(Shop).Display, 0, $"api {ApiHash}\nweb {WebHash}\n", "time=\"2026-10-11T07:00:00Z\" level=warning msg=\"The \\\"SMTP_PASSWORD\\\" variable is not set. Defaulting to a blank string.\"\n")
			.Answer(ComposeCommands.ConfigUninterpolated(Shop).Display, 0, """{"services":{"api":{"environment":{"P":"${SMTP_PASSWORD}"}},"web":{"image":"nginx"}}}""");
		FakeTimeProvider time = new(ReadAt);
		DockerComposeDrift drift = new(time);

		ComposeHashes hashes = (await drift.ReadAsync(shell.Client(), Shop, Ct))!;
		time.Advance(DockerComposeDrift.ProjectAge);

		Assert.Null(hashes.Problem);
		Assert.Equal(2, hashes.Services.Count);
		Assert.Equal(["api"], hashes.Uncertain.Keys);
		Assert.True(drift.IsStale("shop", Plugin, DockerComposeDrift.ProjectAge));
		Assert.False(drift.IsStale("shop", Plugin, DockerComposeDrift.ListAge));
		drift.Forget("shop");
		Assert.Null(drift.Find("shop"));
	}

	[Fact]
	public async Task WhenTheJsonCannotBeRead_EveryServiceStaysInDoubt()
	{
		FakeDockerShell shell = new FakeDockerShell()
			.Answer(ComposeCommands.ConfigHashes(Shop).Display, 0, $"api {ApiHash}\nweb {WebHash}\n", "WARN[0000] The \"SMTP_PASSWORD\" variable is not set. Defaulting to a blank string.\n")
			.Answer(ComposeCommands.ConfigUninterpolated(Shop).Display, 1, error: "unknown flag: --no-interpolate\n");
		DockerComposeDrift drift = new(new FakeTimeProvider(ReadAt));

		ComposeHashes hashes = (await drift.ReadAsync(shell.Client(), Shop, Ct))!;

		Assert.Equal(["api", "web"], hashes.Uncertain.Keys.Order(StringComparer.Ordinal));
	}

	private static ComposeDrift? Compare(ComposeHashes? hashes, IReadOnlyList<DockerContainer> containers) =>
		ComposeDrift.Compare(hashes, containers, Plugin);

	private static ComposeHashes Hashes() => new()
	{
		Services = new Dictionary<string, string> { ["web"] = WebHash, ["api"] = ApiHash },
		ReadAt = ReadAt,
		Tool = Plugin.Display,
	};

	private static DockerContainer Container(string name, string service, string? hash, string version = "2.29.7", string extra = "")
	{
		string labels = $"com.docker.compose.project=shop,com.docker.compose.service={service},com.docker.compose.version={version}"
			+ (hash is null ? "" : $",com.docker.compose.config-hash={hash}") + extra;
		string row = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
		{
			["ID"] = name + "000000000000",
			["Names"] = name,
			["Image"] = "nginx",
			["Labels"] = labels,
			["State"] = "running",
			["Status"] = "Up 2 days",
		});
		return Assert.Single(DockerContainer.Parse(row));
	}
}
