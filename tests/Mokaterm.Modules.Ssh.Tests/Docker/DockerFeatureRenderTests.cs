using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Modules.Ssh.Components.Docker;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Tests.Fakes;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Common.Files;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

// The limits tab and the volume backup and restore view, drawn over a docker answered from a table.
public sealed class DockerFeatureRenderTests
{
	private const string Limited = """
		[{
		  "Id": "f27c9f265ce1aa", "Name": "/shop-db-1",
		  "State": { "Status": "running" },
		  "HostConfig": { "NanoCpus": 1500000000, "Memory": 536870912, "MemorySwap": -1, "RestartPolicy": { "Name": "unless-stopped" } },
		  "Config": { "Image": "postgres:16", "Labels": { "com.docker.compose.project": "shop", "com.docker.compose.service": "db" } }
		}]
		""";

	private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.Zero));
	private readonly FakeDockerShell _shell = new();
	private readonly ScriptedSessionHandle _session;

	public DockerFeatureRenderTests()
	{
		DockerSessionFeature feature = new(_shell, authenticate: null, _time, "build-server", "ada", files: null);
		_session = new ScriptedSessionHandle(new FeatureSession(feature));
		_shell
			.Answer(Line(DockerCommands.Inspect("f27c9f265ce1aa")), 0, Limited)
			.Answer(Line(DockerCommands.ListImages()), 0, DockerFixtures.Images)
			.Answer(Line(DockerCommands.InspectVolume("old-cache")), 0, """[{"Name":"old-cache","Driver":"local","Mountpoint":"/var/lib/docker/volumes/old-cache/_data"}]""")
			.Answer(Line(DockerCommands.ContainersUsingVolume("old-cache")), 0, "");
	}

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task TheLimitsTab_ShowsWhatTheContainerHas_AndThatComposePutsItsOwnBack()
	{
		string html = await RenderAsync<DockerLimitsPanel>(new() { [nameof(DockerLimitsPanel.ContainerId)] = "f27c9f265ce1aa" });

		Assert.Contains(">1.5<", html, StringComparison.Ordinal);
		Assert.Contains(">512m<", html, StringComparison.Ordinal);
		Assert.Contains("-1, no swap limit", html, StringComparison.Ordinal);
		Assert.Contains(">unless-stopped<", html, StringComparison.Ordinal);
		Assert.Contains("belongs to the compose project shop", html, StringComparison.Ordinal);
		Assert.Contains("value=\"1.5\"", html, StringComparison.Ordinal);
		Assert.Contains("value=\"512m\"", html, StringComparison.Ordinal);
		Assert.Contains("value=\"-1\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheBackupForm_StartsInTheStagingFolder_WithTheSmallestImageAsTheHelper()
	{
		string html = await RenderAsync<DockerVolumeArchiveView>(new()
		{
			[nameof(DockerVolumeArchiveView.Mode)] = DockerVolumeArchiveView.BackupMode,
			[nameof(DockerVolumeArchiveView.Volume)] = "old-cache",
		});

		Assert.Contains("value=\"/tmp\"", html, StringComparison.Ordinal);
		Assert.Contains("value=\"old-cache-", html, StringComparison.Ordinal);
		Assert.Contains(".tar.gz\"", html, StringComparison.Ordinal);
		Assert.Contains("made from nginx:1.27-alpine (48.3MB)", html, StringComparison.Ordinal);
		Assert.DoesNotContain("may write to", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheRestoreForm_WithoutAVolume_AsksForANewOne()
	{
		string html = await RenderAsync<DockerVolumeArchiveView>(new() { [nameof(DockerVolumeArchiveView.Mode)] = DockerVolumeArchiveView.RestoreMode });

		Assert.Contains(">New volume<", html, StringComparison.Ordinal);
		Assert.Contains("Archive on the docker host", html, StringComparison.Ordinal);
		Assert.DoesNotContain("replace", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task WithNoImageOnTheHost_TheFormSaysSo()
	{
		_shell.Answer(Line(DockerCommands.ListImages()), 0, "");

		string html = await RenderAsync<DockerVolumeArchiveView>(new()
		{
			[nameof(DockerVolumeArchiveView.Mode)] = DockerVolumeArchiveView.BackupMode,
			[nameof(DockerVolumeArchiveView.Volume)] = "old-cache",
		});

		Assert.Contains("This host has no image to make the helper container from.", html, StringComparison.Ordinal);
		Assert.Contains(">Pull busybox<", html, StringComparison.Ordinal);
	}

	private static string Line(DockerCall call) => DockerCommandLine.Build(call);

	private Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters)
		where TComponent : IComponent =>
		StaticRender.RenderAsync<TComponent>(
			services =>
			{
				services.AddSingleton<TimeProvider>(_time);
				services.AddMokaRed();
				services.AddMokatermUiCommon();
				services.AddSingleton<IUserInteraction, DismissingInteraction>();
				services.AddSingleton<ISettingsService>(new FakeSettingsService());
				services.AddSingleton<IAppEnvironment>(new FakeAppEnvironment(Path.Combine(Path.GetTempPath(), "mokaterm-unused")));
				services.AddSingleton(UnusedService.Of<ISessionManager>());
				services.AddSingleton(UnusedService.Of<ISessionTabs>());
				services.AddSingleton(UnusedService.Of<IFileBrowserDialog>());
			},
			new Dictionary<string, object?>(parameters) { [nameof(DockerLimitsPanel.Session)] = _session })
			.WaitAsync(TimeSpan.FromSeconds(10), Ct);
}
