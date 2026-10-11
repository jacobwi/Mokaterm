using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.UI.Tests.Fakes;

/// <summary>A connected SSH session that never changes, for components that only read a session.</summary>
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

	public event Action? Changed
	{
		add { }
		remove { }
	}

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

	public SessionState State => SessionState.Connected;

	public string? StatusMessage => null;

	public ConnectFailure? Failure => null;

	public DateTimeOffset OpenedAt => DateTimeOffset.UnixEpoch;

	public DateTimeOffset? ConnectedAt => DateTimeOffset.UnixEpoch;

	public ITerminalStream? Terminal { get; init; }

	/// <summary>The live session, for a test that needs its features; none by default.</summary>
	public IProtocolSession? Session { get; init; }
}
