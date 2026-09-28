using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

/// <summary>
/// Closing a connected tab asks first, and the question can be turned off from the prompt itself, which is the one
/// place the user meets it often enough to want that.
/// </summary>
public sealed class SessionCloseTests
{
	[Fact]
	public async Task ClosingAConnectedSession_AsksAndOffersToStopAsking()
	{
		using Harness harness = Harness.Create(new ConfirmResult(true, false));
		FakeSession session = harness.Manager.Add();

		await harness.Actions.CloseAsync([session.Id]);

		ConfirmPrompt prompt = Assert.Single(harness.Interaction.Prompts);
		Assert.Equal("Close session", prompt.Title);
		Assert.Equal("Don't ask again", prompt.RememberText);
		Assert.Empty(harness.Manager.Sessions);
	}

	[Fact]
	public async Task StopAsking_TurnsTheSettingOff_AndSaysWhereToTurnItBackOn()
	{
		using Harness harness = Harness.Create(new ConfirmResult(true, true));
		FakeSession session = harness.Manager.Add();

		await harness.Actions.CloseAsync([session.Id]);

		Assert.False(harness.Settings.Get<GeneralSettings>().ConfirmCloseConnectedSessions);
		Assert.Contains(harness.Interaction.Notices, notice => notice.Contains("Settings, General", StringComparison.Ordinal));
		Assert.Empty(harness.Manager.Sessions);
	}

	[Fact]
	public async Task TheNextClose_AsksNothing()
	{
		using Harness harness = Harness.Create(new ConfirmResult(true, true));
		await harness.Actions.CloseAsync([harness.Manager.Add().Id]);

		await harness.Actions.CloseAsync([harness.Manager.Add().Id]);

		Assert.Single(harness.Interaction.Prompts);
	}

	[Fact]
	public async Task TurningThePromptAway_KeepsTheTabAndTheSetting()
	{
		// A prompt that was cancelled cannot also turn itself off, whatever the check box was left at.
		using Harness harness = Harness.Create(new ConfirmResult(false, true));
		FakeSession session = harness.Manager.Add();

		await harness.Actions.CloseAsync([session.Id]);

		Assert.True(harness.Settings.Get<GeneralSettings>().ConfirmCloseConnectedSessions);
		Assert.Single(harness.Manager.Sessions);
	}

	[Fact]
	public async Task ClosingSeveralAtOnce_AsksOnce()
	{
		using Harness harness = Harness.Create(new ConfirmResult(true, false));
		FakeSession first = harness.Manager.Add();
		FakeSession second = harness.Manager.Add();

		await harness.Actions.CloseAsync([first.Id, second.Id]);

		ConfirmPrompt prompt = Assert.Single(harness.Interaction.Prompts);
		Assert.Equal("Close sessions", prompt.Title);
		Assert.Contains("2 of 2 sessions", prompt.Message, StringComparison.Ordinal);
		Assert.Empty(harness.Manager.Sessions);
	}

	private sealed class Harness : IDisposable
	{
		private Harness(ManualSessionManager manager, SessionWorkspace workspace, SessionRestore restore, ScriptedInteraction interaction, FixedSettings settings, SessionActions actions)
		{
			Manager = manager;
			Workspace = workspace;
			Restore = restore;
			Interaction = interaction;
			Settings = settings;
			Actions = actions;
		}

		public ManualSessionManager Manager { get; }

		public SessionWorkspace Workspace { get; }

		public SessionRestore Restore { get; }

		public ScriptedInteraction Interaction { get; }

		public FixedSettings Settings { get; }

		public SessionActions Actions { get; }

		public static Harness Create(ConfirmResult answer)
		{
			ManualSessionManager manager = new();
			SessionWorkspace workspace = new(manager);
			UiStateStore uiState = new(new MemoryAppDataStore(), TimeProvider.System, NullLogger<UiStateStore>.Instance);
			ScriptedInteraction interaction = new(answer);
			FixedSettings settings = new();
			SessionRestore restore = new(manager, workspace, uiState, settings, interaction, NullLogger<SessionRestore>.Instance);
			SessionActions actions = new(
				manager,
				workspace,
				restore,
				new NoProtocols(),
				interaction,
				settings,
				new ShellState(uiState),
				NullLogger<SessionActions>.Instance);
			return new Harness(manager, workspace, restore, interaction, settings, actions);
		}

		public void Dispose()
		{
			Restore.Dispose();
			Workspace.Dispose();
		}
	}

	/// <summary>Answers every confirmation the same way and keeps what it was asked.</summary>
	private sealed class ScriptedInteraction(ConfirmResult answer) : IUserInteraction
	{
		private readonly List<ConfirmPrompt> _prompts = [];
		private readonly List<string> _notices = [];

		public IReadOnlyList<ConfirmPrompt> Prompts => _prompts;

		public IReadOnlyList<string> Notices => _notices;

		public Task<HostTrustDecision> ConfirmHostIdentityAsync(HostTrustPrompt prompt, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public Task<CredentialPromptResult?> PromptCredentialsAsync(CredentialPrompt prompt, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public Task<SecretPromptResult?> PromptSecretAsync(SecretPrompt prompt, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public Task<IReadOnlyList<string>?> PromptKeyboardInteractiveAsync(KeyboardInteractivePrompt prompt, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public Task<ConfirmResult> ConfirmAsync(ConfirmPrompt prompt, CancellationToken cancellationToken = default)
		{
			_prompts.Add(prompt);
			return Task.FromResult(answer);
		}

		public Task<OverwriteDecision> ConfirmOverwriteAsync(OverwritePrompt prompt, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public void Notify(NoticeSeverity severity, string message, string? title = null) => _notices.Add(message);
	}
}
