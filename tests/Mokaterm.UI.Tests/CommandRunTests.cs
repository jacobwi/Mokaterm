using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Storage;
using Mokaterm.UI.Commands;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Connections;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

/// <summary>Which logins a command run starts from, what saving its command keeps, and what the run dialog lists.</summary>
public sealed class CommandRunTests
{
	private static readonly ProtocolDescriptor Ssh = new()
	{
		Id = "ssh",
		DisplayName = "SSH",
		DefaultPort = 22,
		Capabilities = ProtocolCapabilities.Terminal | ProtocolCapabilities.Commands,
	};

	private static readonly ProtocolDescriptor Sftp = new()
	{
		Id = "sftp",
		DisplayName = "SFTP",
		DefaultPort = 22,
		Capabilities = ProtocolCapabilities.FileSystem,
		VariantOf = "ssh",
	};

	private static readonly ConnectionFolder Servers = new() { Id = Guid.NewGuid(), Name = "Servers" };
	private static readonly ConnectionFolder Web = new() { Id = Guid.NewGuid(), Name = "Web", ParentId = Servers.Id };
	private static readonly HostProfile Db = new() { Id = Guid.NewGuid(), Name = "db", Address = "10.0.0.10", FolderId = Servers.Id };
	private static readonly HostProfile Front = new() { Id = Guid.NewGuid(), Name = "front", Address = "10.0.0.20", FolderId = Web.Id };
	private static readonly HostProfile Loose = new() { Id = Guid.NewGuid(), Address = "10.0.0.30" };
	private static readonly ConnectionProfile DbShell = Login(Db, "ssh", "ops");
	private static readonly ConnectionProfile DbFiles = Login(Db, "sftp", "files");
	private static readonly ConnectionProfile FrontShell = Login(Front, "ssh", "deploy");
	private static readonly ConnectionProfile LooseShell = Login(Loose, "ssh", "root");

	private static readonly ConnectionCatalog Catalog = new(
		[Servers, Web],
		[Db, Front, Loose],
		[DbShell, DbFiles, FrontShell, LooseShell]);

	[Fact]
	public void InFolder_TakesTheLoginsOfEveryHostBelow_SubfoldersIncluded()
	{
		// A host lists its logins the way the tree does, by protocol: sftp before ssh.
		Assert.Equal([DbFiles.Id, DbShell.Id, FrontShell.Id], CommandRunTargets.InFolder(Catalog, Servers.Id).Select(login => login.Id));
		Assert.Equal([FrontShell.Id], CommandRunTargets.InFolder(Catalog, Web.Id).Select(login => login.Id));
	}

	[Fact]
	public void InFolder_SurvivesAFolderThatIsItsOwnAncestor()
	{
		ConnectionFolder looped = Servers with { ParentId = Web.Id };
		ConnectionCatalog damaged = Catalog with { Folders = [looped, Web] };

		Assert.Equal(3, CommandRunTargets.InFolder(damaged, Servers.Id).Count);
	}

	[Fact]
	public void Runnable_LeavesOutLoginsWhoseProtocolCannotRunCommands_AndRepeats()
	{
		List<Guid> runnable = CommandRunTargets.Runnable([DbShell, DbFiles, FrontShell, DbShell], new TwoProtocols());

		Assert.Equal([DbShell.Id, FrontShell.Id], runnable);
		Assert.Equal("SFTP logins cannot run a command without a terminal.", CommandRunTargets.Unavailable(DbFiles, new TwoProtocols()));
	}

	[Fact]
	public void ForSnippet_FollowsTheScopeOfTheSavedCommand()
	{
		CommandSnippet forLogin = Snippet(CommandSnippetScope.Connection) with { ConnectionId = FrontShell.Id, Host = Front.Address };
		CommandSnippet forMachine = Snippet(CommandSnippetScope.Host) with { Host = "10.0.0.10" };

		Assert.Equal([FrontShell.Id], CommandRunTargets.ForSnippet(Catalog, forLogin).Select(login => login.Id));
		Assert.Equal([DbFiles.Id, DbShell.Id], CommandRunTargets.ForSnippet(Catalog, forMachine).Select(login => login.Id));
		Assert.Empty(CommandRunTargets.ForSnippet(Catalog, Snippet(CommandSnippetScope.Global)));
	}

	[Fact]
	public void CreateRunSnippet_ScopesToWhatTheRunCovered()
	{
		CommandSnippet one = CommandSnippetActions.CreateRunSnippet(" uptime ", [(Db, DbShell)]);
		CommandSnippet sameMachine = CommandSnippetActions.CreateRunSnippet("uptime", [(Db, DbShell), (Db, DbFiles)]);
		CommandSnippet many = CommandSnippetActions.CreateRunSnippet("uptime", [(Db, DbShell), (Front, FrontShell)]);

		Assert.Equal(CommandSnippetScope.Connection, one.Scope);
		Assert.Equal(DbShell.Id, one.ConnectionId);
		Assert.Equal("uptime", one.Command);
		Assert.Equal(CommandSnippetScope.Host, sameMachine.Scope);
		Assert.Equal("10.0.0.10", sameMachine.Host);
		Assert.Null(sameMachine.ConnectionId);
		Assert.Equal(CommandSnippetScope.Global, many.Scope);
		Assert.Null(many.Host);
		Assert.False(many.RunImmediately);
	}

