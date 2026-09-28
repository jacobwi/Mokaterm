using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Storage;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Vault;

namespace Mokaterm.UI.Tests;

public sealed class DamagedDocumentNoticesTests
{
	private const string QuarantinePath = @"C:\Users\bc\AppData\Local\Mokaterm\vault\connections.corrupt-20260918T120000000Z.vault";

	[Fact]
	public void Describe_SavedHosts_SaysWhatWasLostAndWhereTheDamagedCopyIs()
	{
		(string title, string message) = DamagedDocumentNotices.Describe(new DamagedDocument { Name = "connections", Path = QuarantinePath, MovedAside = true });

		Assert.Equal("Saved hosts could not be read", title);
		Assert.Contains("start empty", message, StringComparison.Ordinal);
		Assert.EndsWith(QuarantinePath, message, StringComparison.Ordinal);
	}

	[Fact]
	public void Describe_ACopyThatCouldNotBeMovedAside_SaysToSaveItBeforeTheNextChange()
	{
		(_, string message) = DamagedDocumentNotices.Describe(new DamagedDocument { Name = "credentials", Path = "/srv/mokaterm/vault/credentials.vault" });

		Assert.Contains("could not be moved aside", message, StringComparison.Ordinal);
		Assert.Contains("copy /srv/mokaterm/vault/credentials.vault somewhere safe", message, StringComparison.Ordinal);
	}

	[Fact]
	public void Describe_AModuleDocument_NamesIt()
	{
		(string title, _) = DamagedDocumentNotices.Describe(new DamagedDocument { Name = "ssh-agents", Path = "/data/vault/ssh-agents.corrupt-1.vault", MovedAside = true });

		Assert.Contains("\"ssh-agents\"", title, StringComparison.Ordinal);
	}

	[Fact]
	public void Notice_TheSameDamagedCopyReportedTwice_IsShownOnce()
	{
		VaultData store = new();
		using UserInteractionService interaction = new();
		using DamagedDocumentNotices notices = new(store, interaction);
		notices.Start();
		DamagedDocument damaged = new() { Name = "commands", Path = "/data/vault/commands.vault" };

		store.Damage(damaged);
		store.Damage(damaged);

		PendingNotice notice = Assert.Single(interaction.TakeNotices());
		Assert.Equal(NoticeSeverity.Error, notice.Severity);
		Assert.Equal("Saved commands could not be read", notice.Title);
	}

	[Fact]
	public void Dispose_StopsListening()
	{
		VaultData store = new();
		using UserInteractionService interaction = new();
		DamagedDocumentNotices notices = new(store, interaction);
		notices.Start();

		notices.Dispose();
		store.Damage(new DamagedDocument { Name = "commands", Path = "/data/vault/commands.corrupt-1.vault", MovedAside = true });

		Assert.Empty(interaction.TakeNotices());
	}

	private sealed class VaultData : IVaultDataStore
	{
		public event Action<string>? DocumentChanged
		{
			add { }
			remove { }
		}

		public event Action<DamagedDocument>? DocumentDamaged;

		public void Damage(DamagedDocument document) => DocumentDamaged?.Invoke(document);

		public ValueTask<T?> ReadAsync<T>(string name, CancellationToken cancellationToken = default)
			where T : class => throw new NotSupportedException();

		public Task WriteAsync<T>(string name, T document, CancellationToken cancellationToken = default)
			where T : class => throw new NotSupportedException();

		public Task DeleteAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
	}
}
