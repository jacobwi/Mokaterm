using Moka.Red.Core.Enums;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;

namespace Mokaterm.UI.Presentation;

/// <summary>Status dot colors and labels for session states.</summary>
internal static class SessionStateDisplay
{
	public static MokaColor Color(SessionState state) => state switch
	{
		SessionState.Connected => MokaColor.Success,
		SessionState.Connecting => MokaColor.Warning,
		SessionState.Failed => MokaColor.Error,
		_ => MokaColor.Surface,
	};

	public static string Label(SessionState state) => state switch
	{
		SessionState.Connected => "CONNECTED",
		SessionState.Connecting => "CONNECTING",
		SessionState.Disconnected => "DISCONNECTED",
		_ => "FAILED",
	};

	/// <inheritdoc cref="ISessionHandle.CanReconnect"/>
	public static bool CanReconnect(SessionState state) => ISessionHandle.CanReconnect(state);

	public static string FailureTitle(ConnectFailure? failure) => failure switch
	{
		ConnectFailure.HostUnreachable => "Host unreachable",
		ConnectFailure.Timeout => "Connection timed out",
		ConnectFailure.AuthenticationFailed => "Authentication failed",
		ConnectFailure.HostIdentityRejected => "Host identity rejected",
		ConnectFailure.Cancelled => "Connection cancelled",
		ConnectFailure.ProtocolError => "Protocol error",
		_ => "Connection failed",
	};
}
