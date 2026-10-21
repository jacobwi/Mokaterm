using System.Buffers.Binary;
using System.Globalization;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// What Windows said about signing in, read from a Save Session Info PDU (MS-RDPBCGR 2.2.10.1). IronRDP's Client Info
/// PDU sets INFO_LOGONNOTIFY and INFO_LOGONERRORS, so Windows sends one once a user is signed in and the session is
/// theirs (2.2.10.1.1.1 and 2.2.10.1.1.2), and one when it could not sign in with what it was given or shows a dialog
/// (TS_LOGON_ERRORS_INFO, 2.2.10.1.1.4.1.1). A sign-in screen that is simply waiting says nothing at all.
/// </summary>
/// <remarks>
/// IronRDP reads this PDU and drops it with a debug line, so it is read here as well, beside <see cref="RdpServerEnd"/>.
/// The logon info structures carry the user and domain name, which are never read.
/// </remarks>
/// <param name="Description">One sentence for the user and the probe. Never holds a host or user name.</param>
/// <param name="Code">Which notice and code it was, for the log.</param>
internal sealed record RdpLogonNotice(RdpLogonNoticeKind Kind, string Description, string Code)
{
	private const byte SaveSessionInfoType = 0x26;

	private const uint InfoTypeLogon = 0;
	private const uint InfoTypeLogonLong = 1;
	private const uint InfoTypeLogonPlainNotify = 2;
	private const uint InfoTypeLogonExtended = 3;

	private const uint FieldAutoReconnectCookie = 0x1;
	private const uint FieldLogonErrors = 0x2;

	// infoType (4), then TS_LOGON_INFO_EXTENDED's Length (2) and FieldsPresent (4) before its fields.
	private const int ExtendedFieldsOffset = 10;

	private const uint SessionBusyOptions = 0xFFFF_FFF8;
	private const uint DisconnectRefused = 0xFFFF_FFF9;
	private const uint NoPermission = 0xFFFF_FFFA;
	private const uint BumpOptions = 0xFFFF_FFFB;
	private const uint ReconnectOptions = 0xFFFF_FFFC;
	private const uint SessionTerminate = 0xFFFF_FFFD;
	private const uint SessionContinue = 0xFFFF_FFFE;
	private const uint AccessDenied = 0xFFFF_FFFF;

	private const uint FailedBadPassword = 0;
	private const uint FailedUpdatePassword = 1;
	private const uint Warning = 3;

	/// <summary>
	/// The notice in <paramref name="message"/>, or null when it is anything else, including an extended info that only
	/// carries an auto-reconnect cookie. Only the session's own channel counts. Never throws on short or malformed input.
	/// </summary>
	public static RdpLogonNotice? Read(ReadOnlySpan<byte> message, int ioChannelId)
	{
		if (!RdpIoChannel.TryReadShareData(message, ioChannelId, out byte pduType2, out ReadOnlySpan<byte> data)
			|| pduType2 != SaveSessionInfoType
			|| data.Length < 4)
		{
			return null;
		}

		uint infoType = BinaryPrimitives.ReadUInt32LittleEndian(data);
		return infoType switch
		{
			InfoTypeLogon => SignedIn("INFOTYPE_LOGON"),
			InfoTypeLogonLong => SignedIn("INFOTYPE_LOGON_LONG"),
			InfoTypeLogonPlainNotify => SignedIn("INFOTYPE_LOGON_PLAINNOTIFY"),
			InfoTypeLogonExtended => ReadExtended(data),
			_ => null,
		};
	}

	/// <summary>What TS_LOGON_ERRORS_INFO's two fields mean, worded for the user.</summary>
	/// <param name="type">ErrorNotificationType: an NTSTATUS, or one of the LOGON_MSG_* values for a Winlogon dialog.</param>
	/// <param name="data">ErrorNotificationData: a LOGON_FAILED_* value after a failure, a session id with a dialog.</param>
	public static RdpLogonNotice FromLogonError(uint type, uint data)
	{
		string code = string.Create(CultureInfo.InvariantCulture, $"Logon error {StatusName(type)}, data 0x{data:X8}");
		return type switch
		{
			SessionContinue => new(RdpLogonNoticeKind.Continuing, "Windows is going on with the sign-in.", code),
			SessionBusyOptions => Dialog("Windows says the session is busy and asks what to do.", code),
			DisconnectRefused => Dialog("Windows refused to disconnect the session that is signed in.", code),
			NoPermission => Dialog("Windows says this account has no permission to sign in remotely.", code),
			BumpOptions => Dialog("Another user is signed in, and Windows asks whether to disconnect them.", code),
			ReconnectOptions => Dialog("Windows asks whether to reconnect to the session that is already there.", code),
			SessionTerminate => Dialog("Windows is ending the session.", code),
			_ => new(RdpLogonNoticeKind.SignInFailed, Failure(data) + " (" + StatusName(type) + ").", code),
		};
	}

