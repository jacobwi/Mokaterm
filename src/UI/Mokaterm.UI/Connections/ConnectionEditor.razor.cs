using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Presentation;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Connections;

/// <summary>
/// Creates or edits a login: host (picked or created inline), protocol, port, user, authentication with its saved or
/// keychain credential, protocol options from the module, and per-login terminal appearance.
/// </summary>
public sealed partial class ConnectionEditor : ShellEditorBase<ConnectionEditorRequest>
{
	private const string SavedHostMode = "saved";
	private const string NewHostMode = "new";
	private const string LoginSource = "login";
	private const string KeychainSource = "keychain";

	private bool _loading;
	private bool _saving;
	private string? _error;

	// What an empty port means with the options as they stand, reported by the protocol's own editor.
	private int? _defaultPort;

	private ConnectionCatalog _catalog = ConnectionCatalog.Empty;
	private ConnectionProfile? _original;
	private List<HostProfile> _hosts = [];
	private IReadOnlyList<Guid?> _hostOptions = [];
	private Dictionary<Guid, CredentialInfo> _credentialsById = [];
	private IReadOnlyList<Guid?> _keychainPasswords = [];
	private IReadOnlyList<Guid?> _keychainKeys = [];
	private IReadOnlyList<string> _themeIds = [];

	private bool _newHost;
	private Guid? _hostId;
	private string _newHostAddress = "";
	private string _newHostName = "";
	private string? _hostError;

	private string _protocolId = "";
	private string _username = "";
	private string _port = "";
	private string _label = "";
	private AuthenticationMethod _method;
	private ProtocolOptions _options = ProtocolOptions.Empty;
	private string? _portError;

	private CredentialInfo? _existingCredential;
	private bool _useKeychain;
	private Guid? _keychainCredentialId;
	private string _password = "";
	private bool _forgetPassword;
	private string? _privateKey;
	private string? _passphrase;
	private string? _credentialError;

	// Typed in the protocol's own editor and saved with this login's credential: null keeps the stored one, "" forgets it.
	private string? _proxyPassword;

	// Empty means "use the global terminal theme".
	private string _themeChoice = "";
	private string _fontSize = "";
	private string _fontFamily = "";
	private string? _fontSizeError;
	private string? _fontFamilyError;

	[Inject]
	private CatalogState Catalog { get; set; } = default!;

	[Inject]
	private IConnectionRepository Repository { get; set; } = default!;

	[Inject]
	private ICredentialStore Credentials { get; set; } = default!;

	[Inject]
	private IProtocolRegistry Protocols { get; set; } = default!;

	[Inject]
	private IUiContributions Contributions { get; set; } = default!;

	[Inject]
	private ITerminalThemeCatalog Themes { get; set; } = default!;

	[Inject]
	private ISettingsService Settings { get; set; } = default!;

	[Inject]
	private SessionActions Sessions { get; set; } = default!;

	[Inject]
	private ILogger<ConnectionEditor> Logger { get; set; } = default!;

	protected override bool IsBusy => _saving;

	/// <summary>The fields only reach the page once the catalog and the credentials are in, so focus waits for them.</summary>
	protected override bool IsReady => !_loading;

	private string DialogTitle =>
		Request?.TransientSession is not null ? "Save connection"
		: _original is null ? "New login"
		: "Edit login";

	private ProtocolDescriptor? Protocol => Protocols.FindDescriptor(_protocolId);

	private string ProtocolName => Protocol?.DisplayName ?? _protocolId.ToUpperInvariant();

	private string DefaultPortText =>
		(_defaultPort ?? Protocol?.DefaultPort)?.ToString(CultureInfo.InvariantCulture) ?? "";

	/// <summary>False for a protocol without a network port, such as a serial line.</summary>
	private bool ShowPort => Protocol?.UsesPort != false;

	private string UsernamePlaceholder => Protocol?.RequiresUsername == true ? "Asked when connecting" : "Optional";

	private string LabelPlaceholder
	{
		get
		{
			string address = _newHost ? HostAddress.Normalize(_newHostAddress) : SelectedHost?.Address ?? "";
			return address.Length == 0 ? "user@host" : string.IsNullOrWhiteSpace(_username) ? address : $"{_username.Trim()}@{address}";
		}
	}

	private HostProfile? SelectedHost => _hostId is { } id ? _catalog.FindHost(id) : null;

	private string PasswordPlaceholder => HasPrivateCredential(CredentialKind.Password)
		? "Saved. Leave empty to keep it."
		: "Leave empty to be asked when connecting";

