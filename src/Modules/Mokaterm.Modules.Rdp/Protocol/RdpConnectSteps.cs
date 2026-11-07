using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// The steps of an RDP connect as the session tab draws them. Each <see cref="RdpConnectStage"/> reports as one of them,
/// a Kerberos request as part of the sign-in it serves.
/// </summary>
internal static class RdpConnectSteps
{
	public const string Connect = "connect";

	public const string Negotiate = "negotiate";

	public const string Tls = "tls";

	public const string Certificate = "certificate";

	public const string SignIn = "sign-in";

	public const string Session = "session";

	private static readonly ConnectStep[] WithSignIn =
	[
		new(Connect, "Connect", ConnectStepKind.Dial),
		new(Negotiate, "Negotiate", ConnectStepKind.Negotiate),
		new(Tls, "TLS", ConnectStepKind.Encryption),
		new(Certificate, "Certificate", ConnectStepKind.HostIdentity),
		new(SignIn, "Sign-in", ConnectStepKind.Login),
		new(Session, "Session", ConnectStepKind.Session),
	];

	private static readonly ConnectStep[] WithoutSignIn = [.. WithSignIn.Where(step => step.Id != SignIn)];

	/// <summary>
	/// The steps for <paramref name="route"/>. Only network level authentication checks the login before the session
	/// starts; the other routes send it with the session, or not at all, so they have no sign-in step of their own.
	/// </summary>
	public static IReadOnlyList<ConnectStep> For(RdpSignInRoute route) =>
		route == RdpSignInRoute.NetworkLevelAuthentication ? WithSignIn : WithoutSignIn;

	/// <summary>The step <paramref name="stage"/> reports as.</summary>
	public static string Of(RdpConnectStage stage) => stage switch
	{
		RdpConnectStage.Dialing => Connect,
		RdpConnectStage.Negotiating => Negotiate,
		RdpConnectStage.Tls => Tls,
		RdpConnectStage.Certificate => Certificate,
		RdpConnectStage.Login or RdpConnectStage.Kerberos => SignIn,
		_ => Session,
	};
}
