using System.Globalization;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// The codes a server puts in a Set Error Info PDU (MS-RDPBCGR 2.2.5.1.1) to say why it ends a session, in plain words.
/// This is the one place they are worded: the closing notice of a session the server ended and the reason of one that
/// dropped both come from here.
/// </summary>
internal static class RdpErrorInfo
{
	public const uint None = 0x0000_0000;

	public const uint RpcInitiatedDisconnect = 0x0000_0001;

	public const uint RpcInitiatedLogoff = 0x0000_0002;

	public const uint IdleTimeout = 0x0000_0003;

	public const uint LogonTimeout = 0x0000_0004;

	public const uint DisconnectedByOtherConnection = 0x0000_0005;

	public const uint OutOfMemory = 0x0000_0006;

	public const uint ServerDeniedConnection = 0x0000_0007;

	public const uint ServerInsufficientPrivileges = 0x0000_0009;

	public const uint ServerFreshCredentialsRequired = 0x0000_000A;

	public const uint RpcInitiatedDisconnectByUser = 0x0000_000B;

	public const uint LogoffByUser = 0x0000_000C;

	public const uint CloseStackOnDriverNotReady = 0x0000_000F;

	public const uint ServerDwmCrash = 0x0000_0010;

	public const uint CloseStackOnDriverFailure = 0x0000_0011;

	public const uint CloseStackOnDriverIfaceFailure = 0x0000_0012;

	public const uint ServerWinlogonCrash = 0x0000_0017;

	public const uint ServerCsrssCrash = 0x0000_0018;

	public const uint LicenseNoLicenseServer = 0x0000_0101;

	public const uint LicenseNoLicense = 0x0000_0102;

	public const uint LicenseNoRemoteConnections = 0x0000_010A;

	public const uint GraphicsSubsystemFailed = 0x0000_112F;

	// The ranges MS-RDPBCGR groups the rest of the codes in.
	private const uint FirstLicensingCode = 0x0000_0100;
	private const uint LastLicensingCode = 0x0000_010A;
	private const uint FirstBrokerCode = 0x0000_0400;
	private const uint LastBrokerCode = 0x0000_0412;
	private const uint FirstProtocolCode = 0x0000_1000;
	private const uint LastProtocolCode = 0x0000_1FFF;

	/// <summary>
	/// True when the session should be treated as dropped, so the automatic reconnect applies. FreeRDP's
	/// <c>client_auto_reconnect_ex</c> reconnects after a network drop and after this one code, and after no other: every
	/// other code is the server deciding, and connecting again straight away would only meet the same decision.
	/// </summary>
	public static bool Reconnects(uint code) => code == GraphicsSubsystemFailed;

	/// <summary>What happened, as one or two sentences for the closing notice.</summary>
	public static string Describe(uint code) => code switch
	{
		RpcInitiatedDisconnect => "Someone on the server disconnected this remote session. It keeps running there, so reconnecting picks it up again.",
		RpcInitiatedLogoff => "Someone on the server signed this remote session out.",
		IdleTimeout => "The remote session was idle for longer than the server allows.",
		LogonTimeout => "The remote session reached the time limit the server sets for a session.",
		DisconnectedByOtherConnection => "The remote session was ended because another user connected.",
		OutOfMemory => "The server ran out of memory and ended the remote session.",
		ServerDeniedConnection => "The server refused the connection.",
		ServerInsufficientPrivileges => "This account is not allowed to sign in to the server remotely.",
		ServerFreshCredentialsRequired => "The server does not accept a saved password. Type the password when you connect.",
		RpcInitiatedDisconnectByUser => "The remote session was disconnected from inside it. It keeps running on the server, so reconnecting picks it up again.",
		LogoffByUser => "The remote session ended because the user signed out.",
		CloseStackOnDriverNotReady or CloseStackOnDriverFailure or CloseStackOnDriverIfaceFailure =>
			"The display driver on the server could not start the remote session.",
		ServerDwmCrash => "The desktop window manager on the server stopped unexpectedly.",
		ServerWinlogonCrash => "Winlogon on the server stopped unexpectedly.",
		ServerCsrssCrash => "The Windows subsystem process (CSRSS) on the server stopped unexpectedly.",
		LicenseNoLicenseServer => "The server could not find a Remote Desktop license server.",
		LicenseNoLicense => "The server has no Remote Desktop client access licenses left.",
		LicenseNoRemoteConnections => "The server is not licensed to accept remote connections.",
		GraphicsSubsystemFailed => "The graphics system on the server failed.",
		>= FirstLicensingCode and <= LastLicensingCode => $"Remote Desktop licensing on the server refused the session (code {Hex(code)}).",
		>= FirstBrokerCode and <= LastBrokerCode => $"The connection broker could not place the session (code {Hex(code)}).",
		>= FirstProtocolCode and <= LastProtocolCode => $"The server ended the session after a protocol error (code {Hex(code)}).",
		_ => $"The server ended the remote session (code {Hex(code)}).",
	};

	/// <summary>The code as RDP documentation writes it, which is also safe to log.</summary>
	public static string Hex(uint code) => string.Create(CultureInfo.InvariantCulture, $"0x{code:X8}");
}
