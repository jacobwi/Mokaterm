using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Settings.Pages.Updates;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The page the desktop host registers next to its updater. Rendering it here proves it is reachable from a settings
/// page descriptor, and that it reads sensibly with and without an <see cref="IAppUpdater"/>.
/// </summary>
public sealed class UpdateSettingsPageTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Render_WithNoUpdater_ShowsTheSettingsAndSaysWhyThereIsNothingToDo()
	{
		string html = await RenderAsync(null);

		Assert.Contains("Feed location", html, StringComparison.Ordinal);
		Assert.Contains("Check at startup", html, StringComparison.Ordinal);
		Assert.Contains("no updater", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Check now", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Restart and update", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_WithNoFeed_OffersNoCheck()
	{
		string html = await RenderAsync(new AppUpdateState
		{
			Stage = UpdateStage.NotConfigured,
			Message = "No update feed is set, so Mokaterm never looks for a new version.",
		});

		Assert.Contains("No update feed is set", html, StringComparison.Ordinal);
		Assert.Contains(">Off<", html, StringComparison.Ordinal);
		Assert.Contains("Check now", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Download now", html, StringComparison.Ordinal);
	}

	// A build out of bin has no install to replace, which the page says instead of failing on every attempt.
	[Fact]
	public async Task Render_WhenTheAppIsNotInstalled_SaysSo()
	{
		string html = await RenderAsync(new AppUpdateState
		{
			Stage = UpdateStage.NotInstalled,
			Message = "This copy of Mokaterm was not installed, so it cannot replace its own files.",
		});

		Assert.Contains(">Not installed<", html, StringComparison.Ordinal);
		Assert.Contains("cannot replace its own files", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_WithAnUpdateAvailable_OffersTheDownloadAndNotTheRestart()
	{
		string html = await RenderAsync(new AppUpdateState { Stage = UpdateStage.Available, AvailableVersion = "0.2.0" });

		Assert.Contains(">Update available<", html, StringComparison.Ordinal);
		Assert.Contains("0.2.0", html, StringComparison.Ordinal);
		Assert.Contains("Download now", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Restart and update", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_WhileDownloading_ShowsProgressInsteadOfTheButton()
	{
		string html = await RenderAsync(new AppUpdateState
		{
			Stage = UpdateStage.Downloading,
			AvailableVersion = "0.2.0",
			DownloadPercent = 42,
		});

		Assert.Contains("42%", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Download now", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_WhenTheUpdateIsDownloaded_OffersTheRestart()
	{
		string html = await RenderAsync(new AppUpdateState
		{
			Stage = UpdateStage.ReadyToRestart,
			AvailableVersion = "0.2.0",
			DownloadPercent = 100,
		});

		Assert.Contains(">Ready to restart<", html, StringComparison.Ordinal);
		Assert.Contains("Restart and update", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Download now", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_AfterAFailure_ShowsWhatFailed()
	{
		string html = await RenderAsync(new AppUpdateState
		{
			Stage = UpdateStage.Failed,
			Message = "The update feed could not be read.",
		});

		Assert.Contains(">Failed<", html, StringComparison.Ordinal);
		Assert.Contains("The update feed could not be read.", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(AppUpdateState? state) => StaticRender.RenderAsync<UpdateSettingsPage>(
		services =>
		{
			services.AddMokaRed();
			services.AddMokatermUiCommon();
			services.AddSingleton<IAppEnvironment>(new TestEnvironment());
			services.AddSingleton<ISettingsService, DefaultSettings>();
			services.AddSingleton<IUserInteraction, NoInteraction>();
			if (state is not null)
			{
				services.AddSingleton<IAppUpdater>(new FakeUpdater(state));
			}
		},
		new Dictionary<string, object?>(StringComparer.Ordinal))
		.WaitAsync(TimeSpan.FromSeconds(5), Ct);

	private sealed class TestEnvironment : IAppEnvironment
	{
		public HostKind Kind => HostKind.Desktop;

		public string PlatformName => "Windows";

		public string AppVersion => "0.1.3";

		public string DataDirectory => "";

		public string TempDirectory => "";
	}

	private sealed class DefaultSettings : ISettingsService
	{
		public event Action<string>? Changed
		{
			add { }
			remove { }
		}

		public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public T Get<T>()
			where T : class, ISettingsSection, new() => new();

		public Task UpdateAsync<T>(Func<T, T> update, CancellationToken cancellationToken = default)
			where T : class, ISettingsSection, new() => throw new NotSupportedException();

		public Task ResetAsync<T>(CancellationToken cancellationToken = default)
			where T : class, ISettingsSection, new() => throw new NotSupportedException();

		public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	/// <summary>An updater parked in one state. Nothing in a static render can press a button, so no action is needed.</summary>
	private sealed class FakeUpdater(AppUpdateState state) : IAppUpdater
	{
		public event Action<AppUpdateState>? StateChanged
		{
			add { }
			remove { }
		}

		public AppUpdateState State => state;

		public Task<AppUpdateState> CheckAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<AppUpdateState> DownloadAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<AppUpdateState> ApplyAndRestartAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<AppUpdateState?> CheckAtStartupAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
	}

	private sealed class NoInteraction : IUserInteraction
	{
		public Task<HostTrustDecision> ConfirmHostIdentityAsync(HostTrustPrompt prompt, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public Task<CredentialPromptResult?> PromptCredentialsAsync(CredentialPrompt prompt, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public Task<SecretPromptResult?> PromptSecretAsync(SecretPrompt prompt, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public Task<IReadOnlyList<string>?> PromptKeyboardInteractiveAsync(KeyboardInteractivePrompt prompt, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public Task<ConfirmResult> ConfirmAsync(ConfirmPrompt prompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<OverwriteDecision> ConfirmOverwriteAsync(OverwritePrompt prompt, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public void Notify(NoticeSeverity severity, string message, string? title = null)
		{
		}
	}
}
