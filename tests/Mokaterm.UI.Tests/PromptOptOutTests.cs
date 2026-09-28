using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Interaction;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

/// <summary>Turning a prompt off writes the setting behind it and says where to turn it back on.</summary>
public sealed class PromptOptOutTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ApplyAsync_WritesTheSetting_AndSaysWhereItComesBack()
	{
		FixedSettings settings = new();
		RecordingNotices notices = new();

		await PromptOptOut.ApplyAsync<GeneralSettings>(
			settings,
			notices,
			NullLogger.Instance,
			section => section with { ConfirmCloseConnectedSessions = false },
			"Connected sessions now close without asking. Settings, General turns it back on.").WaitAsync(TimeSpan.FromSeconds(5), Ct);

		Assert.False(settings.Get<GeneralSettings>().ConfirmCloseConnectedSessions);
		Assert.Equal(["Connected sessions now close without asking. Settings, General turns it back on."], notices.Messages);
	}

	[Fact]
	public async Task ApplyAsync_WhenTheWriteFails_SaysNothingAndThrowsNothing()
	{
		// Whatever the user confirmed has already been decided, so a lost preference must not surface as an error.
		RecordingNotices notices = new();

		await PromptOptOut.ApplyAsync<GeneralSettings>(
			new FailingSettings(),
			notices,
			NullLogger.Instance,
			section => section with { ConfirmCloseConnectedSessions = false },
			"Never shown.").WaitAsync(TimeSpan.FromSeconds(5), Ct);

		Assert.Empty(notices.Messages);
	}

	private sealed class RecordingNotices : IUserInteraction
	{
		private readonly List<string> _messages = [];

		public IReadOnlyList<string> Messages => _messages;

		public Task<HostTrustDecision> ConfirmHostIdentityAsync(HostTrustPrompt prompt, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public Task<CredentialPromptResult?> PromptCredentialsAsync(CredentialPrompt prompt, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public Task<SecretPromptResult?> PromptSecretAsync(SecretPrompt prompt, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public Task<IReadOnlyList<string>?> PromptKeyboardInteractiveAsync(KeyboardInteractivePrompt prompt, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public Task<ConfirmResult> ConfirmAsync(ConfirmPrompt prompt, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public Task<OverwriteDecision> ConfirmOverwriteAsync(OverwritePrompt prompt, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public void Notify(NoticeSeverity severity, string message, string? title = null) => _messages.Add(message);
	}

	private sealed class FailingSettings : ISettingsService
	{
		public event Action<string>? Changed
		{
			add { }
			remove { }
		}

		public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public T Get<T>() where T : class, ISettingsSection, new() => new();

		public Task UpdateAsync<T>(Func<T, T> update, CancellationToken cancellationToken = default)
			where T : class, ISettingsSection, new() => Task.FromException(new IOException("The settings file is in use."));

		public Task ResetAsync<T>(CancellationToken cancellationToken = default)
			where T : class, ISettingsSection, new() => throw new NotSupportedException();

		public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
	}
}
