using System.Net.Security;
using Devolutions.IronRdp;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// A connected RDP session before anything has been decoded: the TLS stream, the frame reader over it and what
/// the server agreed to. Disposing it closes the connection.
/// </summary>
internal sealed class RdpTransport : IAsyncDisposable
{
	private readonly Config _config;
	private int _closed;
	private int _disposed;

	public RdpTransport(
		Config config,
		ConnectionResult result,
		Framed<SslStream> framed,
		SslStream stream,
		int width,
		int height,
		bool networkLevelAuthentication,
		HostIdentity? identity,
		bool identityVerified)
	{
		ArgumentNullException.ThrowIfNull(result);
		_config = config;
		Result = result;
		Framed = framed;
		Stream = stream;
		Width = width;
		Height = height;
		NetworkLevelAuthentication = networkLevelAuthentication;
		Identity = identity;
		IdentityVerified = identityVerified;
	}

	public ConnectionResult Result { get; }

	public Framed<SslStream> Framed { get; }

	/// <summary>
	/// The TLS stream underneath <see cref="Framed"/>. Writes go here rather than through the frame reader, whose
	/// write lock is a thread-affine mutex that an await can move off its owning thread.
	/// </summary>
	public SslStream Stream { get; }

	public int Width { get; }

	public int Height { get; }

	/// <summary>True when the login was checked with CredSSP before the desktop started.</summary>
	public bool NetworkLevelAuthentication { get; }

	/// <summary>The server's certificate, or null when it presented none.</summary>
	public HostIdentity? Identity { get; }

	/// <summary>True when the certificate was known or the user accepted it.</summary>
	public bool IdentityVerified { get; }

	public RdpConnectionInfo Describe() => new()
	{
		Width = Width,
		Height = Height,
		IsEncrypted = true,
		Security = NetworkLevelAuthentication
			? "TLS with network level authentication"
			: "TLS, with the login left to the server's own screen",
		IdentityVerified = IdentityVerified,
		CertificateSubject = Identity?.Subject,
		CertificateFingerprint = Identity?.Fingerprint,
	};

	/// <summary>
	/// Closes the connection and leaves everything else alone. The frame reader has no cancellation of its own, so
	/// this is what ends a session loop that is waiting on the wire, before the handles it is using go away.
	/// </summary>
	public async ValueTask CloseAsync()
	{
		if (Interlocked.Exchange(ref _closed, 1) != 0)
		{
			return;
		}

		try
		{
			await Stream.DisposeAsync();
		}
		catch (Exception ex) when (ex is IOException or ObjectDisposedException)
		{
			// The connection is gone either way.
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		await CloseAsync();
		Result.Dispose();
		_config.Dispose();
	}
}
