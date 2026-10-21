using Mokaterm.Modules.Rdp.Protocol;
using Mokaterm.Modules.Rdp.Tests.Fakes;

namespace Mokaterm.Modules.Rdp.Tests;

/// <summary>Reading what Windows says about signing in, from Save Session Info PDUs laid out as MS-RDPBCGR 2.2.10.1 has them.</summary>
public sealed class RdpLogonNoticeTests
{
	private const int IoChannel = FakeStage.DefaultIoChannel;

	// INFOTYPE_LOGON, INFOTYPE_LOGON_LONG and INFOTYPE_LOGON_PLAINNOTIFY, each with its 576 bytes of data.
	[Theory]
	[InlineData(0u, "INFOTYPE_LOGON")]
	[InlineData(1u, "INFOTYPE_LOGON_LONG")]
	[InlineData(2u, "INFOTYPE_LOGON_PLAINNOTIFY")]
	public void Read_ALogonNotification_IsASignIn(uint infoType, string code)
	{
		RdpLogonNotice? notice = RdpLogonNotice.Read(RdpPdus.SaveSessionInfo(infoType, new byte[576]), IoChannel);

		Assert.NotNull(notice);
		Assert.Equal(RdpLogonNoticeKind.SignedIn, notice.Kind);
		Assert.Equal("Windows signed the user in.", notice.Description);
		Assert.Equal(code, notice.Code);
	}

	[Fact]
	public void Read_AWrongPassword_IsAFailedSignIn()
	{
		RdpLogonNotice? notice = RdpLogonNotice.Read(RdpPdus.LogonErrors(0xC000_006D, 0), IoChannel);

		Assert.NotNull(notice);
		Assert.Equal(RdpLogonNoticeKind.SignInFailed, notice.Kind);
		Assert.Equal(
			"Windows refused the user name or the password and shows its sign-in screen (STATUS_LOGON_FAILURE).",
			notice.Description);
		Assert.Equal("Logon error STATUS_LOGON_FAILURE, data 0x00000000", notice.Code);
	}

	[Fact]
	public void Read_LogonErrorsAfterAnAutoReconnectCookie_AreStillFound()
	{
		RdpLogonNotice? notice = RdpLogonNotice.Read(RdpPdus.LogonErrors(0xC000_0224, 1, withCookie: true), IoChannel);

		Assert.NotNull(notice);
		Assert.Equal(RdpLogonNoticeKind.SignInFailed, notice.Kind);
		Assert.StartsWith("Windows wants the password changed", notice.Description, StringComparison.Ordinal);
		Assert.Contains("STATUS_PASSWORD_MUST_CHANGE", notice.Description, StringComparison.Ordinal);
	}

	[Fact]
	public void Read_AStatusWithoutAName_IsWrittenInHex()
	{
		RdpLogonNotice? notice = RdpLogonNotice.Read(RdpPdus.LogonErrors(0xC000_1234, 2), IoChannel);

		Assert.NotNull(notice);
		Assert.Equal("Windows could not sign in and shows its sign-in screen (0xC0001234).", notice.Description);
	}

	// What FreeRDP logs on almost every Windows connection: the logon goes on, with the session id as the data.
	[Fact]
	public void Read_SessionContinue_SaysNothingAboutHowItEnds()
	{
		RdpLogonNotice? notice = RdpLogonNotice.Read(RdpPdus.LogonErrors(0xFFFF_FFFE, 2), IoChannel);

		Assert.NotNull(notice);
		Assert.Equal(RdpLogonNoticeKind.Continuing, notice.Kind);
	}

	[Theory]
	[InlineData(0xFFFF_FFF8u, "busy")]
	[InlineData(0xFFFF_FFF9u, "refused to disconnect")]
	[InlineData(0xFFFF_FFFAu, "no permission")]
	[InlineData(0xFFFF_FFFBu, "Another user is signed in")]
	[InlineData(0xFFFF_FFFCu, "reconnect")]
	[InlineData(0xFFFF_FFFDu, "ending the session")]
	public void Read_AWinlogonDialog_SaysWhichOne(uint type, string words)
	{
		RdpLogonNotice? notice = RdpLogonNotice.Read(RdpPdus.LogonErrors(type, 3), IoChannel);

		Assert.NotNull(notice);
		Assert.Equal(RdpLogonNoticeKind.Dialog, notice.Kind);
		Assert.Contains(words, notice.Description, StringComparison.Ordinal);
	}

	[Fact]
	public void Read_AnAccessDenied_IsAFailedSignIn()
	{
		RdpLogonNotice? notice = RdpLogonNotice.Read(RdpPdus.LogonErrors(0xFFFF_FFFF, 0), IoChannel);

		Assert.NotNull(notice);
		Assert.Equal(RdpLogonNoticeKind.SignInFailed, notice.Kind);
		Assert.EndsWith("(ERROR_CODE_ACCESS_DENIED).", notice.Description, StringComparison.Ordinal);
	}

	[Fact]
	public void Read_AnExtendedInfoWithOnlyACookie_IsNoNotice()
	{
		byte[] extended = new byte[6 + 32 + 570];
		extended[0] = 6 + 32;
		extended[2] = 0x1;
		extended[6] = 28;
		extended[10] = 28;

		Assert.Null(RdpLogonNotice.Read(RdpPdus.SaveSessionInfo(3, extended), IoChannel));
	}

	[Fact]
	public void Read_EverythingElse_IsNoNotice()
	{
		Assert.Null(RdpLogonNotice.Read(RdpPdus.SaveSessionInfo(1, new byte[576], channel: IoChannel + 1), IoChannel));
		Assert.Null(RdpLogonNotice.Read(RdpPdus.SaveSessionInfo(7, new byte[576]), IoChannel));
		Assert.Null(RdpLogonNotice.Read(RdpPdus.SetErrorInfo(RdpErrorInfo.LogoffByUser), IoChannel));
		Assert.Null(RdpLogonNotice.Read(RdpPdus.Ultimatum(3), IoChannel));
		Assert.Null(RdpLogonNotice.Read([0x00, 0x08, 0x21, 0x80, 0x00, 0x00, 0x00, 0x00], IoChannel));
	}

	[Fact]
	public void Read_AMessageCutAnywhere_NeverThrows()
	{
		byte[] whole = RdpPdus.LogonErrors(0xC000_006D, 0, withCookie: true);
		for (int length = 0; length < whole.Length; length++)
		{
			_ = RdpLogonNotice.Read(whole.AsSpan(0, length), IoChannel);
		}

		// A cookie whose length claims more than the message holds.
		byte[] lying = RdpPdus.LogonErrors(0xC000_006D, 0, withCookie: true);
		int cookieLength = lying.Length - 570 - 12 - 32;
		lying[cookieLength] = 0xFF;
		lying[cookieLength + 1] = 0xFF;
		Assert.Null(RdpLogonNotice.Read(lying, IoChannel));
	}
}