	private string GlobalThemeText =>
		$"Global ({Themes.Get(Settings.Get<TerminalSettings>().ThemeId).Name})";

	// Invariant, like the parser, so the placeholder can be typed back in as shown.
	private string GlobalFontSizeText =>
		Settings.Get<TerminalSettings>().FontSize.ToString(CultureInfo.InvariantCulture);

	private static string FontSizeRequirement => string.Create(
		CultureInfo.CurrentCulture,
		$"Use a size from {TerminalSettings.MinFontSize} to {TerminalSettings.MaxFontSize}, or leave it empty.");

	private Type? OptionsEditorType =>
		Contributions.FindProtocol(_protocolId)?.OptionsEditor
		?? (Protocol?.VariantOf is { } parent ? Contributions.FindProtocol(parent)?.OptionsEditor : null);

	private Dictionary<string, object> OptionsEditorParameters => new()
	{
		[nameof(ConnectionOptionsEditorBase.Options)] = _options,
		[nameof(ConnectionOptionsEditorBase.OptionsChanged)] = EventCallback.Factory.Create<ProtocolOptions>(this, OnOptionsChanged),
		[nameof(ConnectionOptionsEditorBase.ProtocolId)] = _protocolId,
		[nameof(ConnectionOptionsEditorBase.Disabled)] = _saving,
		[nameof(ConnectionOptionsEditorBase.DefaultPortChanged)] = EventCallback.Factory.Create<int?>(this, OnDefaultPortChanged),
		[nameof(ConnectionOptionsEditorBase.ProxyPassword)] = _proxyPassword ?? "",
		[nameof(ConnectionOptionsEditorBase.ProxyPasswordChanged)] = EventCallback.Factory.Create<string?>(this, OnProxyPasswordChanged),
		[nameof(ConnectionOptionsEditorBase.HasProxyPassword)] = HasStoredProxyPassword,
		[nameof(ConnectionOptionsEditorBase.CanSaveProxyPassword)] = CanSaveProxyPassword,
	};

	/// <summary>True when the credential the login uses today already holds a proxy password.</summary>
	private bool HasStoredProxyPassword =>
		(_useKeychain
			? _keychainCredentialId is { } keychainId && _credentialsById.TryGetValue(keychainId, out CredentialInfo? shared) ? shared : null
			: PrivateCredential) is { HasProxyPassword: true };

	/// <summary>
	/// True when this login writes a credential of its own that a proxy password can go into. An agent or anonymous login
	/// has none, a keychain entry belongs to every login that uses it (the keychain edits its proxy password), and a
	/// password this login asks for at connect time stores nothing either.
	/// </summary>
	private bool CanSaveProxyPassword => !_useKeychain && CredentialKindFor(_method) switch
	{
		CredentialKind.Password => !_forgetPassword && (_password.Length > 0 || HasPrivateCredential(CredentialKind.Password)),
		CredentialKind.PrivateKey => !string.IsNullOrWhiteSpace(_privateKey) || HasPrivateCredential(CredentialKind.PrivateKey),
		_ => false,
	};

	private void OnDefaultPortChanged(int? port)
	{
		if (_defaultPort != port)
		{
			_defaultPort = port;
			StateHasChanged();
		}
	}

	private static CredentialKind? CredentialKindFor(AuthenticationMethod method) => method switch
	{
		AuthenticationMethod.Password or AuthenticationMethod.KeyboardInteractive => CredentialKind.Password,
		AuthenticationMethod.PublicKey => CredentialKind.PrivateKey,
		_ => null,
	};

	private string MethodName(AuthenticationMethod method) => method switch
	{
		AuthenticationMethod.Password => "Password",
		AuthenticationMethod.PublicKey => "Public key",
		AuthenticationMethod.KeyboardInteractive => "Keyboard-interactive",
		AuthenticationMethod.Agent => "SSH agent",
		// A login without credentials is called anonymous on FTP, but on a remote desktop it means the far side asks.
		_ => IsRemoteDesktop ? "No password" : "Anonymous",
	};

	private string? MethodHelp => _method switch
	{
		AuthenticationMethod.Anonymous when IsRemoteDesktop =>
			"Mokaterm sends no password. The server, or the desktop behind it, asks for the login itself.",
		AuthenticationMethod.Anonymous => "Logs in as anonymous, without a password.",
		AuthenticationMethod.Password when IsRemoteDesktop => "The password the remote desktop server itself asks for.",
		AuthenticationMethod.Password => "Asked for at connect time when none is saved here.",
		AuthenticationMethod.KeyboardInteractive => "The server asks the questions. A saved password answers a plain password prompt.",
		AuthenticationMethod.PublicKey => "The key is used first; the server may still ask for more.",
		_ => null,
	};

