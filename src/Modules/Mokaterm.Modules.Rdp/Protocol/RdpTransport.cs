using System.Net.Security;
using Devolutions.IronRdp;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Rdp.Sessions;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// A connected RDP session before anything has been decoded: the TLS stream, the frame reader over it and what
/// the server agreed to. Disposing it closes the connection.
/// </summary>
internal sealed class RdpTransport : IRdpWire, IAsyncDisposable
{
	private int _closed;
	private int _disposed;

	public RdpTransport(
		ConnectionResult result,
		SslStream stream,
		RdpFrameReader reader,
		int width,
		int height,
		bool networkLevelAuthentication,
		HostIdentity? identity,
		bool identityVerified)
	{
		ArgumentNullException.ThrowIfNull(result);
		ArgumentNullException.ThrowIfNull(stream);
		ArgumentNullException.ThrowIfNull(reader);
		Result = result;
		Stream = stream;
		Reader = reader;
		Width = width;
		Height = height;
		NetworkLevelAuthentication = networkLevelAuthentication;
		Identity = identity;
		IdentityVerified = identityVerified;
	}

	public ConnectionResult Result { get; }

	/// <summary>
	/// The TLS stream. Writes go straight here, each one under the session's gate, never through IronRDP's
	/// <c>Framed&lt;T&gt;.Write</c>, whose write lock is a thread-affine mutex that an await can move off its owning thread.
	/// </summary>
	public SslStream Stream { get; }

	/// <summary>Reads whole messages from <see cref="Stream"/>. Only the session's pump reads.</summary>
	public RdpFrameReader Reader { get; }

	public int Width { get; }

	public int Height { get; }

	/// <summary>True when the login was checked with CredSSP before the desktop started.</summary>
	public bool NetworkLevelAuthentication { get; }

	/// <summary>The server's certificate, or null when it presented none.</summary>
	public HostIdentity? Identity { get; }

	/// <summary>True when the certificate was known or the user accepted it.</summary>
	public bool IdentityVerified { get; }

	public async ValueTask WriteAsync(byte[] bytes, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(bytes);
		await Stream.WriteAsync(bytes, cancellationToken);
		await Stream.FlushAsync(cancellationToken);
	}

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
	/// Closes the connection and leaves everything else alone. A read that is waiting on the wire fails at once, which
	/// is what ends a session loop before the handles it is using go away.
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
		Reader.Dispose();
		Result.Dispose();
	}
}
