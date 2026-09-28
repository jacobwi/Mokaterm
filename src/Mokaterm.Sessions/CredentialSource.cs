using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Sessions;

/// <summary>
/// Login material for one connect attempt: the connection's saved credential, or a prompt when there is none. A
/// typed password the user asked to save stays in pinned memory until <see cref="SavePendingAsync"/> runs after
/// the server accepted it, so a mistyped password is never stored.
/// </summary>
internal sealed class CredentialSource : ICredentialSource, IDisposable
{
	private const string AnonymousUsername = "anonymous";

	private readonly ConnectionProfile _connection;
	private readonly HostProfile _host;
	private readonly ProtocolDescriptor _protocol;
	private readonly bool _isTransient;
	private readonly ICredentialStore _store;
	private readonly IConnectionRepository _repository;
	private readonly IUserInteraction _interaction;
	private readonly ILogger _logger;
	private readonly Lock _lock = new();
	private string? _lastUsername;
	private string? _pendingUsername;
	private SecretBuffer? _pendingPassword;

	public CredentialSource(
		ConnectionProfile connection,
		HostProfile host,
		ProtocolDescriptor protocol,
		bool isTransient,
		ICredentialStore store,
		IConnectionRepository repository,
		IUserInteraction interaction,
		ILogger logger)
	{
		_connection = connection;
		_host = host;
		_protocol = protocol;
		_isTransient = isTransient;
		_store = store;
		_repository = repository;
		_interaction = interaction;
		_logger = logger;
	}

	public string? Username => Method == AuthenticationMethod.Anonymous ? SavedUsername ?? AnonymousUsername : SavedUsername;

	public AuthenticationMethod Method => _connection.AuthenticationMethod;

	private string? SavedUsername => NullIfBlank(_connection.Username);

	/// <summary>Saving applies to saved connections that have no credential yet.</summary>
	private bool CanSave => !_isTransient && _connection.CredentialId is null;

	public async ValueTask<LoginCredentials?> GetAsync(CancellationToken cancellationToken)
	{
		if (Method == AuthenticationMethod.Anonymous)
		{
			return new LoginCredentials { Username = SavedUsername ?? AnonymousUsername, Method = Method };
		}

		if (_connection.CredentialId is Guid credentialId)
		{
			return await GetSavedAsync(credentialId, cancellationToken);
		}

		return Method switch
		{
			AuthenticationMethod.Password or AuthenticationMethod.KeyboardInteractive => await PromptPasswordAsync(null, cancellationToken),
			AuthenticationMethod.PublicKey => throw new ProtocolConnectException(
				ConnectFailure.AuthenticationFailed,
				"No private key is set for this connection. Edit the connection and choose a key."),
			_ => await GetWithoutSecretsAsync(cancellationToken),
		};
	}

	public async ValueTask<LoginCredentials?> RetryAsync(string reason, CancellationToken cancellationToken) =>
		Method is AuthenticationMethod.Password or AuthenticationMethod.KeyboardInteractive
			? await PromptPasswordAsync(reason, cancellationToken)
			: null;