	private bool IsRemoteDesktop => Protocol?.Has(ProtocolCapabilities.RemoteDesktop) == true;

	private static string? Family(ProtocolDescriptor? protocol) => protocol is null ? null : protocol.VariantOf ?? protocol.Id;

	private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

	private IReadOnlyList<Guid?> KeychainOptions(CredentialKind kind) =>
		kind == CredentialKind.Password ? _keychainPasswords : _keychainKeys;

	/// <summary>True when the edited login already owns a saved credential of <paramref name="kind"/>.</summary>
	private bool HasPrivateCredential(CredentialKind kind) => PrivateCredential is { } credential && credential.Kind == kind;

	/// <summary>The login's own credential; shared keychain entries and credentials owned by another login never count.</summary>
	private CredentialInfo? PrivateCredential =>
		_existingCredential is { IsShared: false } credential
		&& _original is not null
		&& (credential.OwnerConnectionId is null || credential.OwnerConnectionId == _original.Id)
			? credential
			: null;

	private string HostLabel(Guid? hostId) =>
		hostId is { } id && _catalog.FindHost(id) is { } host
			? string.Equals(host.DisplayName, host.Address, StringComparison.Ordinal) ? host.Address : $"{host.DisplayName} ({host.Address})"
			: "Choose a host";

	private string CredentialLabel(Guid? credentialId)
	{
		if (credentialId is not { } id || !_credentialsById.TryGetValue(id, out CredentialInfo? credential))
		{
			return "Choose a saved credential";
		}

		string label = string.IsNullOrWhiteSpace(credential.Username) ? credential.Name : $"{credential.Name} ({credential.Username})";
		return credential.KeyAlgorithm is { } algorithm ? $"{label} · {algorithm}" : label;
	}

	private string ThemeLabel(string themeId) => themeId.Length == 0 ? GlobalThemeText : Themes.Get(themeId).Name;

