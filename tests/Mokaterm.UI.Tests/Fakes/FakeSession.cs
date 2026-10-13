using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.UI.Tests.Fakes;

/// <summary>
/// A connected SSH session, for components that only read a session. A test that needs it to drop or reconnect sets
/// its state through <see cref="Become"/>, which tells whoever listens, as a real session does.
/// </summary>
internal sealed class FakeSession : ISessionHandle
{
	public FakeSession(Guid? connectionId = null, HostEnvironment environment = HostEnvironment.None)
	{
		Host = new HostProfile { Id = Guid.NewGuid(), Address = "build-01.example.com", Environment = environment };
		Connection = new ConnectionProfile
		{
			Id = connectionId ?? Guid.NewGuid(),
			HostId = Host.Id,
			ProtocolId = "ssh",
			Username = "bc",
		};
	}

	public event Action? Changed;

	public Guid Id { get; } = Guid.NewGuid();

	public string Title => "bc@build-01.example.com";

	public HostProfile Host { get; }

	public ConnectionProfile Connection { get; }

	public ProtocolDescriptor Protocol { get; } = new()
	{
		Id = "ssh",
		DisplayName = "SSH",
		DefaultPort = 22,
		Capabilities = ProtocolCapabilities.Terminal,
	};

	/// <summary>A quick-connect session when set, with no saved login behind it.</summary>
	public bool IsTransient { get; init; }

	public SessionState State { get; private set; } = SessionState.Connected;

	public string? StatusMessage { get; private set; }

	public ConnectFailure? Failure => null;

	public DateTimeOffset OpenedAt => DateTimeOffset.UnixEpoch;

	public DateTimeOffset? ConnectedAt => DateTimeOffset.UnixEpoch;

	public ITerminalStream? Terminal { get; init; }

	/// <summary>The live session, for a test that needs its features; none by default.</summary>
	public IProtocolSession? Session { get; init; }

	/// <summary>Moves the session to <paramref name="state"/> and says so, with the line a real one shows meanwhile.</summary>
	public FakeSession Become(SessionState state, string? statusMessage = null)
	{
		State = state;
		StatusMessage = statusMessage;
		Changed?.Invoke();
		return this;
	}
}