	/// <summary>
	/// Stores the password the user asked to save as a private credential of the connection and points the connection
	/// at it. Call once the server accepted the login. Failures become a notice; they never fail the session.
	/// </summary>
	internal async Task SavePendingAsync(CancellationToken cancellationToken)
	{
		string username;
		SecretBuffer password;
		lock (_lock)
		{
			if (_pendingUsername is null || _pendingPassword is null)
			{
				return;
			}

			username = _pendingUsername;
			password = _pendingPassword;
			_pendingUsername = null;
			_pendingPassword = null;
		}

		using SecretBuffer owned = password;
		try
		{
			ConnectionCatalog catalog = await _repository.GetCatalogAsync(cancellationToken);

			// Read the latest profile so edits made while the session was connecting are kept. A connection deleted or
			// given a credential in the meantime is left alone.
			ConnectionProfile? current = catalog.FindConnection(_connection.Id);
			if (current is null || current.CredentialId is not null)
			{
				return;
			}

			CredentialInfo info = new()
			{
				Id = Guid.NewGuid(),
				Name = $"{username}@{_host.Address}",
				Kind = CredentialKind.Password,
				Username = username,
				IsShared = false,
				OwnerConnectionId = current.Id,
			};

			CredentialInfo saved = await _store.SaveAsync(info, new CredentialSecretInput { Password = owned.RevealString() }, cancellationToken);
			ConnectionProfile updated = current with
			{
				CredentialId = saved.Id,
				Username = string.IsNullOrWhiteSpace(current.Username) ? username : current.Username,
			};

			await _repository.SaveConnectionAsync(updated, cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Saving the password for connection {ConnectionId} failed.", _connection.Id);
			_interaction.Notify(NoticeSeverity.Warning, $"The password was not saved. {ex.Message}", "Save password");
		}
	}

	public void Dispose()
	{
		lock (_lock)
		{
			_pendingPassword?.Dispose();
			_pendingPassword = null;
			_pendingUsername = null;
		}
	}

	private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

	private async ValueTask<LoginCredentials?> GetSavedAsync(Guid credentialId, CancellationToken cancellationToken)
	{
		CredentialInfo info = await _store.FindAsync(credentialId, cancellationToken)
			?? throw new ProtocolConnectException(
				ConnectFailure.AuthenticationFailed,
				"The saved credential for this connection no longer exists. Edit the connection and choose another.");

		SecretBuffer? typedPassword = null;
		try
		{
			string? username = SavedUsername ?? NullIfBlank(info.Username);
			if (username is null && !_protocol.RequiresUsername)
			{
				username = "";
			}

			if (username is null)
			{
				bool isKey = info.Kind == CredentialKind.PrivateKey;
				CredentialPromptResult? typed = await _interaction.PromptCredentialsAsync(
					new CredentialPrompt
					{
						Title = LoginTitle(null),
						Message = isKey ? "Enter the username for the saved key." : "Enter the username. Leave the password empty to use the saved one.",
						OfferSave = false,
					},
					cancellationToken);
				if (typed is null)
				{
					return null;
				}

				username = typed.Username.Trim();
				if (!isKey && typed.Password.Length > 0)
				{
					typedPassword = SecretBuffer.FromString(typed.Password);
				}
			}

			using CredentialSecret secret = await _store.RevealAsync(credentialId, cancellationToken);

			// The proxy password belongs to the connection's route, not to the login method, so both kinds carry it.
			LoginCredentials credentials = secret.Kind == CredentialKind.PrivateKey
				? new LoginCredentials
				{
					Username = username,
					Method = Method,
					PrivateKey = secret.PrivateKey?.Copy(),
					Passphrase = secret.Passphrase?.Copy(),
					ProxyPassword = secret.ProxyPassword?.Copy(),
				}
				: new LoginCredentials
				{
					Username = username,
					Method = Method,
					Password = typedPassword ?? secret.Password?.Copy(),
					ProxyPassword = secret.ProxyPassword?.Copy(),
				};

			if (ReferenceEquals(credentials.Password, typedPassword))
			{
				typedPassword = null;
			}

			RememberUsername(username);
			return credentials;
		}
		finally
		{
			typedPassword?.Dispose();
		}
	}

	private async ValueTask<LoginCredentials?> PromptPasswordAsync(string? message, CancellationToken cancellationToken)
	{
		string? savedUsername = SavedUsername;
		string? suggested = savedUsername ?? LastUsername();
		CredentialPromptResult? result = await _interaction.PromptCredentialsAsync(
			new CredentialPrompt
			{
				Title = LoginTitle(savedUsername),
				Message = message,
				Username = suggested,
				AllowUsernameEdit = savedUsername is null,
				RequireUsername = _protocol.RequiresUsername,
				OfferSave = CanSave,
			},
			cancellationToken);
		if (result is null)
		{
			return null;
		}

		string username = savedUsername ?? result.Username.Trim();
		SecretBuffer password = SecretBuffer.FromString(result.Password);
		lock (_lock)
		{
			_lastUsername = username;
			_pendingPassword?.Dispose();
			_pendingPassword = result.Save && CanSave ? password.Copy() : null;
			_pendingUsername = _pendingPassword is null ? null : username;
		}

		return new LoginCredentials { Username = username, Method = Method, Password = password };
	}

	/// <summary>Methods without secrets of their own (an SSH agent): only the username is needed.</summary>
	private async ValueTask<LoginCredentials?> GetWithoutSecretsAsync(CancellationToken cancellationToken)
	{
		string? username = SavedUsername ?? (_protocol.RequiresUsername ? null : "");
		if (username is null)
		{
			CredentialPromptResult? typed = await _interaction.PromptCredentialsAsync(
				new CredentialPrompt { Title = LoginTitle(null), Message = "Enter the username.", OfferSave = false },
				cancellationToken);
			if (typed is null)
			{
				return null;
			}

			username = typed.Username.Trim();
		}

		RememberUsername(username);
		return new LoginCredentials { Username = username, Method = Method };
	}

	private string LoginTitle(string? username) =>
		username is null ? $"Log in to {_host.Address}" : $"Log in to {username}@{_host.Address}";

	private string? LastUsername()
	{
		lock (_lock)
		{
			return _lastUsername;
		}
	}

	private void RememberUsername(string username)
	{
		lock (_lock)
		{
			_lastUsername = username;
		}
	}
}
