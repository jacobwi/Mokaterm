using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.UI.FileBrowser.Tests.Fakes;

/// <summary>A session tab whose connection can drop and come back, each connection a new object.</summary>
internal sealed class FakeTab(FakeFileFeature feature) : ISessionHandle
{
	public event Action? Changed
	{
		add { }
		remove { }
	}

	public Guid Id { get; } = Guid.NewGuid();

	public string Title => "abc@build-01";

	public HostProfile Host { get; } = new() { Id = Guid.NewGuid(), Address = "build-01" };

	public ConnectionProfile Connection { get; } = new() { Id = Guid.NewGuid(), HostId = Guid.NewGuid(), ProtocolId = "ssh" };

	public ProtocolDescriptor Protocol { get; } = new() { Id = "ssh", DisplayName = "SSH", DefaultPort = 22, Capabilities = ProtocolCapabilities.FileSystem };

	public bool IsTransient { get; init; }

	public SessionState State => Session is null ? SessionState.Disconnected : SessionState.Connected;

	public string? StatusMessage => null;

	public ConnectFailure? Failure => null;

	public DateTimeOffset OpenedAt => DateTimeOffset.UnixEpoch;

	public DateTimeOffset? ConnectedAt => DateTimeOffset.UnixEpoch;

	public ITerminalStream? Terminal => null;

	public IProtocolSession? Session { get; private set; } = new FakeConnection(feature);

	public void Reconnect() => Session = new FakeConnection(feature);

	public void Disconnect() => Session = null;
}