	protected override async Task LoadAsync(ConnectionEditorRequest request)
	{
		_loading = true;
		_error = null;
		try
		{
			await Catalog.EnsureLoadedAsync();
			IReadOnlyList<CredentialInfo> credentials = await Credentials.ListAsync();
			if (IsCurrent(request))
			{
				Load(request, Catalog.Catalog, credentials);
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError(ex, "Loading the connection editor failed.");
			_error = VaultFailure.Describe(ex, "open this login");
		}
		finally
		{
			if (IsCurrent(request))
			{
				_loading = false;
			}
		}
	}

	private void Load(ConnectionEditorRequest request, ConnectionCatalog catalog, IReadOnlyList<CredentialInfo> credentials)
	{
		_catalog = catalog;
		_hosts = [.. catalog.Hosts.OrderBy(host => host.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
		_hostOptions = [.. _hosts.Select(host => (Guid?)host.Id)];
		_credentialsById = [];
		foreach (CredentialInfo credential in credentials)
		{
			_credentialsById.TryAdd(credential.Id, credential);
		}

		_keychainPasswords = SharedCredentialIds(credentials, CredentialKind.Password);
		_keychainKeys = SharedCredentialIds(credentials, CredentialKind.PrivateKey);
		_themeIds = ["", .. Themes.Themes.Select(theme => theme.Id)];
		ResetFields();

		if (request.ConnectionId is { } connectionId)
		{
			if (catalog.FindConnection(connectionId) is not { } connection)
			{
				_error = "This login no longer exists.";
				return;
			}

			_original = connection;
			_hostId = connection.HostId;
			LoadLogin(connection);
			if (connection.CredentialId is { } credentialId && _credentialsById.TryGetValue(credentialId, out CredentialInfo? credential))
			{
				_existingCredential = credential;
				_useKeychain = credential.IsShared;
				_keychainCredentialId = credential.IsShared ? credentialId : null;
			}
		}
		else if (request.TransientSession is { } session)
		{
			LoadLogin(session.Connection);
			HostProfile? match = _hosts.Find(host => string.Equals(host.Address, session.Host.Address, StringComparison.OrdinalIgnoreCase));
			_hostId = match?.Id;
			_newHost = match is null;
			_newHostAddress = match is null ? session.Host.Address : "";
		}
		else
		{
			_hostId = request.HostId is { } hostId && catalog.FindHost(hostId) is not null ? hostId
				: _hosts.Count == 1 ? _hosts[0].Id
				: null;
			_newHost = _hosts.Count == 0;
			if (Protocols.Protocols is [var protocol, ..])
			{
				_protocolId = protocol.Id;
				_method = protocol.AuthenticationMethods.Count > 0 ? protocol.AuthenticationMethods[0] : AuthenticationMethod.Password;
			}
		}
	}

	private static List<Guid?> SharedCredentialIds(IReadOnlyList<CredentialInfo> credentials, CredentialKind kind) =>
	[
		.. credentials
			.Where(credential => credential.IsShared && credential.Kind == kind)
			.OrderBy(credential => credential.Name, StringComparer.CurrentCultureIgnoreCase)
			.Select(credential => (Guid?)credential.Id),
	];

	private void LoadLogin(ConnectionProfile connection)
	{
		_protocolId = connection.ProtocolId;
		_port = connection.Port?.ToString(CultureInfo.InvariantCulture) ?? "";
		_username = connection.Username ?? "";
		_label = connection.Label ?? "";
		_method = connection.AuthenticationMethod;
		_options = connection.Options;
		_themeChoice = connection.Terminal?.ThemeId ?? "";
		_fontSize = connection.Terminal?.FontSize?.ToString(CultureInfo.InvariantCulture) ?? "";
		_fontFamily = connection.Terminal?.FontFamily ?? "";
	}

	private void ResetFields()
	{
		_saving = false;
		_error = null;
		_original = null;
		_newHost = false;
		_hostId = null;
		_newHostAddress = "";
		_newHostName = "";
		_hostError = null;
		_protocolId = "";
		_defaultPort = null;
		_username = "";
		_port = "";
		_label = "";
		_method = AuthenticationMethod.Password;
		_options = ProtocolOptions.Empty;
		_portError = null;
		_existingCredential = null;
		_useKeychain = false;
		_keychainCredentialId = null;
		_credentialError = null;
		_themeChoice = "";
		_fontSize = "";
		_fontFamily = "";
		_fontSizeError = null;
		_fontFamilyError = null;
		ClearSecrets();
	}

	/// <summary>A closed editor keeps no secret, and neither does one handed a different login.</summary>
	protected override void Reset() => ClearSecrets();

	// Strings cannot be wiped; dropping the references is the most a form field allows.
	private void ClearSecrets()
	{
		_password = "";
		_forgetPassword = false;
		_privateKey = null;
		_passphrase = null;
		_proxyPassword = "";
	}

	private void OnHostModeChanged(string mode)
	{
		_newHost = mode == NewHostMode;
		_hostError = null;
	}

	private void OnProtocolChanged(string protocolId)
	{
		ProtocolDescriptor? previous = Protocol;
		_protocolId = protocolId;
		_portError = null;
		_defaultPort = null;
		if (Protocol is not { } next)
		{
			return;
		}

		if (!next.AuthenticationMethods.Contains(_method) && next.AuthenticationMethods.Count > 0)
		{
			SetMethod(next.AuthenticationMethods[0]);
		}

		// Options belong to a protocol family: SFTP keeps SSH options, FTP starts clean.
		if (!string.Equals(Family(previous), Family(next), StringComparison.OrdinalIgnoreCase))
		{
			_options = ProtocolOptions.Empty;
		}
	}

	private void OnMethodChanged(string value)
	{
		if (Enum.TryParse(value, out AuthenticationMethod method))
		{
			SetMethod(method);
		}
	}

	private void SetMethod(AuthenticationMethod method)
	{
		_method = method;
		_credentialError = null;
		if (CredentialKindFor(method) is not { } kind)
		{
			return;
		}

		if (_keychainCredentialId is { } id && _credentialsById.TryGetValue(id, out CredentialInfo? credential) && credential.Kind != kind)
		{
			_keychainCredentialId = null;
		}

		if (KeychainOptions(kind).Count == 0)
		{
			_useKeychain = false;
		}
	}

	private void OnCredentialSourceChanged(string source)
	{
		_useKeychain = source == KeychainSource;
		_credentialError = null;
	}

	private void OnKeychainCredentialChanged(Guid? credentialId)
	{
		_keychainCredentialId = credentialId;
		_credentialError = null;
		if (string.IsNullOrWhiteSpace(_username)
			&& credentialId is { } id
			&& _credentialsById.TryGetValue(id, out CredentialInfo? credential)
			&& !string.IsNullOrWhiteSpace(credential.Username))
		{
			_username = credential.Username;
		}
	}

	private void OnOptionsChanged(ProtocolOptions options) => _options = options;

	private void OnProxyPasswordChanged(string? proxyPassword) => _proxyPassword = proxyPassword;

	/// <summary>The proxy password to write: null keeps what is stored, so an untouched field changes nothing.</summary>
	private string? TypedProxyPassword => CanSaveProxyPassword ? _proxyPassword : null;

	private CredentialSecretInput? ProxyPasswordOnly() =>
		TypedProxyPassword is { } proxyPassword ? new CredentialSecretInput { ProxyPassword = proxyPassword } : null;

	private async Task SaveAsync(bool connect)
	{
		if (Request is not { } request || _saving || _loading || (request.ConnectionId is not null && _original is null))
		{
			return;
		}

		if (!Validate(out int? port, out double? fontSize))
		{
			return;
		}

		_saving = true;
		_error = null;
		Guid connectionId = _original?.Id ?? Guid.NewGuid();
		string username = _username.Trim();
		CredentialChange change = default;
		try
		{
			change = await SaveCredentialAsync(connectionId, username);
			Guid hostId = await SaveNewHostAsync()
				?? SelectedHost?.Id
				?? throw new InvalidOperationException("The selected host no longer exists.");

			ConnectionProfile connection = (_original ?? new ConnectionProfile { Id = connectionId, HostId = hostId, ProtocolId = _protocolId }) with
			{
				HostId = hostId,
				ProtocolId = _protocolId,
				Port = port,
				Username = NullIfBlank(username),
				Label = NullIfBlank(_label),
				AuthenticationMethod = _method,
				CredentialId = change.CredentialId,
				Options = _options,
				Terminal = BuildTerminalOverrides(fontSize),
			};

			await Repository.SaveConnectionAsync(connection);
			if (change.StaleCredentialId is { } stale)
			{
				await DeleteCredentialAsync(stale);
			}

			ClearSecrets();
			_saving = false;
			CloseSaved();

			// Saving a quick-connect tab makes that tab the login's own, so it is not left as a throwaway beside it.
			bool adopted = request.TransientSession is { } open && await Sessions.AdoptAsync(open.Id, connection.Id);
			if (connect && !adopted)
			{
				await Sessions.OpenAsync(connection.Id);
			}
		}
		catch (CredentialValidationException ex)
		{
			_credentialError = ex.Message;
			_saving = false;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError(ex, "Saving a login failed.");
			if (change.CreatedCredentialId is { } created)
			{
				await DeleteCredentialAsync(created);
			}

			_error = VaultFailure.Describe(ex, "save this login");
			_saving = false;
		}
	}

	private bool Validate(out int? port, out double? fontSize)
	{
		port = null;
		fontSize = null;
		_error = Protocol is null ? "Choose a protocol." : null;
		_hostError = _newHost
			? HostAddress.Validate(_newHostAddress)
			: SelectedHost is null ? "Choose a host." : null;

		_portError = null;
		string portText = ShowPort ? _port.Trim() : "";
		if (portText.Length > 0)
		{
			if (int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedPort) && parsedPort is >= 1 and <= 65535)
			{
				port = parsedPort;
			}
			else
			{
				_portError = "Use a port from 1 to 65535, or leave it empty for the default.";
			}
		}

		_fontSizeError = null;
		string fontSizeText = _fontSize.Trim();
		if (fontSizeText.Length > 0 && Protocol?.Has(ProtocolCapabilities.Terminal) == true)
		{
			// The same range as the setting this overrides: a size the settings page refuses must not reach a terminal
			// through a saved login either.
			if (double.TryParse(fontSizeText, NumberStyles.Float, CultureInfo.InvariantCulture, out double size)
				&& size >= TerminalSettings.MinFontSize
				&& size <= TerminalSettings.MaxFontSize)
			{
				fontSize = size;
			}
			else
			{
				_fontSizeError = FontSizeRequirement;
			}
		}

		_fontFamilyError = _fontFamily.Trim().Length > 0 && Protocol?.Has(ProtocolCapabilities.Terminal) == true && !TerminalFontFamily.IsValid(_fontFamily)
			? TerminalFontFamily.Requirement
			: null;

		_credentialError = CredentialKindFor(_method) switch
		{
			{ } when _useKeychain && (_keychainCredentialId is not { } keychainId || !_credentialsById.ContainsKey(keychainId)) =>
				"Choose a keychain entry.",
			CredentialKind.PrivateKey when !_useKeychain && string.IsNullOrWhiteSpace(_privateKey) && !HasPrivateCredential(CredentialKind.PrivateKey) =>
				"Paste or import a private key.",
			_ => null,
		};

		return _error is null && _hostError is null && _portError is null && _fontSizeError is null && _fontFamilyError is null
			&& _credentialError is null;
	}

	/// <summary>
	/// Applies the credential choice. A credential this login owns is updated in place when it keeps its kind; one it no
	/// longer uses is reported as stale and deleted only after the login saved, so a failed save never loses it.
	/// </summary>
	private async Task<CredentialChange> SaveCredentialAsync(Guid connectionId, string username)
	{
		CredentialInfo? owned = PrivateCredential;
		if (CredentialKindFor(_method) is not { } kind)
		{
			return new CredentialChange(null, owned?.Id, null);
		}

		if (_useKeychain)
		{
			return new CredentialChange(_keychainCredentialId, owned?.Id, null);
		}

		string address = _newHost ? HostAddress.Normalize(_newHostAddress) : SelectedHost?.Address ?? "";
		string name = username.Length == 0 ? address : $"{username}@{address}";

		if (owned is not null && owned.Kind == kind)
		{
			if (kind == CredentialKind.Password && _forgetPassword)
			{
				return new CredentialChange(null, owned.Id, null);
			}

			CredentialSecretInput? secret = kind == CredentialKind.Password
				? _password.Length > 0 ? new CredentialSecretInput { Password = _password, ProxyPassword = TypedProxyPassword } : ProxyPasswordOnly()
				: !string.IsNullOrWhiteSpace(_privateKey) ? new CredentialSecretInput { PrivateKey = _privateKey, Passphrase = _passphrase ?? "", ProxyPassword = TypedProxyPassword }
				: !string.IsNullOrEmpty(_passphrase) ? new CredentialSecretInput { Passphrase = _passphrase, ProxyPassword = TypedProxyPassword }
				: ProxyPasswordOnly();

			CredentialInfo updated = owned with { Name = name, Username = NullIfBlank(username), OwnerConnectionId = connectionId };
			if (secret is not null || updated != owned)
			{
				await Credentials.SaveAsync(updated, secret);
			}

			return new CredentialChange(owned.Id, null, null);
		}

		// An empty password on a login without one means "ask when connecting": nothing to store.
		if (kind == CredentialKind.Password && _password.Length == 0)
		{
			return new CredentialChange(null, owned?.Id, null);
		}

		CredentialSecretInput input = kind == CredentialKind.Password
			? new CredentialSecretInput { Password = _password, ProxyPassword = TypedProxyPassword }
			: new CredentialSecretInput { PrivateKey = _privateKey, Passphrase = string.IsNullOrEmpty(_passphrase) ? null : _passphrase, ProxyPassword = TypedProxyPassword };

		CredentialInfo created = await Credentials.SaveAsync(
			new CredentialInfo
			{
				Id = Guid.NewGuid(),
				Name = name,
				Kind = kind,
				Username = NullIfBlank(username),
				IsShared = false,
				OwnerConnectionId = connectionId,
			},
			input);

		return new CredentialChange(created.Id, owned?.Id, created.Id);
	}

	private async Task<Guid?> SaveNewHostAsync()
	{
		if (!_newHost)
		{
			return null;
		}

		HostProfile host = new()
		{
			Id = Guid.NewGuid(),
			Address = HostAddress.Normalize(_newHostAddress),
			Name = _newHostName.Trim(),
		};

		await Repository.SaveHostAsync(host);
		return host.Id;
	}

	private TerminalProfileOverrides? BuildTerminalOverrides(double? fontSize)
	{
		if (Protocol?.Has(ProtocolCapabilities.Terminal) != true)
		{
			return null;
		}

		TerminalProfileOverrides overrides = new()
		{
			ThemeId = NullIfBlank(_themeChoice),
			FontSize = fontSize,
			FontFamily = NullIfBlank(_fontFamily),
		};

		return overrides.IsEmpty ? null : overrides;
	}

	private async Task DeleteCredentialAsync(Guid credentialId)
	{
		try
		{
			await Credentials.DeleteAsync(credentialId);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogWarning(ex, "Deleting credential {CredentialId} failed.", credentialId);
		}
	}

	/// <summary>The credential a saved login should reference, and credentials to delete after or instead of saving it.</summary>
	private readonly record struct CredentialChange(Guid? CredentialId, Guid? StaleCredentialId, Guid? CreatedCredentialId);
}
