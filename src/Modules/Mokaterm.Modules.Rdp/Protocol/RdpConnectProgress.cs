using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// Which step a connect attempt is on. Every step reports its own status line, and a failure or a timeout is worded
/// for the step it happened in.
/// </summary>
internal sealed class RdpConnectProgress
{
	private readonly IProgress<string>? _status;
	private readonly IProgress<string>? _trace;
	private volatile StageState _current = new(RdpConnectStage.Dialing, null);

	public RdpConnectProgress(string host, int port, IProgress<string>? status, IProgress<string>? trace = null)
	{
		Host = host;
		Port = port;
		Endpoint = HostEndpoint.Format(host, port);
		_status = status;
		_trace = trace;
	}

	public string Host { get; }

	public int Port { get; }

	/// <summary><see cref="Host"/> and <see cref="Port"/> the way every message writes them.</summary>
	public string Endpoint { get; }

	public RdpConnectStage Stage => _current.Stage;

	/// <summary>
	/// What the step was doing: the address being dialed, the Kerberos server asked, or the connection sequence step
	/// the server stopped answering at.
	/// </summary>
	public string? Detail => _current.Detail;

	/// <summary>Moves on to <paramref name="stage"/> and says so through the status line.</summary>
	public void Enter(RdpConnectStage stage, string? detail = null)
	{
		_current = new StageState(stage, detail);
		_status?.Report(StatusText(stage, detail));
	}

	/// <summary>Records what the current step is doing without a new status line.</summary>
	public void Note(string? detail) => _current = _current with { Detail = detail };

	// Finer than a status line, so only the probe asks for it: the session tab would flicker through every message.
	public void Trace(string text) => _trace?.Report(text);

	/// <summary>The status line for a step, as the session tab and the probe show it.</summary>
	public string StatusText(RdpConnectStage stage, string? detail) => stage switch
	{
		RdpConnectStage.Dialing => detail is null ? $"Connecting to {Endpoint}" : $"Connecting to {Endpoint} ({detail})",
		RdpConnectStage.Negotiating => "Negotiating security",
		RdpConnectStage.Tls => "Starting TLS",
		RdpConnectStage.Certificate => "Checking the certificate",
		RdpConnectStage.Login => "Checking the login (CredSSP)",
		RdpConnectStage.Kerberos => detail is null ? "Asking the Kerberos server" : $"Asking the Kerberos server {detail}",
		RdpConnectStage.Session => "Setting up the session",
		_ => "Connecting",
	};

	// One object, so a reader on another thread never sees a stage with the previous stage's detail.
	private sealed record StageState(RdpConnectStage Stage, string? Detail);
}