	[Fact]
	public async Task Dialog_FromThePalette_ListsEveryLogin_AndSaysWhyOneCannotBePicked()
	{
		string html = await RenderDialogAsync(new CommandRunEditorRequest());

		Assert.Contains("Run a command on several hosts", html, StringComparison.Ordinal);
		Assert.Contains("ops@10.0.0.10", html, StringComparison.Ordinal);
		Assert.Contains("deploy@10.0.0.20", html, StringComparison.Ordinal);
		Assert.Contains("SFTP logins cannot run a command without a terminal.", html, StringComparison.Ordinal);
		Assert.Contains("0 of 3 picked", html, StringComparison.Ordinal);
		Assert.Contains("Skip hosts that would ask", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Run on", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Dialog_FromASelection_StartsOnThePickedLogins_AndNeverPicksOneThatCannotRun()
	{
		string html = await RenderDialogAsync(new CommandRunEditorRequest { ConnectionIds = [DbShell.Id, DbFiles.Id, FrontShell.Id] });

		Assert.Contains("2 of 3 picked", html, StringComparison.Ordinal);
		Assert.Contains("Run on 2 logins", html, StringComparison.Ordinal);
		Assert.Contains("deploy@10.0.0.20", html, StringComparison.Ordinal);
		Assert.DoesNotContain("root@10.0.0.30", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Dialog_FromASavedCommand_FillsTheCommand_AndPicksTheLoginsItsScopeNames()
	{
		CommandSnippet snippet = Snippet(CommandSnippetScope.Connection) with { ConnectionId = LooseShell.Id, Host = Loose.Address, Command = "df -h /var" };

		string html = await RenderDialogAsync(new CommandRunEditorRequest { Snippet = snippet });

		Assert.Contains("df -h /var", html, StringComparison.Ordinal);
		Assert.Contains("Run on 1 login", html, StringComparison.Ordinal);
	}

	[Fact]
	public void Request_NeverPrintsTheCommandOfItsSavedCommand()
	{
		CommandRunEditorRequest request = new() { Snippet = Snippet(CommandSnippetScope.Global) with { Command = "curl -H token-s3cr3t" } };

		Assert.DoesNotContain("s3cr3t", request.ToString(), StringComparison.Ordinal);
	}

	private static ConnectionProfile Login(HostProfile host, string protocolId, string user) => new()
	{
		Id = Guid.NewGuid(),
		HostId = host.Id,
		ProtocolId = protocolId,
		Username = user,
	};

	private static CommandSnippet Snippet(CommandSnippetScope scope) => new()
	{
		Id = Guid.NewGuid(),
		Name = "uptime",
		Command = "uptime",
		Scope = scope,
	};

	private static Task<string> RenderDialogAsync(CommandRunEditorRequest request)
	{
		ShellState shell = new(new UiStateStore(new MemoryAppDataStore(), TimeProvider.System, NullLogger<UiStateStore>.Instance));
		_ = shell.OpenEditor(request);
		return StaticRender.RenderAsync<CommandRunDialog>(
			services =>
			{
				services.AddMokaRed();
				services.AddMokatermUiCommon();
				services.AddSingleton(TimeProvider.System);
				services.AddSingleton<IAppDataStore, MemoryAppDataStore>();
				services.AddSingleton<IVault, UnlockedVault>();
				services.AddSingleton<IConnectionRepository>(new FixedRepository());
				services.AddSingleton<IProtocolRegistry, TwoProtocols>();
				services.AddSingleton<ISettingsService>(new FixedSettings());
				services.AddSingleton<ICommandSnippetStore, NoSnippets>();
				services.AddSingleton<ICommandRunner, UnusedRunner>();
				services.AddSingleton(shell);
				services.AddScoped<UiStateStore>();
				services.AddScoped<CatalogState>();
				services.AddScoped<FormInterop>();
				services.AddScoped<IUserInteraction, UserInteractionService>();
				services.AddScoped<ISessionManager, ManualSessionManager>();
				services.AddScoped<SessionWorkspace>();
				services.AddScoped<InputBroadcast>();
				services.AddScoped<CommandSnippetActions>();
			},
			new Dictionary<string, object?>(StringComparer.Ordinal));
	}

	private sealed class TwoProtocols : IProtocolRegistry
	{
		private readonly Dictionary<string, IProtocolProvider> _providers = new(StringComparer.OrdinalIgnoreCase)
		{
			["ssh"] = new DescriptorOnly(Ssh),
			["sftp"] = new DescriptorOnly(Sftp),
		};

		public IReadOnlyList<ProtocolDescriptor> Protocols => [Ssh, Sftp];

		public IProtocolProvider? Find(string protocolId) => _providers.GetValueOrDefault(protocolId);

		public IProtocolProvider Get(string protocolId) => _providers[protocolId];
	}

	private sealed class DescriptorOnly(ProtocolDescriptor descriptor) : IProtocolProvider
	{
		public ProtocolDescriptor Descriptor => descriptor;

		public Task<IProtocolSession> ConnectAsync(ProtocolConnectContext context, CancellationToken cancellationToken) =>
			throw new NotSupportedException("The dialog never connects by itself.");
	}

	private sealed class FixedRepository : IConnectionRepository
	{
		public event Action? Changed
		{
			add { }
			remove { }
		}

		public ValueTask<ConnectionCatalog> GetCatalogAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Catalog);

		public Task SaveFolderAsync(ConnectionFolder folder, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task DeleteFolderAsync(Guid folderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task SaveHostAsync(HostProfile host, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task DeleteHostAsync(Guid hostId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task SaveConnectionAsync(ConnectionProfile connection, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task DeleteConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task MarkConnectedAsync(Guid connectionId, DateTimeOffset connectedAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	private sealed class NoSnippets : ICommandSnippetStore
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

		public Task<CommandSnippet> SaveAsync(CommandSnippet snippet, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task DeleteAsync(Guid snippetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task MarkUsedAsync(Guid snippetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
	}

	private sealed class UnusedRunner : ICommandRunner
	{
		public Task<ICommandRun> StartAsync(CommandRunRequest request, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException("Rendering never starts a run.");
	}
}
