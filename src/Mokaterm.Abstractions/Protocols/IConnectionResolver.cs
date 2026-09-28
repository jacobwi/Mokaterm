using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;

namespace Mokaterm.Abstractions.Protocols;

/// <summary>
/// Opens a second saved login from inside a connect attempt, for protocols that reach their target through another
/// host. Only the scope that owns the vault can hand out credentials, which is why this arrives with the attempt
/// rather than through the container.
/// </summary>
public interface IConnectionResolver
{
	/// <summary>The saved login with a credential source of its own, or null when the login no longer exists.</summary>
	ValueTask<ResolvedConnection?> ResolveAsync(Guid connectionId, CancellationToken cancellationToken = default);
}

/// <summary>A saved login ready for a second connect. The caller owns it and must dispose it.</summary>
public sealed class ResolvedConnection : IDisposable
{
	public ResolvedConnection(HostProfile host, ConnectionProfile connection, ICredentialSource credentials)
	{
		Host = host;
		Connection = connection;
		Credentials = credentials;
	}

	public HostProfile Host { get; }

	public ConnectionProfile Connection { get; }

	public ICredentialSource Credentials { get; }

	/// <summary>The port this login uses, falling back to <paramref name="defaultPort"/>.</summary>
	public int GetPort(int defaultPort) => Connection.Port ?? defaultPort;

	public void Dispose() => (Credentials as IDisposable)?.Dispose();
}
