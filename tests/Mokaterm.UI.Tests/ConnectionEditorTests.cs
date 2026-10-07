using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Storage;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Connections;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The login editor says what the login will land on and what each choice means: the machine with its environment, what
/// the protocol is, who else sees a credential, and a login this host already carries.
/// </summary>
public sealed class ConnectionEditorTests
{
	private const string Address = "10.10.2.3";

	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

	private static readonly Guid HostId = Guid.Parse("4b2e8d05-0d24-4f3a-9c21-7b0f36b1d001");

	private static readonly Guid LoginId = Guid.Parse("4b2e8d05-0d24-4f3a-9c21-7b0f36b1d002");

	private static readonly Guid OtherLoginId = Guid.Parse("4b2e8d05-0d24-4f3a-9c21-7b0f36b1d003");

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	// Saving a login can open a session on the spot, so the machine it lands on is shown the way the tree shows it.
	[Fact]
	public async Task Render_AProductionHost_ShowsItsAddressTagAndHowManyLoginsItHas()
	{
		string html = await RenderAsync(
			CatalogWith(Host(HostEnvironment.Production), Login(OtherLoginId, "deploy")),
			new ConnectionEditorRequest { HostId = HostId });

		Assert.Contains(Address, html, StringComparison.Ordinal);
		Assert.Contains("PRODUCTION", html, StringComparison.Ordinal);
		Assert.Contains("1 login", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_AHostWithNoLoginYet_SaysSoRatherThanShowingAZero()
	{
		string html = await RenderAsync(CatalogWith(Host()), new ConnectionEditorRequest { HostId = HostId });

		Assert.Contains("No logins yet", html, StringComparison.Ordinal);
	}

	// The protocol picker was a row of names; the descriptor already carries a sentence saying what each one is.
	[Fact]
	public async Task Render_TheProtocolPicker_SaysWhatTheChosenProtocolIs()
	{
		string html = await RenderAsync(CatalogWith(Host()), new ConnectionEditorRequest { HostId = HostId });

		Assert.Contains("Protocol", html, StringComparison.Ordinal);
		Assert.Contains(OneProtocol.Ssh.Description, html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_APasswordKeptWithTheLogin_SaysWhereItLives()
	{
		string html = await RenderAsync(CatalogWith(Host()), new ConnectionEditorRequest { HostId = HostId });

		Assert.Contains("Kept with this login alone, encrypted in the vault.", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ALoginThisHostAlreadyHas_SaysSoWithoutRefusingTheSave()
	{
		string html = await RenderAsync(
			CatalogWith(Host(), Login(LoginId, "deploy"), Login(OtherLoginId, "deploy")),
			new ConnectionEditorRequest { ConnectionId = LoginId });

		Assert.Contains("already has a login for deploy over SSH.", html, StringComparison.Ordinal);

		// The save buttons are still offered: the warning is a notice, not a refusal.
		Assert.Contains("Save and connect", html, StringComparison.Ordinal);
	}

	// Reopening a saved login must not report it against itself.
	[Fact]
	public async Task Render_TheLoginBeingEdited_IsNotItsOwnDuplicate()
	{
		string html = await RenderAsync(
			CatalogWith(Host(), Login(LoginId, "deploy")),
			new ConnectionEditorRequest { ConnectionId = LoginId });

		Assert.DoesNotContain("already has a", html, StringComparison.Ordinal);
	}

	// A login's theme is the narrowest of the four, which only matters once the host's environment already set one.
	[Fact]
	public async Task Render_AHostWhoseEnvironmentSetsATheme_SaysTheLoginsThemeWinsOverIt()
	{
		string html = await RenderAsync(
			CatalogWith(Host(HostEnvironment.Staging)),
			new ConnectionEditorRequest { HostId = HostId },
			terminal: new TerminalSettings { StagingThemeId = "amber" });

		Assert.Contains("Wins over the Night theme set for this environment.", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_AHostWithoutAnEnvironmentTheme_SaysTheThemeIsForThisLoginOnly()
	{
		string html = await RenderAsync(CatalogWith(Host()), new ConnectionEditorRequest { HostId = HostId });

		Assert.Contains("Only for this login.", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_NoSavedHost_OffersTheNewHostFieldsAndSaysWhatComesLater()
	{
		string html = await RenderAsync(ConnectionCatalog.Empty, new ConnectionEditorRequest());

		Assert.Contains("Color, environment and tags come later, from the host editor.", html, StringComparison.Ordinal);
		Assert.DoesNotContain("No logins yet", html, StringComparison.Ordinal);
	}

	private static HostProfile Host(HostEnvironment environment = HostEnvironment.None) => new()
	{
		Id = HostId,
		Address = Address,
		Environment = environment,
	};

	private static ConnectionProfile Login(Guid id, string username) => new()
	{
		Id = id,
		HostId = HostId,
		ProtocolId = OneProtocol.Ssh.Id,
		Username = username,
	};

	private static ConnectionCatalog CatalogWith(HostProfile host, params ConnectionProfile[] logins) => new([], [host], logins);

	private static Task<string> RenderAsync(ConnectionCatalog catalog, ConnectionEditorRequest request, TerminalSettings? terminal = null)
	{
		ShellState shell = new(new UiStateStore(new MemoryAppDataStore(), TimeProvider.System, NullLogger<UiStateStore>.Instance));
		shell.OpenEditor(request);

		return StaticRender.RenderAsync<ConnectionEditor>(
			services =>
			{
				services.AddMokaRed();
				services.AddMokatermUiCommon();
				services.AddSingleton<IAppDataStore, MemoryAppDataStore>();
				services.AddSingleton<IVault, UnlockedVault>();
				services.AddSingleton<IConnectionRepository>(new EditableCatalog(catalog));
				services.AddSingleton<ICredentialStore, NoCredentials>();
				services.AddSingleton<IProtocolRegistry, OneProtocol>();
				services.AddSingleton<ITerminalThemeCatalog>(new OneTheme("amber", "Night"));
				services.AddSingleton<ISettingsService>(new FixedSettings(terminal ?? new TerminalSettings()));
				services.AddSingleton<ISessionManager, ManualSessionManager>();
				services.AddSingleton(new UiStateStore(new MemoryAppDataStore(), TimeProvider.System, NullLogger<UiStateStore>.Instance));
				services.AddSingleton(shell);
				services.AddScoped<IUserInteraction, UserInteractionService>();
				services.AddScoped<CatalogState>();
				services.AddScoped<SessionWorkspace>();
				services.AddScoped<SessionRestore>();
				services.AddScoped<SessionActions>();
			},
			new Dictionary<string, object?>(StringComparer.Ordinal)).WaitAsync(Patience, Ct);
	}

	/// <summary>One protocol, so the picker has something to show and the editor has a description to repeat.</summary>
	private sealed class OneProtocol : IProtocolRegistry
	{
		public static ProtocolDescriptor Ssh { get; } = new()
		{
			Id = "ssh",
			DisplayName = "SSH",
			Description = "Secure shell with a file browser over SFTP.",
			DefaultPort = 22,
			Capabilities = ProtocolCapabilities.Terminal | ProtocolCapabilities.FileSystem,
			AuthenticationMethods = [AuthenticationMethod.Password, AuthenticationMethod.PublicKey],
		};

		public IReadOnlyList<ProtocolDescriptor> Protocols => [Ssh];

		// The editor reads a descriptor through the provider, so the registry has to hand one over.
		public IProtocolProvider? Find(string protocolId) =>
			string.Equals(protocolId, Ssh.Id, StringComparison.OrdinalIgnoreCase) ? SshProvider.Instance : null;

		public IProtocolProvider Get(string protocolId) => Find(protocolId) ?? throw new KeyNotFoundException(protocolId);
	}

	/// <summary>Carries the descriptor and nothing else: rendering an editor never opens a session.</summary>
	private sealed class SshProvider : IProtocolProvider
	{
		public static SshProvider Instance { get; } = new();

		public ProtocolDescriptor Descriptor => OneProtocol.Ssh;

		public Task<IProtocolSession> ConnectAsync(ProtocolConnectContext context, CancellationToken cancellationToken) =>
			throw new NotSupportedException();
	}

	/// <summary>An unlocked vault with nothing saved in it, which is all the editor needs to fill its keychain lists.</summary>
	private sealed class NoCredentials : ICredentialStore
	{
		public event Action? Changed
		{
			add { }
			remove { }
		}

		public ValueTask<IReadOnlyList<CredentialInfo>> ListAsync(CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<IReadOnlyList<CredentialInfo>>([]);

		public ValueTask<CredentialInfo?> FindAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask<CredentialInfo> SaveAsync(CredentialInfo info, CredentialSecretInput? secret, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public ValueTask<CredentialSecret> RevealAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
	}
}
