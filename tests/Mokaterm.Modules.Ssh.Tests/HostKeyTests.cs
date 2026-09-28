using System.Text;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Ssh.Connection;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Security;

namespace Mokaterm.Modules.Ssh.Tests;

public sealed class HostKeyTests
{
	[Fact]
	public void Fingerprint_MatchesSshKeygenForAPublicKeyBlob()
	{
		byte[] blob = Convert.FromBase64String(TestKeys.Ed25519PublicKey);

		Assert.Equal(TestKeys.Ed25519Fingerprint, SshHostKeys.Fingerprint(blob));
	}

	[Fact]
	public void Fingerprint_IsUnpaddedBase64WithPrefix()
	{
		string fingerprint = SshHostKeys.Fingerprint("anything"u8);

		Assert.StartsWith("SHA256:", fingerprint, StringComparison.Ordinal);
		Assert.Equal(43, fingerprint.Length - "SHA256:".Length);
		Assert.DoesNotContain('=', fingerprint);
	}

	[Theory]
	[InlineData(TestKeys.Ed25519PublicKey, "ssh-ed25519")]
	[InlineData("AAAAB3NzaC1yc2EAAAADAQABAAABAQCE0ZdP7rupFlMe", "ssh-rsa")]
	public void ReadKeyType_ReadsTheNameInTheBlob(string base64, string expected)
	{
		byte[] blob = Convert.FromBase64String(PadBase64(base64));

		Assert.Equal(expected, SshHostKeys.ReadKeyType(blob));
	}

	[Fact]
	public void ReadKeyType_RejectsNonsense()
	{
		Assert.Null(SshHostKeys.ReadKeyType([0, 0]));
		Assert.Null(SshHostKeys.ReadKeyType([0xFF, 0xFF, 0xFF, 0xFF, 1]));
		Assert.Null(SshHostKeys.ReadKeyType([0, 0, 0, 2, 0x0A, 0x41]));
	}

	[Theory]
	[InlineData(TestKeys.Ed25519, null, "ssh-ed25519", TestKeys.Ed25519Fingerprint)]
	[InlineData(TestKeys.EcdsaP256, null, "ecdsa-sha2-nistp256", TestKeys.EcdsaP256Fingerprint)]
	[InlineData(TestKeys.EncryptedRsaPem, TestKeys.EncryptedRsaPemPassphrase, "ssh-rsa", TestKeys.EncryptedRsaPemFingerprint)]
	public void CreateIdentity_AgreesWithSshNetAndSshKeygen(string privateKey, string? passphrase, string algorithm, string fingerprint)
	{
		using PrivateKeyFile keyFile = LoadKey(privateKey, passphrase);
		HostKeyEventArgs args = new(new KeyHostAlgorithm(keyFile.Key.ToString()!, keyFile.Key));

		HostIdentity identity = SshHostKeys.CreateIdentity("10.10.2.3", 2222, args);

		Assert.Equal("10.10.2.3", identity.Host);
		Assert.Equal(2222, identity.Port);
		Assert.Equal(HostIdentityKind.SshHostKey, identity.Kind);
		Assert.Equal(algorithm, identity.Algorithm);
		Assert.Equal(fingerprint, identity.Fingerprint);
		Assert.Equal("SHA256:" + args.FingerPrintSHA256, identity.Fingerprint);
	}

	[Fact]
	public void CreateIdentity_NamesRsaKeysByKeyTypeWhateverSignatureWasNegotiated()
	{
		using PrivateKeyFile keyFile = LoadKey(TestKeys.EncryptedRsaPem, TestKeys.EncryptedRsaPemPassphrase);
		HostKeyEventArgs args = new(new KeyHostAlgorithm("rsa-sha2-512", keyFile.Key));

		Assert.Equal("ssh-rsa", SshHostKeys.CreateIdentity("host", 22, args).Algorithm);
	}

	[Fact]
	public void Check_KnownHost_TrustsAtOnce()
	{
		RecordingVerifier verifier = new(Task.FromResult(true));
		HostKeyCheck check = new("host", 22, verifier, null, null, CancellationToken.None);
		HostKeyEventArgs args = Ed25519HostKey();

		check.OnHostKeyReceived(null, args);

		Assert.True(args.CanTrust);
		Assert.Equal(TestKeys.Ed25519Fingerprint, check.AcceptedFingerprint);
		Assert.Equal(TestKeys.Ed25519Fingerprint, Assert.Single(verifier.Identities).Fingerprint);
	}

