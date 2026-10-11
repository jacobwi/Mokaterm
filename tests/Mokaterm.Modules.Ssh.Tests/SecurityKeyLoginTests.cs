using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Core.Extensions;
using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.Keys;
using Renci.SshNet;
using LoginMethod = Mokaterm.Abstractions.Connections.AuthenticationMethod;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>
/// Security key files on their way into a login: the keychain's validation, and the connector, which reads the file
/// through Core and refuses it where no authenticator can reach the key, before anything is dialled.
/// </summary>
public sealed class SecurityKeyLoginTests : IDisposable
{
	private readonly SoftwareSecurityKey _device = new();
	private readonly ServiceProvider _services;

	public SecurityKeyLoginTests()
	{
		ServiceCollection services = new();
		services.AddMokatermCore();
		services.AddSingleton<ISecurityKeyAuthenticator>(_device);
		_services = services.BuildServiceProvider();
	}

	private ISshSecurityKeyReader Reader => _services.GetRequiredService<ISshSecurityKeyReader>();

	public void Dispose()
	{
		_services.Dispose();
		_device.Dispose();
	}

	[Theory]
	[InlineData(SshKeyType.EcdsaSecurityKey)]
	[InlineData(SshKeyType.Ed25519SecurityKey)]
	public async Task Inspector_ShowsASecurityKeyFileAsItsOwnType(SshKeyType type)
	{
		GeneratedSshKey key = await Generate(type, passphrase: null);

		PrivateKeyInspection inspection = new SshPrivateKeyInspector(Reader).Inspect(key.PrivateKey, null);

		Assert.Equal(PrivateKeyStatus.Valid, inspection.Status);
		Assert.Equal(key.PublicKey.Algorithm, inspection.Algorithm);
		Assert.Equal(key.PublicKey.Fingerprint, inspection.Fingerprint);
		Assert.True(SecurityKeyTypes.IsSecurityKey(inspection.Algorithm));
	}

	[Fact]
	public async Task Inspector_AsksForThePassphraseOfAnEncryptedOne()
	{
		GeneratedSshKey key = await Generate(SshKeyType.EcdsaSecurityKey, "pass phrase");
		SshPrivateKeyInspector inspector = new(Reader);

		Assert.Equal(PrivateKeyStatus.PassphraseRequired, inspector.Inspect(key.PrivateKey, null).Status);
		Assert.Equal(PrivateKeyStatus.WrongPassphrase, inspector.Inspect(key.PrivateKey, "nope").Status);
		Assert.Equal(PrivateKeyStatus.Valid, inspector.Inspect(key.PrivateKey, "pass phrase").Status);
	}

	[Fact]
	public async Task Inspector_WithoutCoresReaderSaysItCannotReadOne()
	{
		GeneratedSshKey key = await Generate(SshKeyType.EcdsaSecurityKey, passphrase: null);

		Assert.Equal(PrivateKeyStatus.Unsupported, new SshPrivateKeyInspector().Inspect(key.PrivateKey, null).Status);
	}

	[Fact]
	public async Task IsSecurityKey_ReadsThePublicHalfWithoutThePassphrase()
	{
		GeneratedSshKey key = await Generate(SshKeyType.Ed25519SecurityKey, "pass phrase");

		Assert.True(SshPrivateKeys.IsSecurityKey(Encoding.UTF8.GetBytes(key.PrivateKey)));
		Assert.False(SshPrivateKeys.IsSecurityKey(Encoding.UTF8.GetBytes(TestKeys.Ed25519)));
	}

	[Fact(Timeout = TestTimeouts.NetworkMilliseconds)]
	public async Task Connect_RefusesASecurityKeyWhereNoAuthenticatorCanReachIt()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		GeneratedSshKey key = await Generate(SshKeyType.EcdsaSecurityKey, passphrase: null);
		SshConnector connector = new(new FakeSettingsService(), securityKeyReader: Reader);

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(() =>
			connector.ConnectAsync(Context(key.PrivateKey, LoopbackPort.Free(), new ThrowingInteraction()), static info => new SshClient(info), ct));

