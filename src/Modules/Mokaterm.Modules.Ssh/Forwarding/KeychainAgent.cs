using System.Text;
using Mokaterm.Modules.Ssh.Agent;
using Mokaterm.Modules.Ssh.Connection;
using Renci.SshNet;
using Renci.SshNet.Security;

namespace Mokaterm.Modules.Ssh.Forwarding;

/// <summary>
/// A small agent in this process that holds the one key a login signed in with, for agent forwarding when no system
/// agent answers. It lists that key and signs with it through the same SSH.NET key algorithms the login used, the
/// RSA SHA-2 flags included; everything else (adding, removing, locking, extensions) gets a failure, so the server's
/// programs can use the key while the session lasts and nothing more.
/// </summary>
internal sealed class KeychainAgent : IForwardedAgent, IDisposable
{
	/// <summary>What <c>ssh-add -l</c> on the server shows next to the key.</summary>
	public const string Comment = "Mokaterm keychain";

	private const string RsaKeyType = "ssh-rsa";

	private readonly PrivateKeyFile _key;
	private readonly byte[] _blob;
	private readonly string _keyType;

	// SSH.NET's signature objects keep hash state, and several forwarded channels may sign at once.
	private readonly Lock _signing = new();

	/// <param name="key">Owned: disposed with the agent.</param>
	public KeychainAgent(PrivateKeyFile key)
	{
		ArgumentNullException.ThrowIfNull(key);
		_key = key;
		_blob = key.HostKeyAlgorithms.First().Data;
		_keyType = SshHostKeys.ReadKeyType(_blob) ?? "";
	}

	/// <summary><c>SHA256:...</c> of the key, as <c>ssh-add -l</c> prints it.</summary>
	public string Fingerprint => SshHostKeys.Fingerprint(_blob);

	public Task<byte[]> ExchangeAsync(byte[] request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return Task.FromResult(Answer(request));
	}

	/// <summary>Answers one request body with one response body.</summary>
	public byte[] Answer(ReadOnlySpan<byte> request)
	{
		if (request.IsEmpty)
		{
			return Failure();
		}

		return request[0] switch
		{
			SshAgentClient.RequestIdentities when request.Length == 1 => Identities(),
			SshAgentClient.SignRequest => Sign(request[1..]),
			_ => Failure(),
		};
	}

	public void Dispose() => _key.Dispose();

	/// <summary>The algorithm a sign request asks for: SHA-2 for an RSA key when a flag says so, the key's own otherwise.</summary>
	internal string AlgorithmFor(uint flags) =>
		_keyType != RsaKeyType
			? _keyType
			: (flags & SshAgentClient.FlagRsaSha2512) != 0
				? "rsa-sha2-512"
				: (flags & SshAgentClient.FlagRsaSha2256) != 0
					? "rsa-sha2-256"
					: RsaKeyType;

	private static byte[] Failure() => [SshAgentClient.Failure];

	private byte[] Identities() =>
		new SshWireWriter()
			.WriteByte(SshAgentClient.IdentitiesAnswer)
			.WriteUInt32(1)
			.WriteString(_blob)
			.WriteString(Encoding.UTF8.GetBytes(Comment))
			.ToArray();

	private byte[] Sign(ReadOnlySpan<byte> body)
	{
		SshWireReader reader = new(body.ToArray());
		if (!reader.TryReadString(out byte[] blob)
			|| !reader.TryReadString(out byte[] data)
			|| !reader.TryReadUInt32(out uint flags)
			|| reader.Remaining != 0
			|| !blob.AsSpan().SequenceEqual(_blob))
		{
			return Failure();
		}

		string algorithm = AlgorithmFor(flags);
		if (_key.HostKeyAlgorithms.FirstOrDefault(candidate => string.Equals(candidate.Name, algorithm, StringComparison.Ordinal)) is not HostAlgorithm signer)
		{
			return Failure();
		}

		byte[] signature;
		lock (_signing)
		{
			try
			{
				// The wire-format blob (algorithm name, then signature), which is exactly what a sign response carries.
				signature = signer.Sign(data);
			}
			catch (Exception)
			{
				// A key that cannot sign is answered the way an agent answers it, not by ending the channel.
				return Failure();
			}
		}

		return new SshWireWriter().WriteByte(SshAgentClient.SignResponse).WriteString(signature).ToArray();
	}
}