	[Fact]
	public void Check_RejectedHost_RefusesTheKey()
	{
		HostKeyCheck check = new("host", 22, new RecordingVerifier(Task.FromResult(false)), null, null, CancellationToken.None);
		HostKeyEventArgs args = Ed25519HostKey();

		check.OnHostKeyReceived(null, args);

		Assert.False(args.CanTrust);
		Assert.True(check.Rejected);
		Assert.Null(check.AcceptedFingerprint);
	}

	[Fact]
	public async Task Check_UserStillDeciding_RefusesForNowAndHandsBackTheVerification()
	{
		TaskCompletionSource<bool> decision = new();
		HostKeyCheck check = new("host", 22, new RecordingVerifier(decision.Task), null, null, CancellationToken.None);
		HostKeyEventArgs args = Ed25519HostKey();

		check.OnHostKeyReceived(null, args);

		Assert.False(args.CanTrust);
		Assert.False(check.Rejected);
		PendingHostKey pending = check.Pending ?? throw new InvalidOperationException("Nothing is pending.");
		Assert.Equal(TestKeys.Ed25519Fingerprint, pending.Fingerprint);
		decision.SetResult(true);
		Assert.True(await pending.Verification);
	}

	[Fact]
	public void Check_ApprovedFingerprint_IsTrustedWithoutAskingAgain()
	{
		RecordingVerifier verifier = new(Task.FromResult(false));
		HostKeyCheck check = new("host", 22, verifier, TestKeys.Ed25519Fingerprint, null, CancellationToken.None);
		HostKeyEventArgs args = Ed25519HostKey();

		check.OnHostKeyReceived(null, args);

		Assert.True(args.CanTrust);
		Assert.Empty(verifier.Identities);
	}

	[Fact]
	public void Check_DifferentKeyAfterApproval_GoesThroughVerificationAgain()
	{
		RecordingVerifier verifier = new(Task.FromResult(false));
		HostKeyCheck check = new("host", 22, verifier, TestKeys.EcdsaP256Fingerprint, null, CancellationToken.None);
		HostKeyEventArgs args = Ed25519HostKey();

		check.OnHostKeyReceived(null, args);

		Assert.False(args.CanTrust);
		Assert.Single(verifier.Identities);
		Assert.True(check.Rejected);
	}

	[Fact]
	public void Check_CompanionConnection_OnlyAcceptsThePinnedKey()
	{
		HostKeyCheck check = new("host", 22, verifier: null, TestKeys.EcdsaP256Fingerprint, null, CancellationToken.None);
		HostKeyEventArgs args = Ed25519HostKey();

		check.OnHostKeyReceived(null, args);

		Assert.False(args.CanTrust);
		Assert.True(check.KeyChanged);
	}

	[Fact]
	public void Check_AfterPin_RefusesAnotherKeyOnReKey()
	{
		HostKeyCheck check = new("host", 22, new RecordingVerifier(Task.FromResult(true)), null, null, CancellationToken.None);
		check.OnHostKeyReceived(null, Ed25519HostKey());
		check.Pin();

		using PrivateKeyFile other = LoadKey(TestKeys.EcdsaP256, null);
		HostKeyEventArgs rekey = new(new KeyHostAlgorithm(other.Key.ToString()!, other.Key));
		check.OnHostKeyReceived(null, rekey);

		Assert.False(rekey.CanTrust);
		Assert.True(check.KeyChanged);
	}

	[Fact]
	public void Check_VerifierFailure_IsReportedAndRefused()
	{
		HostKeyCheck check = new("host", 22, new RecordingVerifier(Task.FromException<bool>(new InvalidOperationException("vault locked"))), null, null, CancellationToken.None);
		HostKeyEventArgs args = Ed25519HostKey();

		check.OnHostKeyReceived(null, args);

		Assert.False(args.CanTrust);
		Assert.Equal("vault locked", check.Error?.Message);
	}

	internal static PrivateKeyFile LoadKey(string privateKey, string? passphrase)
	{
		using MemoryStream stream = new(Encoding.UTF8.GetBytes(privateKey));
		return new PrivateKeyFile(stream, passphrase);
	}

	private static HostKeyEventArgs Ed25519HostKey()
	{
		PrivateKeyFile keyFile = LoadKey(TestKeys.Ed25519, null);
		return new HostKeyEventArgs(new KeyHostAlgorithm("ssh-ed25519", keyFile.Key));
	}

	private static string PadBase64(string value) => value.PadRight((value.Length + 3) / 4 * 4, '=');

	private sealed class RecordingVerifier(Task<bool> answer) : IHostIdentityVerifier
	{
		public List<HostIdentity> Identities { get; } = [];

		public ValueTask<bool> VerifyAsync(HostIdentity identity, CancellationToken cancellationToken = default)
		{
			Identities.Add(identity);
			return new ValueTask<bool>(answer);
		}
	}
}
