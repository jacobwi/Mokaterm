using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Modules.Ssh.Tests.Fakes;

/// <summary>A session tab whose state the test moves along, raising <see cref="Changed"/> the way the real handle does.</summary>
internal sealed class ScriptedSessionHandle : ISessionHandle
{
	private int _state = (int)SessionState.Connected;
	private ConnectFailure? _failure;
	private string? _status;

	public ScriptedSessionHandle()
	{
		Host = new HostProfile { Id = Guid.NewGuid(), Address = "web1.example.com" };
		Connection = new ConnectionProfile { Id = Guid.NewGuid(), HostId = Host.Id, ProtocolId = "ssh", Username = "alice" };
	}

	public event Action? Changed;

	public Guid Id { get; } = Guid.NewGuid();

	public string Title => "alice@web1.example.com";

	public HostProfile Host { get; }

	public ConnectionProfile Connection { get; }

	public ProtocolDescriptor Protocol { get; } = new()
	{
		Id = "ssh",
		DisplayName = "SSH",
		DefaultPort = 22,
		Capabilities = ProtocolCapabilities.Terminal,
	};

	public bool IsTransient => false;

	public SessionState State => (SessionState)Volatile.Read(ref _state);

	public string? StatusMessage => _status;

	public ConnectFailure? Failure => _failure;

	public DateTimeOffset OpenedAt => DateTimeOffset.UnixEpoch;

	public DateTimeOffset? ConnectedAt => null;

	public ITerminalStream? Terminal => null;

	public IProtocolSession? Session => null;

	public void Move(SessionState state, ConnectFailure? failure = null, string? status = null)
	{
		_failure = failure;
		_status = status;
		Volatile.Write(ref _state, (int)state);
		Changed?.Invoke();
	}
}
