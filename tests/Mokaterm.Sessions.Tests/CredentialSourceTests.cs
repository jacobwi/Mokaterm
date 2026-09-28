using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Sessions.Tests.Fakes;

namespace Mokaterm.Sessions.Tests;

public sealed class CredentialSourceTests
{
	private static readonly HostProfile Host = new() { Id = Guid.NewGuid(), Address = "10.0.0.5" };

	private readonly FakeCredentialStore _store = new();
	private readonly FakeConnectionRepository _repository = new();
	private readonly FakeUserInteraction _interaction = new();

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task GetAsync_SavedPassword_FallsBackToTheCredentialUsername()
	{
		CredentialInfo credential = _store.Add(Credential(CredentialKind.Password, username: "bob"), new CredentialSecretInput { Password = "hunter2" });
		using CredentialSource source = CreateSource(AddConnection(null, AuthenticationMethod.Password, credential.Id));

		using LoginCredentials? login = await source.GetAsync(Ct);

		Assert.NotNull(login);
		Assert.Equal("bob", login.Username);
		Assert.Equal(AuthenticationMethod.Password, login.Method);
		Assert.Equal("hunter2", login.Password?.RevealString());
		Assert.Null(login.PrivateKey);
		Assert.Empty(_interaction.CredentialPrompts);
	}

	[Fact]
	public async Task GetAsync_SavedKey_FillsKeyAndPassphrase_AndPrefersTheConnectionUsername()
	{
		CredentialInfo credential = _store.Add(
			Credential(CredentialKind.PrivateKey, username: "someone-else"),
			new CredentialSecretInput { PrivateKey = "-----BEGIN OPENSSH PRIVATE KEY-----", Passphrase = "correct horse" });
		using CredentialSource source = CreateSource(AddConnection("alice", AuthenticationMethod.PublicKey, credential.Id));

		using LoginCredentials? login = await source.GetAsync(Ct);

		Assert.NotNull(login);
		Assert.Equal("alice", login.Username);
		Assert.Equal(AuthenticationMethod.PublicKey, login.Method);
		Assert.Equal("-----BEGIN OPENSSH PRIVATE KEY-----", login.PrivateKey?.RevealString());
		Assert.Equal("correct horse", login.Passphrase?.RevealString());
		Assert.Null(login.Password);
	}

	[Fact]
	public async Task GetAsync_SavedProxyPassword_ReachesTheLoginForEitherKind()
	{
		CredentialInfo password = _store.Add(
			Credential(CredentialKind.Password, username: "bob"),
			new CredentialSecretInput { Password = "hunter2", ProxyPassword = "proxy-secret" });
		CredentialInfo key = _store.Add(
			Credential(CredentialKind.PrivateKey, username: "bob"),
			new CredentialSecretInput { PrivateKey = "KEY", ProxyPassword = "proxy-secret" });

		using CredentialSource passwordSource = CreateSource(AddConnection(null, AuthenticationMethod.Password, password.Id));
		using CredentialSource keySource = CreateSource(AddConnection(null, AuthenticationMethod.PublicKey, key.Id));
		using LoginCredentials? passwordLogin = await passwordSource.GetAsync(Ct);
		using LoginCredentials? keyLogin = await keySource.GetAsync(Ct);

		Assert.Equal("proxy-secret", passwordLogin?.ProxyPassword?.RevealString());
		Assert.Equal("proxy-secret", keyLogin?.ProxyPassword?.RevealString());
	}

	[Fact]
	public async Task GetAsync_WithoutASavedProxyPassword_LeavesItUnset()
	{
		CredentialInfo credential = _store.Add(Credential(CredentialKind.Password, username: "bob"), new CredentialSecretInput { Password = "hunter2" });
		using CredentialSource source = CreateSource(AddConnection(null, AuthenticationMethod.Password, credential.Id));

		using LoginCredentials? login = await source.GetAsync(Ct);

		Assert.Null(login?.ProxyPassword);
	}

