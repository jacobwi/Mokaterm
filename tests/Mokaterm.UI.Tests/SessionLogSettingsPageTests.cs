using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Settings.Pages.SessionLogs;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

/// <summary>
/// Rendering the page proves it is reachable from its settings page descriptor, that it says plainly what a log file
/// holds, and that saving a copy only shows up on a host that can write one.
/// </summary>
public sealed class SessionLogSettingsPageTests
{
	private static readonly SessionLogFile Written = new()
	{
		Name = "web01-root-20260926-142530.log",
		Length = 2048,
		WrittenAt = new DateTimeOffset(2026, 9, 26, 14, 30, 0, TimeSpan.Zero),
	};

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Render_SaysWhatTheFileHolds_AndListsWhatWasWritten()
	{
		string html = await RenderAsync(new SessionLogSettings { RecordEverySession = true }, withFileAccess: true, Written);

		Assert.Contains("A log holds everything the server printed", html, StringComparison.Ordinal);
		Assert.Contains("Nothing you type is ever recorded", html, StringComparison.Ordinal);
		Assert.Contains("Record every session", html, StringComparison.Ordinal);
		Assert.Contains("Size limit", html, StringComparison.Ordinal);
		Assert.Contains("Plain text", html, StringComparison.Ordinal);
		Assert.Contains(Written.Name, html, StringComparison.Ordinal);
		Assert.Contains("2 KB", html, StringComparison.Ordinal);
		Assert.Contains($"Save a copy of {Written.Name}", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_WithoutALocalFileAccess_OffersNoSave()
	{
		string html = await RenderAsync(new SessionLogSettings(), withFileAccess: false, Written);

		Assert.Contains(Written.Name, html, StringComparison.Ordinal);
		Assert.DoesNotContain("Save a copy of", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_WithNothingWrittenYet_SaysSo()
	{
		string html = await RenderAsync(new SessionLogSettings(), withFileAccess: true);

		Assert.Contains("No session logs yet", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Save a copy of", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(SessionLogSettings settings, bool withFileAccess, params SessionLogFile[] files) =>
		StaticRender.RenderAsync<SessionLogSettingsPage>(
			services =>
			{
				services.AddMokaRed();
				services.AddMokatermUiCommon();
				services.AddSingleton<ISettingsService>(new FixedSettings(settings));
				services.AddSingleton<ISessionLogRecorder>(new FakeRecorder(files));
				services.AddSingleton<IUserInteraction, SilentInteraction>();
				services.AddSingleton(TimeProvider.System);
				if (withFileAccess)
				{
					services.AddSingleton<ILocalFileAccess, NoLocalFiles>();
				}
			},
			new Dictionary<string, object?>(StringComparer.Ordinal))
			.WaitAsync(TimeSpan.FromSeconds(5), Ct);

	private sealed class FakeRecorder(SessionLogFile[] files) : ISessionLogRecorder
	{
		public event Action? Changed
		{
			add { }
			remove { }
		}

		public string Folder => Path.Combine("C:", "logs", "sessions");

		public SessionLogStatus StatusFor(Guid sessionId) => SessionLogStatus.Off;

		public void SetRecording(Guid sessionId, bool record)
		{
		}

		public IReadOnlyList<SessionLogFile> ListFiles() => files;

		public Stream OpenRead(string name) => throw new NotSupportedException();
	}

	/// <summary>Present so the save action shows; a static render can never press it.</summary>
	private sealed class NoLocalFiles : ILocalFileAccess
	{
		public bool CanPickFolders => false;

		public ValueTask<IReadOnlyList<LocalFileItem>> PickFilesAsync(bool multiple = true, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public ValueTask<IReadOnlyList<LocalFileItem>> PickFolderAsync(CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public ValueTask<bool> SaveFileAsync(string suggestedName, long? length, Func<Stream, CancellationToken, Task> writeAsync, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();
	}

	private sealed class SilentInteraction : IUserInteraction
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