	private static RdpLogonNotice? ReadExtended(ReadOnlySpan<byte> data)
	{
		if (data.Length < ExtendedFieldsOffset)
		{
			return null;
		}

		uint present = BinaryPrimitives.ReadUInt32LittleEndian(data[6..]);
		ReadOnlySpan<byte> fields = data[ExtendedFieldsOffset..];

		// The fields come in the order of their flags, each behind its own cbFieldData.
		if ((present & FieldAutoReconnectCookie) != 0)
		{
			if (fields.Length < 4)
			{
				return null;
			}

			uint length = BinaryPrimitives.ReadUInt32LittleEndian(fields);
			if (length > (uint)(fields.Length - 4))
			{
				return null;
			}

			fields = fields[(4 + (int)length)..];
		}

		if ((present & FieldLogonErrors) == 0 || fields.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(fields) < 8)
		{
			return null;
		}

		return FromLogonError(BinaryPrimitives.ReadUInt32LittleEndian(fields[4..]), BinaryPrimitives.ReadUInt32LittleEndian(fields[8..]));
	}

	private static RdpLogonNotice SignedIn(string code) => new(RdpLogonNoticeKind.SignedIn, "Windows signed the user in.", code);

	private static RdpLogonNotice Dialog(string description, string code) => new(RdpLogonNoticeKind.Dialog, description, code);

	private static string Failure(uint data) => data switch
	{
		FailedBadPassword => "Windows refused the user name or the password and shows its sign-in screen",
		FailedUpdatePassword => "Windows wants the password changed before it signs in, on its sign-in screen",
		Warning => "Windows showed a warning while signing in, on its sign-in screen",
		// LOGON_FAILED_OTHER, or a value the specification does not define.
		_ => "Windows could not sign in and shows its sign-in screen",
	};

	/// <summary>The NTSTATUS codes a sign-in usually fails with, by name; any other as hex.</summary>
	private static string StatusName(uint type) => type switch
	{
		AccessDenied => "ERROR_CODE_ACCESS_DENIED",
		SessionBusyOptions => "LOGON_MSG_SESSION_BUSY_OPTIONS",
		DisconnectRefused => "LOGON_MSG_DISCONNECT_REFUSED",
		NoPermission => "LOGON_MSG_NO_PERMISSION",
		BumpOptions => "LOGON_MSG_BUMP_OPTIONS",
		ReconnectOptions => "LOGON_MSG_RECONNECT_OPTIONS",
		SessionTerminate => "LOGON_MSG_SESSION_TERMINATE",
		SessionContinue => "LOGON_MSG_SESSION_CONTINUE",
		0xC000_0064 => "STATUS_NO_SUCH_USER",
		0xC000_006A => "STATUS_WRONG_PASSWORD",
		0xC000_006D => "STATUS_LOGON_FAILURE",
		0xC000_006E => "STATUS_ACCOUNT_RESTRICTION",
		0xC000_006F => "STATUS_INVALID_LOGON_HOURS",
		0xC000_0070 => "STATUS_INVALID_WORKSTATION",
		0xC000_0071 => "STATUS_PASSWORD_EXPIRED",
		0xC000_0072 => "STATUS_ACCOUNT_DISABLED",
		0xC000_015B => "STATUS_LOGON_TYPE_NOT_GRANTED",
		0xC000_0193 => "STATUS_ACCOUNT_EXPIRED",
		0xC000_0224 => "STATUS_PASSWORD_MUST_CHANGE",
		0xC000_0234 => "STATUS_ACCOUNT_LOCKED_OUT",
		_ => string.Create(CultureInfo.InvariantCulture, $"0x{type:X8}"),
	};
}