	[Fact]
	public async Task GetAsync_SavedKeyWithoutAnyUsername_PromptsAndUsesOnlyTheTypedUsername()
	{
		CredentialInfo credential = _store.Add(Credential(CredentialKind.PrivateKey), new CredentialSecretInput { PrivateKey = "KEY" });
		using CredentialSource source = CreateSource(AddConnection(null, AuthenticationMethod.PublicKey, credential.Id));
		_interaction.AnswerCredentials("carol", "typed-password", save: true);

		using LoginCredentials? login = await source.GetAsync(Ct);

		Assert.False(Assert.Single(_interaction.CredentialPrompts).OfferSave);
		Assert.NotNull(login);
		Assert.Equal("carol", login.Username);
		Assert.Equal("KEY", login.PrivateKey?.RevealString());
		Assert.Null(login.Password);
	}

	[Fact]
	public async Task GetAsync_WithoutCredential_PromptsAndSavesAPrivateCredentialOnceAccepted()
	{
		ConnectionProfile connection = AddConnection(null, AuthenticationMethod.Password);
		using CredentialSource source = CreateSource(connection);
		_interaction.AnswerCredentials("dave", "s3cret", save: true);

		using LoginCredentials? login = await source.GetAsync(Ct);

		CredentialPrompt prompt = Assert.Single(_interaction.CredentialPrompts);
		Assert.Equal("Log in to 10.0.0.5", prompt.Title);
		Assert.True(prompt.AllowUsernameEdit);
		Assert.True(prompt.OfferSave);
		Assert.NotNull(login);
		Assert.Equal("dave", login.Username);
		Assert.Equal("s3cret", login.Password?.RevealString());
		Assert.Empty(await _store.ListAsync(Ct));

		await source.SavePendingAsync(Ct);

		CredentialInfo saved = Assert.Single(await _store.ListAsync(Ct));
		Assert.False(saved.IsShared);
		Assert.Equal(connection.Id, saved.OwnerConnectionId);
		Assert.Equal("dave@10.0.0.5", saved.Name);
		Assert.Equal(CredentialKind.Password, saved.Kind);
		Assert.Equal("s3cret", _store.SecretOf(saved.Id)?.Password);
		ConnectionProfile updated = Assert.Single(_repository.SavedConnections);
		Assert.Equal(saved.Id, updated.CredentialId);
		Assert.Equal("dave", updated.Username);
	}

	[Fact]
	public async Task GetAsync_PromptForAConnectionWithAUsername_KeepsTheUsernameFixed()
	{
		using CredentialSource source = CreateSource(AddConnection("erin", AuthenticationMethod.KeyboardInteractive));
		_interaction.AnswerCredentials("mallory", "pw");

		using LoginCredentials? login = await source.GetAsync(Ct);

		CredentialPrompt prompt = Assert.Single(_interaction.CredentialPrompts);
		Assert.Equal("Log in to erin@10.0.0.5", prompt.Title);
		Assert.Equal("erin", prompt.Username);
		Assert.False(prompt.AllowUsernameEdit);
		Assert.Equal("erin", login?.Username);
		Assert.Equal(AuthenticationMethod.KeyboardInteractive, login?.Method);
	}

	[Fact]
	public async Task GetAsync_TransientConnection_NeverOffersOrSaves()
	{
		using CredentialSource source = CreateSource(AddConnection("frank", AuthenticationMethod.Password), isTransient: true);
		_interaction.AnswerCredentials("frank", "pw", save: true);

		using LoginCredentials? login = await source.GetAsync(Ct);
		await source.SavePendingAsync(Ct);

		Assert.NotNull(login);
		Assert.False(Assert.Single(_interaction.CredentialPrompts).OfferSave);
		Assert.Empty(await _store.ListAsync(Ct));
		Assert.Empty(_repository.SavedConnections);
	}

	[Fact]
	public async Task RetryAsync_PasswordMethod_PromptsAgainWithTheReason()
	{
		using CredentialSource source = CreateSource(AddConnection(null, AuthenticationMethod.Password));
		_interaction.AnswerCredentials("gina", "wrong");
		_interaction.AnswerCredentials("gina", "right");

		using LoginCredentials? first = await source.GetAsync(Ct);
		using LoginCredentials? second = await source.RetryAsync("Authentication failed. Try again.", Ct);

		Assert.Equal(2, _interaction.CredentialPrompts.Count);
		CredentialPrompt retry = _interaction.CredentialPrompts[1];
		Assert.Equal("Authentication failed. Try again.", retry.Message);
		Assert.Equal("gina", retry.Username);
		Assert.Equal("right", second?.Password?.RevealString());
	}

