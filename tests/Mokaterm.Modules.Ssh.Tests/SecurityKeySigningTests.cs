using System.Text;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Ssh.Agent;
using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.SecurityKeys;
using Mokaterm.Modules.Ssh.Tests.Fakes;
using Renci.SshNet.Security;
using LoginMethod = Mokaterm.Abstractions.Connections.AuthenticationMethod;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>
/// A security key file signing a login through the authenticator, end to end with a software authenticator that signs
/// what a FIDO2 key signs. Each signature is checked with the port of sshd's rules and, where the machine has one, by
/// <c>ssh-keygen</c> itself, which runs OpenSSH's own <c>sshkey_verify</c>.
/// </summary>
public sealed class SecurityKeySigningTests : IDisposable
{
	private static readonly byte[] LoginData = Encoding.ASCII.GetBytes("session id, then the whole publickey request");

	private readonly SoftwareSecurityKey _device = new();

	public void Dispose() => _device.Dispose();

	[Theory]
	[InlineData(SecurityKeyAlgorithm.EcdsaP256)]
	[InlineData(SecurityKeyAlgorithm.Ed25519)]
	public void Sign_ProducesASignatureSshdAccepts(SecurityKeyAlgorithm algorithm)
	{
		SshSecurityKey key = Enrol(algorithm, SecurityKeyFlags.UserPresenceRequired);
		HostAlgorithm hostAlgorithm = Assert.Single(new SecurityKeySource(key, _device).HostKeyAlgorithms);

		byte[] signature = hostAlgorithm.Sign(LoginData);

		Assert.Equal(key.KeyType, hostAlgorithm.Name);
		Assert.Equal(key.PublicKeyBlob, hostAlgorithm.Data);
		Assert.True(OpenSshSkVerifier.Verify(key.PublicKeyBlob, signature, LoginData));
		Assert.False(OpenSshSkVerifier.Verify(key.PublicKeyBlob, signature, [.. LoginData, 0]));
		Assert.False(hostAlgorithm.VerifySignature(LoginData, signature));
	}

	[Fact]
	public void Sign_HandsTheMessageItselfToTheAuthenticatorAsClientData()
	{
		SshSecurityKey key = Enrol(SecurityKeyAlgorithm.EcdsaP256, SecurityKeyFlags.UserPresenceRequired | SecurityKeyFlags.UserVerificationRequired);

		Assert.Single(new SecurityKeySource(key, _device).HostKeyAlgorithms).Sign(LoginData);

		SecurityKeyAssertionRequest request = Assert.Single(_device.Assertions);
		Assert.Equal(LoginData, request.ClientData);
		Assert.Equal("ssh:", request.RelyingPartyId);
		Assert.Equal(key.KeyHandle, request.CredentialId);
		Assert.True(request.RequireUserVerification);
	}

	[Fact]
	public void Sign_FallsBackToTheWebAuthnTypeWhenRawClientDataIsRefused()
	{
		SshSecurityKey key = Enrol(SecurityKeyAlgorithm.EcdsaP256, SecurityKeyFlags.UserPresenceRequired);
		_device.RefuseRawClientData = true;

		byte[] signature = Assert.Single(new SecurityKeySource(key, _device).HostKeyAlgorithms).Sign(LoginData);

		SshWireReader reader = new(signature);
		Assert.True(reader.TryReadText(out string type));
		Assert.Equal(SecurityKeyTypes.WebAuthnEcdsaSignatureType, type);
		Assert.Equal(2, _device.Assertions.Count);
		Assert.True(OpenSshSkVerifier.Verify(key.PublicKeyBlob, signature, LoginData));
	}

	[Fact]
	public void Sign_CarriesExtensionsOnlyInTheWebAuthnType()
	{
		SshSecurityKey key = Enrol(SecurityKeyAlgorithm.EcdsaP256, SecurityKeyFlags.UserPresenceRequired);
		_device.RefuseRawClientData = true;
		_device.AssertionExtensions = [0xA1, 0x61, (byte)'x', 0xF5];

		byte[] signature = Assert.Single(new SecurityKeySource(key, _device).HostKeyAlgorithms).Sign(LoginData);

		Assert.True(OpenSshSkVerifier.Verify(key.PublicKeyBlob, signature, LoginData));
	}