		Assert.Equal(ConnectFailure.AuthenticationFailed, failure.Failure);
		Assert.Contains("desktop app", failure.Message, StringComparison.Ordinal);
	}

	[Fact(Timeout = TestTimeouts.NetworkMilliseconds)]
	public async Task Connect_RefusesASecurityKeyWhenThePlatformHasNoAuthenticatorApi()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		GeneratedSshKey key = await Generate(SshKeyType.EcdsaSecurityKey, passphrase: null);
		using SoftwareSecurityKey unavailable = new() { IsAvailable = false };
		SshConnector connector = new(new FakeSettingsService(), securityKeyReader: Reader, securityKeys: unavailable);

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(() =>
			connector.ConnectAsync(Context(key.PrivateKey, LoopbackPort.Free(), new ThrowingInteraction()), static info => new SshClient(info), ct));

		Assert.Contains("no way to reach one", failure.Message, StringComparison.Ordinal);
	}

	[Fact(Timeout = TestTimeouts.NetworkMilliseconds)]
	public async Task Connect_AsksForTheFilesPassphraseThenDials()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		GeneratedSshKey key = await Generate(SshKeyType.Ed25519SecurityKey, "pass phrase");
		SshConnector connector = new(new FakeSettingsService(), securityKeyReader: Reader, securityKeys: _device);
		PassphraseInteraction interaction = new("pass phrase");

		// Nothing listens on the port, so the attempt ends at the dial: the key was read and the login was built.
		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(() =>
			connector.ConnectAsync(Context(key.PrivateKey, LoopbackPort.Free(), interaction), static info => new SshClient(info), ct));

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.Equal(1, interaction.Prompts);
		Assert.Empty(_device.Assertions);
	}

	[Fact(Timeout = TestTimeouts.NetworkMilliseconds)]
	public async Task Module_HandsTheReaderAndTheAuthenticatorToItsServices()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		GeneratedSshKey key = await Generate(SshKeyType.EcdsaSecurityKey, passphrase: null);
		ServiceCollection collection = new();
		collection.AddSingleton<ISettingsService>(new FakeSettingsService());
		collection.AddMokatermCore();
		collection.AddSingleton<ISecurityKeyAuthenticator>(_device);
		new SshModule().ConfigureServices(collection);
		await using ServiceProvider services = collection.BuildServiceProvider();

		PrivateKeyInspection inspection = services.GetRequiredService<IPrivateKeyInspector>().Inspect(key.PrivateKey, null);
		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(() =>
			services.GetRequiredService<SshConnector>().ConnectAsync(
				Context(key.PrivateKey, LoopbackPort.Free(), new ThrowingInteraction()),
				static info => new SshClient(info),
				ct));

		// Valid only with Core's reader, and a dial instead of "desktop app only" only with the authenticator.
		Assert.Equal(PrivateKeyStatus.Valid, inspection.Status);
		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
	}

	private static ProtocolConnectContext Context(string privateKey, int port, IUserInteraction interaction) => new ConnectContext
	{
		ProtocolId = SshProtocolIds.Ssh,
		Port = port,
		Credentials = new KeyCredentialSource("me", privateKey),
		Verifier = new RefusingHostVerifier(),
		Interaction = interaction,
	}.Build();

	private Task<GeneratedSshKey> Generate(SshKeyType type, string? passphrase) =>
		_services.GetRequiredService<ISshKeyGenerator>().GenerateAsync(
			new SshKeyRequest { Type = type, Comment = "me@box", Passphrase = passphrase },
			TestContext.Current.CancellationToken);

	/// <summary>A saved key login with no passphrase saved and no retries.</summary>
	private sealed class KeyCredentialSource(string username, string privateKey) : ICredentialSource
	{
		public string? Username => username;

		public LoginMethod Method => LoginMethod.PublicKey;

		public ValueTask<LoginCredentials?> GetAsync(CancellationToken cancellationToken) =>
			ValueTask.FromResult<LoginCredentials?>(new LoginCredentials
			{
				Username = username,
				Method = LoginMethod.PublicKey,
				PrivateKey = SecretBuffer.FromString(privateKey),
			});

		public ValueTask<LoginCredentials?> RetryAsync(string reason, CancellationToken cancellationToken) =>
			ValueTask.FromResult<LoginCredentials?>(null);
	}

	/// <summary>A user who types the key's passphrase when asked and refuses everything else.</summary>
	private sealed class PassphraseInteraction(string passphrase) : IUserInteraction
	{
		private int _prompts;

		public int Prompts => Volatile.Read(ref _prompts);

		public Task<SecretPromptResult?> PromptSecretAsync(SecretPrompt prompt, CancellationToken cancellationToken = default)
		{
			Interlocked.Increment(ref _prompts);
			return Task.FromResult<SecretPromptResult?>(new SecretPromptResult(passphrase, false));
		}

		public Task<HostTrustDecision> ConfirmHostIdentityAsync(HostTrustPrompt prompt, CancellationToken cancellationToken = default) =>
			Task.FromResult(HostTrustDecision.Reject);

		public Task<CredentialPromptResult?> PromptCredentialsAsync(CredentialPrompt prompt, CancellationToken cancellationToken = default) =>
			Task.FromResult<CredentialPromptResult?>(null);

		public Task<IReadOnlyList<string>?> PromptKeyboardInteractiveAsync(KeyboardInteractivePrompt prompt, CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<string>?>(null);

		public Task<ConfirmResult> ConfirmAsync(ConfirmPrompt prompt, CancellationToken cancellationToken = default) =>
			Task.FromResult(ConfirmResult.No);

		public Task<OverwriteDecision> ConfirmOverwriteAsync(OverwritePrompt prompt, CancellationToken cancellationToken = default) =>
			Task.FromResult(OverwriteDecision.Cancel);

		public void Notify(NoticeSeverity severity, string message, string? title = null)
		{
		}
	}
}
