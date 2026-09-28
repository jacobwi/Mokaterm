using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Connections;
using Mokaterm.UI.Commands;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

public sealed class CommandSnippetActionsTests
{
	private static readonly HostProfile Host = new() { Id = Guid.NewGuid(), Name = "Build box", Address = "build-01.example.com" };

	private static readonly ConnectionProfile Connection = new()
	{
		Id = Guid.NewGuid(),
		HostId = Host.Id,
		ProtocolId = "ssh",
		Username = "bc",
	};

	[Fact]
	public void CreateQuickSnippet_SavedLogin_ScopesToThatLogin()
	{
		CommandSnippet snippet = CommandSnippetActions.CreateQuickSnippet("  sudo systemctl restart nginx  ", Host, Connection, isTransient: false);

		Assert.Equal(CommandSnippetScope.Connection, snippet.Scope);
		Assert.Equal(Connection.Id, snippet.ConnectionId);
		Assert.Equal("build-01.example.com", snippet.Host);
		Assert.Equal("sudo systemctl restart nginx", snippet.Command);
		Assert.Equal("sudo systemctl restart nginx", snippet.Name);
		Assert.NotEqual(Guid.Empty, snippet.Id);
	}

	[Fact]
	public void CreateQuickSnippet_QuickConnectSession_ScopesToTheMachine()
	{
		CommandSnippet snippet = CommandSnippetActions.CreateQuickSnippet("uptime", Host, Connection, isTransient: true);

		Assert.Equal(CommandSnippetScope.Host, snippet.Scope);
		Assert.Null(snippet.ConnectionId);
		Assert.Equal("build-01.example.com", snippet.Host);
	}

	[Fact]
	public void CreateQuickSnippet_NeverRunsOnItsOwn() =>
		Assert.False(CommandSnippetActions.CreateQuickSnippet("rm -rf /tmp/build", Host, Connection, isTransient: false).RunImmediately);

	[Fact]
	public void CreateQuickSnippet_LongCommand_GetsAShortenedName()
	{
		CommandSnippet snippet = CommandSnippetActions.CreateQuickSnippet(
			"find /var/log -name '*.log' -mtime +7 -print -delete",
			Host,
			Connection,
			isTransient: false);

		Assert.True(snippet.Name.Length <= CommandSnippetName.MaxLength);
		Assert.EndsWith("...", snippet.Name, StringComparison.Ordinal);
		Assert.Equal("find /var/log -name '*.log' -mtime +7 -print -delete", snippet.Command);
	}

	[Fact]
	public async Task ApplyAsync_TypesTheCommandIntoEverySessionThatTypesTogether()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		using InputBroadcast broadcast = new(workspace);
		FakeSession sender = manager.Add(terminal: new RecordingTerminalStream());
		FakeSession partner = manager.Add(terminal: new RecordingTerminalStream());
		FakeSession bystander = manager.Add(terminal: new RecordingTerminalStream());
		broadcast.Toggle(sender.Id);
		broadcast.Toggle(partner.Id);

		await Actions(broadcast).ApplyAsync(sender, Snippet("uptime"), run: true);

		Assert.Equal(["uptime\r"], Sent(sender));
		Assert.Equal(["uptime\r"], Sent(partner));
		Assert.Empty(Sent(bystander));
	}

	[Fact]
	public async Task ApplyAsync_WithNobodyToTypeWith_TouchesOnlyItsOwnSession()
	{
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		using InputBroadcast broadcast = new(workspace);
		FakeSession alone = manager.Add(terminal: new RecordingTerminalStream());
		FakeSession other = manager.Add(terminal: new RecordingTerminalStream());

		await Actions(broadcast).ApplyAsync(alone, Snippet("ls"), run: false);

		Assert.Equal(["ls"], Sent(alone));
		Assert.Empty(Sent(other));
	}

	private static List<string> Sent(FakeSession session) => ((RecordingTerminalStream)session.Terminal!).Sent;

	private static CommandSnippet Snippet(string command) => new()
	{
		Id = Guid.NewGuid(),
		Name = command,
		Command = command,
	};

	private static CommandSnippetActions Actions(InputBroadcast broadcast) => new(
		new EmptySnippetStore(),
		new UserInteractionService(),
		broadcast,
		new ShellState(new UiStateStore(new MemoryAppDataStore(), TimeProvider.System, NullLogger<UiStateStore>.Instance)),
		NullLogger<CommandSnippetActions>.Instance);

	/// <summary>A store that holds nothing; these tests only care about what reaches a terminal.</summary>
	private sealed class EmptySnippetStore : ICommandSnippetStore
	{
		public event Action? Changed
		{
			add { }
			remove { }
		}

		public ValueTask<IReadOnlyList<CommandSnippet>> ListAsync(CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<IReadOnlyList<CommandSnippet>>([]);

		public ValueTask<IReadOnlyList<CommandSnippet>> QueryAsync(CommandSnippetTarget target, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<IReadOnlyList<CommandSnippet>>([]);

		public Task<CommandSnippet> SaveAsync(CommandSnippet snippet, CancellationToken cancellationToken = default) =>
			Task.FromResult(snippet);

		public Task DeleteAsync(Guid snippetId, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task MarkUsedAsync(Guid snippetId, CancellationToken cancellationToken = default) => Task.CompletedTask;
	}
}