	[Fact]
	public void Sign_HasNoFallbackForEd25519()
	{
		SshSecurityKey key = Enrol(SecurityKeyAlgorithm.Ed25519, SecurityKeyFlags.UserPresenceRequired);
		_device.RefuseRawClientData = true;

		ProtocolConnectException failure = Assert.Throws<ProtocolConnectException>(() =>
			Assert.Single(new SecurityKeySource(key, _device).HostKeyAlgorithms).Sign(LoginData));

		Assert.Equal(ConnectFailure.AuthenticationFailed, failure.Failure);
		Assert.Single(_device.Assertions);
	}

	[Theory]
	[InlineData(SecurityKeyFailure.Cancelled, ConnectFailure.Cancelled, "closed")]
	[InlineData(SecurityKeyFailure.TimedOut, ConnectFailure.Timeout, "not touched in time")]
	[InlineData(SecurityKeyFailure.NoCredential, ConnectFailure.AuthenticationFailed, "Plug in the key")]
	[InlineData(SecurityKeyFailure.NotSupported, ConnectFailure.AuthenticationFailed, "the platform's own words")]
	public void Sign_TurnsAuthenticatorFailuresIntoConnectFailures(SecurityKeyFailure failure, ConnectFailure expected, string message)
	{
		SshSecurityKey key = Enrol(SecurityKeyAlgorithm.EcdsaP256, SecurityKeyFlags.UserPresenceRequired);
		_device.Failure = new SecurityKeyException(failure, "the platform's own words");

		ProtocolConnectException thrown = Assert.Throws<ProtocolConnectException>(() =>
			Assert.Single(new SecurityKeySource(key, _device).HostKeyAlgorithms).Sign(LoginData));

		Assert.Equal(expected, thrown.Failure);
		Assert.Contains(message, thrown.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Sign_PausesTheConnectWatchdogWhileTheUserTouchesTheKey()
	{
		SshSecurityKey key = Enrol(SecurityKeyAlgorithm.EcdsaP256, SecurityKeyFlags.UserPresenceRequired);
		using ConnectWatchdog watchdog = new(TimeSpan.FromSeconds(1));
		SlowDevice slow = new(_device, TimeSpan.FromSeconds(1.5), () => watchdog.TimedOut);
		SecurityKeySource source = new(key, slow);
		RecordingInteraction interaction = new();
		SecurityKeyTouch touch = new(watchdog, interaction, "me@host", TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
		source.Touch.Attach(touch);

		Assert.Single(source.HostKeyAlgorithms).Sign(LoginData);

		// The touch outlasts the step timeout, so only a paused watchdog had not fired by the time the key answered.
		Assert.False(slow.TimedOutDuringTouch);
		Assert.False(watchdog.TimedOut);
		Notice notice = Assert.Single(interaction.Notices);
		Assert.Equal(NoticeSeverity.Info, notice.Severity);
		Assert.Contains("Touch your security key to log in to me@host", notice.Message, StringComparison.Ordinal);
		Assert.Equal(TimeSpan.FromMinutes(1), Assert.Single(_device.Assertions).Timeout);
	}

	[Fact]
	public void Login_AttachesItsTouchGateAndRemovesOnlyItsOwn()
	{
		SshSecurityKey key = Enrol(SecurityKeyAlgorithm.Ed25519, SecurityKeyFlags.UserPresenceRequired);
		SecurityKeySource source = new(key, _device);
		using LoginCredentials credentials = new() { Username = "me", Method = LoginMethod.PublicKey };
		using ConnectWatchdog watchdog = new(TimeSpan.FromMinutes(1));

		SshLogin first = new(credentials, source, new RecordingInteraction(), "me@host", TimeSpan.FromMinutes(1), watchdog, CancellationToken.None);
		SecurityKeyTouch? firstTouch = source.Touch.Current;
		SshLogin second = new(credentials, source, new RecordingInteraction(), "me@host", TimeSpan.FromMinutes(1), watchdog, CancellationToken.None);
		SecurityKeyTouch? secondTouch = source.Touch.Current;
		first.Dispose();

		Assert.NotNull(firstTouch);
		Assert.NotSame(firstTouch, secondTouch);
		Assert.Same(secondTouch, source.Touch.Current);
		second.Dispose();
		Assert.Null(source.Touch.Current);
	}

	[Theory(Timeout = TestTimeouts.NetworkMilliseconds)]
	[InlineData(SecurityKeyAlgorithm.EcdsaP256, false)]
	[InlineData(SecurityKeyAlgorithm.EcdsaP256, true)]
	[InlineData(SecurityKeyAlgorithm.Ed25519, false)]
	public async Task SshKeygen_VerifiesTheSignatureWithOpenSshsOwnCode(SecurityKeyAlgorithm algorithm, bool webAuthn)
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		string keygen = OpenSshKeygen.Find() ?? "";
		Assert.SkipWhen(keygen.Length == 0, "No ssh-keygen is installed here to check the signature against.");
		SshSecurityKey key = Enrol(algorithm, SecurityKeyFlags.UserPresenceRequired);
		_device.RefuseRawClientData = webAuthn;
		byte[] message = Encoding.UTF8.GetBytes("a message for ssh-keygen -Y");
		byte[] signature = Assert.Single(new SecurityKeySource(key, _device).HostKeyAlgorithms).Sign(SshSignatures.Signed("mokaterm", message));

		await using TempFolder folder = new();
		string path = Path.Combine(folder.Path, "message.sig");
		await File.WriteAllTextAsync(path, SshSignatures.Armor(key.PublicKeyBlob, "mokaterm", signature), ct);

		(int exitCode, string output, string error) = await OpenSshKeygen.RunAsync(keygen, ["-Y", "check-novalidate", "-n", "mokaterm", "-s", path], message, ct);
		Assert.True(exitCode == 0, error);
		Assert.Contains("Good \"mokaterm\" signature", output + error, StringComparison.Ordinal);

		(exitCode, _, _) = await OpenSshKeygen.RunAsync(keygen, ["-Y", "check-novalidate", "-n", "mokaterm", "-s", path], [.. message, (byte)'!'], ct);
		Assert.NotEqual(0, exitCode);
	}

	private SshSecurityKey Enrol(SecurityKeyAlgorithm algorithm, SecurityKeyFlags flags)
	{
		(byte[] credentialId, byte[] publicKey) = _device.Enrol(algorithm, "ssh:");
		SshWireWriter blob = new SshWireWriter().WriteString(Encoding.ASCII.GetBytes(SecurityKeyTypes.KeyType(algorithm)));
		if (algorithm == SecurityKeyAlgorithm.EcdsaP256)
		{
			blob.WriteString("nistp256"u8);
		}

		blob.WriteString(publicKey).WriteString("ssh:"u8);
		return new SshSecurityKey
		{
			Algorithm = algorithm,
			PublicKeyBlob = blob.ToArray(),
			Application = "ssh:",
			Flags = flags,
			KeyHandle = credentialId,
		};
	}

	/// <summary>A device the user takes a while to touch, which notes whether the watchdog fired meanwhile.</summary>
	private sealed class SlowDevice(ISecurityKeyAuthenticator inner, TimeSpan delay, Func<bool> timedOut) : ISecurityKeyAuthenticator
	{
		public string Description => inner.Description;

		public bool IsAvailable => true;

		public bool TimedOutDuringTouch { get; private set; }

		public async Task<SecurityKeyAssertion> GetAssertionAsync(SecurityKeyAssertionRequest request, CancellationToken cancellationToken = default)
		{
			await Task.Delay(delay, cancellationToken);
			TimedOutDuringTouch = timedOut();
			return await inner.GetAssertionAsync(request, cancellationToken);
		}

		public Task<SecurityKeyCredential> MakeCredentialAsync(SecurityKeyCredentialRequest request, CancellationToken cancellationToken = default) =>
			inner.MakeCredentialAsync(request, cancellationToken);
	}
}
