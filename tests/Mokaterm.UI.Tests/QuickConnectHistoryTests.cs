using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.QuickConnect;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The quick connect history: the vault and nowhere else, newest first, one entry per target, twenty at most, and only
/// targets that actually connected. A locked vault shows nothing and keeps nothing.
/// </summary>
public sealed class QuickConnectHistoryTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Record_PutsTheNewestFirst()
	{
		using QuickConnectHistory history = Create(out _);

		await history.RecordAsync(Entry("ssh", "root", "a.example"), Ct);
		await history.RecordAsync(Entry("vnc", null, "b.example", 5901), Ct);

		Assert.Equal(["b.example:5901", "root@a.example"], (await history.ListAsync(Ct)).Select(entry => entry.Text));
	}

	// Typing the same machine in capitals the next time moves the old entry up instead of adding a second one.
	[Fact]
	public async Task Record_SameTarget_MovesItUpAndKeepsOneEntry()
	{
		using QuickConnectHistory history = Create(out _);
		await history.RecordAsync(Entry("ssh", "root", "a.example"), Ct);
		await history.RecordAsync(Entry("ssh", "root", "b.example"), Ct);

		await history.RecordAsync(Entry("SSH", "root", "A.EXAMPLE"), Ct);

		Assert.Equal(["root@A.EXAMPLE", "root@b.example"], (await history.ListAsync(Ct)).Select(entry => entry.Text));
	}

	[Fact]
	public async Task Record_ADifferentProtocolUserOrPort_IsADifferentTarget()
	{
		using QuickConnectHistory history = Create(out _);

		await history.RecordAsync(Entry("ssh", "root", "a.example"), Ct);
		await history.RecordAsync(Entry("sftp", "root", "a.example"), Ct);
		await history.RecordAsync(Entry("ssh", "deploy", "a.example"), Ct);
		await history.RecordAsync(Entry("ssh", "root", "a.example", 2222), Ct);

		Assert.Equal(4, (await history.ListAsync(Ct)).Count);
	}

	[Fact]
	public async Task Record_KeepsTheLastTwenty()
	{
		using QuickConnectHistory history = Create(out _);
		for (int index = 0; index < QuickConnectHistory.MaxEntries + 5; index++)
		{
			await history.RecordAsync(Entry("ssh", null, $"host-{index:00}"), Ct);
		}

		IReadOnlyList<QuickConnectRecent> entries = await history.ListAsync(Ct);

		Assert.Equal(QuickConnectHistory.MaxEntries, entries.Count);
		Assert.Equal("host-24", entries[0].Address);
		Assert.Equal("host-05", entries[^1].Address);
	}

	[Fact]
	public async Task Remove_ForgetsOneTarget()
	{
		using QuickConnectHistory history = Create(out _);
		await history.RecordAsync(Entry("ssh", "root", "a.example"), Ct);
		await history.RecordAsync(Entry("ssh", "root", "b.example"), Ct);

		await history.RemoveAsync(Entry("ssh", "ROOT", "a.example"), Ct);

		Assert.Equal(["root@b.example"], (await history.ListAsync(Ct)).Select(entry => entry.Text));
	}

	[Fact]
	public async Task Clear_DeletesTheDocument()
	{
		using QuickConnectHistory history = Create(out MemoryVaultDataStore vault);
		await history.RecordAsync(Entry("ssh", "root", "a.example"), Ct);

		await history.ClearAsync(Ct);

		Assert.Empty(vault.Documents);
		Assert.Empty(await history.ListAsync(Ct));
	}

	// Host and user names never go to a plain file: the history is a vault document of its own.
	[Fact]
	public async Task Entries_AreStoredInTheirOwnVaultDocument()
	{
		using QuickConnectHistory history = Create(out MemoryVaultDataStore vault);

		await history.RecordAsync(Entry("ssh", "deploy", "secret-host.example"), Ct);

		Assert.Equal(QuickConnectHistoryDocument.DocumentName, Assert.Single(vault.Documents.Keys));
		Assert.Contains("secret-host.example", vault.Documents[QuickConnectHistoryDocument.DocumentName], StringComparison.Ordinal);
	}

	[Fact]
	public async Task ALockedVault_ShowsNothingAndKeepsNothing()
	{
		using QuickConnectHistory history = Create(out MemoryVaultDataStore vault);
		await history.RecordAsync(Entry("ssh", "root", "a.example"), Ct);
		string before = vault.Documents[QuickConnectHistoryDocument.DocumentName];
		vault.Locked = true;

		Assert.Empty(await history.ListAsync(Ct));
		await history.RecordAsync(Entry("ssh", "root", "b.example"), Ct);
		await history.RemoveAsync(Entry("ssh", "root", "a.example"), Ct);
		await history.ClearAsync(Ct);

		vault.Locked = false;
		Assert.Equal(before, vault.Documents[QuickConnectHistoryDocument.DocumentName]);
	}

	[Fact]
	public async Task Changed_IsRaisedForTheHistoryDocumentOnly()
	{
		using QuickConnectHistory history = Create(out MemoryVaultDataStore vault);
		int changes = 0;
		history.Changed += () => changes++;

		await vault.WriteAsync("file-bookmarks", new QuickConnectHistoryDocument(), Ct);
		await history.RecordAsync(Entry("ssh", "root", "a.example"), Ct);

		Assert.Equal(1, changes);
	}

	// A typo never connects, so it never becomes a suggestion.
	[Fact]
	public async Task RecordWhenConnected_KeepsTheTargetOnlyOnceTheSessionConnects()
	{
		ManualSessionManager sessions = new();
		using QuickConnectHistory history = Create(out MemoryVaultDataStore vault, sessions);
		ChangingSession session = new();
		sessions.Add(session);

		history.RecordWhenConnected(session, Entry("ssh", "root", "a.example"));
		session.Set(SessionState.Failed);
		await Eventually.StaysTrueAsync(() => history.PendingCount == 1, "the failed session stays pending");
		Assert.Empty(await history.ListAsync(Ct));

		// A reconnect that gets through counts: the target did open.
		session.Set(SessionState.Connected);
		await Eventually.TrueAsync(() => history.PendingCount == 0, "the connect was noticed");
		await Eventually.TrueAsync(() => vault.Documents.Count == 1, "the target was kept");
		Assert.Equal(["root@a.example"], (await history.ListAsync(Ct)).Select(entry => entry.Text));
	}

	[Fact]
	public async Task RecordWhenConnected_ASessionClosedBeforeItConnected_LeavesNothing()
	{
		ManualSessionManager sessions = new();
		using QuickConnectHistory history = Create(out MemoryVaultDataStore vault, sessions);
		ChangingSession session = new();
		sessions.Add(session);
		history.RecordWhenConnected(session, Entry("ssh", "root", "a.example"));

		sessions.Remove(session.Id);
		session.Set(SessionState.Connected);

		Assert.Equal(0, history.PendingCount);
		await Eventually.StaysTrueAsync(() => vault.Documents.Count == 0, "nothing was written");
	}

	// Saved as a login while it was still connecting: the login is what the box offers from then on.
	[Fact]
	public async Task RecordWhenConnected_ASessionSavedMeanwhile_LeavesNothing()
	{
		ManualSessionManager sessions = new();
		using QuickConnectHistory history = Create(out MemoryVaultDataStore vault, sessions);
		ChangingSession session = new();
		sessions.Add(session);
		history.RecordWhenConnected(session, Entry("ssh", "root", "a.example"));

		session.IsTransient = false;
		session.Set(SessionState.Connected);

		Assert.Equal(0, history.PendingCount);
		await Eventually.StaysTrueAsync(() => vault.Documents.Count == 0, "nothing was written");
	}

	[Fact]
	public async Task RecordWhenConnected_StampsTheTimeOfTheConnect()
	{
		ManualSessionManager sessions = new();
		FakeTimeProvider time = new(DateTimeOffset.Parse("2026-10-11T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
		using QuickConnectHistory history = Create(out MemoryVaultDataStore vault, sessions, time);
		ChangingSession session = new();
		sessions.Add(session);
		history.RecordWhenConnected(session, Entry("ssh", "root", "a.example"));

		time.Advance(TimeSpan.FromMinutes(3));
		session.Set(SessionState.Connected);

		await Eventually.TrueAsync(() => vault.Documents.Count == 1, "the target was kept");
		Assert.Equal(time.GetUtcNow(), (await history.ListAsync(Ct))[0].ConnectedAt);
	}

	[Fact]
	public void Dispose_LetsGoOfEveryWaitingSession()
	{
		ManualSessionManager sessions = new();
		QuickConnectHistory history = Create(out _, sessions);
		ChangingSession session = new();
		sessions.Add(session);
		history.RecordWhenConnected(session, Entry("ssh", "root", "a.example"));

		history.Dispose();

		Assert.Equal(0, history.PendingCount);
		Assert.Equal(0, session.Listeners);
	}

	private static QuickConnectHistory Create(out MemoryVaultDataStore vault, ManualSessionManager? sessions = null, TimeProvider? time = null)
	{
		vault = new MemoryVaultDataStore();
		return new QuickConnectHistory(vault, sessions ?? new ManualSessionManager(), time ?? TimeProvider.System, NullLogger<QuickConnectHistory>.Instance);
	}

	private static QuickConnectRecent Entry(string protocol, string? user, string address, int? port = null) =>
		QuickConnectRecent.From(protocol, new QuickConnectTarget(protocol, user, address, port), DateTimeOffset.UnixEpoch);

	/// <summary>A quick-connect session whose state the test moves by hand.</summary>
	private sealed class ChangingSession : ISessionHandle
	{
		private Action? _changed;

		public event Action? Changed
		{
			add => _changed += value;
			remove => _changed -= value;
		}

		public int Listeners => _changed?.GetInvocationList().Length ?? 0;

		public Guid Id { get; } = Guid.NewGuid();

		public string Title => "root@a.example";

		public HostProfile Host { get; } = new() { Id = Guid.NewGuid(), Address = "a.example" };

		public ConnectionProfile Connection => new() { Id = Guid.NewGuid(), HostId = Host.Id, ProtocolId = "ssh", Username = "root" };

		public ProtocolDescriptor Protocol { get; } = new()
		{
			Id = "ssh",
			DisplayName = "SSH",
			DefaultPort = 22,
			Capabilities = ProtocolCapabilities.Terminal,
		};

		public bool IsTransient { get; set; } = true;

		public SessionState State { get; private set; } = SessionState.Connecting;

		public string? StatusMessage => null;

		public ConnectFailure? Failure => null;

		public DateTimeOffset OpenedAt => DateTimeOffset.UnixEpoch;

		public DateTimeOffset? ConnectedAt => null;

		public ITerminalStream? Terminal => null;

		public IProtocolSession? Session => null;

		public void Set(SessionState state)
		{
			State = state;
			_changed?.Invoke();
		}
	}
}