	[Fact]
	public async Task RetryAsync_KeyMethod_ReturnsNullWithoutPrompting()
	{
		CredentialInfo credential = _store.Add(Credential(CredentialKind.PrivateKey), new CredentialSecretInput { PrivateKey = "KEY" });
		using CredentialSource source = CreateSource(AddConnection("hank", AuthenticationMethod.PublicKey, credential.Id));

		using LoginCredentials? retry = await source.RetryAsync("Authentication failed.", Ct);

		Assert.Null(retry);
		Assert.Empty(_interaction.CredentialPrompts);
	}

	[Fact]
	public async Task GetAsync_PublicKeyWithoutCredential_FailsAuthentication()
	{
		using CredentialSource source = CreateSource(AddConnection("ivy", AuthenticationMethod.PublicKey));

		ProtocolConnectException error = await Assert.ThrowsAsync<ProtocolConnectException>(async () => await source.GetAsync(Ct));

		Assert.Equal(ConnectFailure.AuthenticationFailed, error.Failure);
		Assert.Equal("No private key is set for this connection. Edit the connection and choose a key.", error.Message);
	}

	[Fact]
	public async Task GetAsync_Anonymous_UsesTheAnonymousUsernameWithoutSecrets()
	{
		using CredentialSource source = CreateSource(AddConnection(null, AuthenticationMethod.Anonymous), protocol: TestProtocols.Ftp);

		using LoginCredentials? login = await source.GetAsync(Ct);

		Assert.NotNull(login);
		Assert.Equal("anonymous", login.Username);
		Assert.Null(login.Password);
		Assert.Empty(_interaction.CredentialPrompts);
	}

	[Fact]
	public async Task SavePendingAsync_WhenTheStoreFails_NotifiesInsteadOfThrowing()
	{
		using CredentialSource source = CreateSource(AddConnection("jack", AuthenticationMethod.Password));
		_interaction.AnswerCredentials("jack", "pw", save: true);
		_store.SaveError = new InvalidOperationException("The vault is locked.");

		using LoginCredentials? login = await source.GetAsync(Ct);
		await source.SavePendingAsync(Ct);

		Assert.NotNull(login);
		Notice notice = Assert.Single(_interaction.Notices);
		Assert.Equal(NoticeSeverity.Warning, notice.Severity);
		Assert.Contains("The vault is locked.", notice.Message, StringComparison.Ordinal);
		Assert.Empty(_repository.SavedConnections);
	}

	[Fact]
	public async Task GetAsync_CancelledPrompt_ReturnsNull()
	{
		using CredentialSource source = CreateSource(AddConnection(null, AuthenticationMethod.Password));

		Assert.Null(await source.GetAsync(Ct));
		_ = Assert.Single(_interaction.CredentialPrompts);
	}

	private static CredentialInfo Credential(CredentialKind kind, string? username = null) => new()
	{
		Id = Guid.NewGuid(),
		Name = "test credential",
		Kind = kind,
		Username = username,
		IsShared = true,
	};

	private ConnectionProfile AddConnection(string? username, AuthenticationMethod method, Guid? credentialId = null)
	{
		ConnectionProfile connection = new()
		{
			Id = Guid.NewGuid(),
			HostId = Host.Id,
			ProtocolId = "ssh",
			Username = username,
			AuthenticationMethod = method,
			CredentialId = credentialId,
		};

		_repository.Add(Host, connection);
		return connection;
	}

	private CredentialSource CreateSource(ConnectionProfile connection, bool isTransient = false, ProtocolDescriptor? protocol = null) =>
		new(connection, Host, protocol ?? TestProtocols.Ssh, isTransient, _store, _repository, _interaction, NullLogger.Instance);
}
