using System.Security.Authentication;
using System.Text;
using FluentFTP;
using Mokaterm.Modules.Ftp.Connection;

namespace Mokaterm.Modules.Ftp.Tests;

public sealed class FtpSettingsTests
{
	[Fact]
	public void Defaults_MatchTheDocumentedValues()
	{
		FtpSettings settings = new();

		Assert.Equal("ftp", FtpSettings.SectionKey);
		Assert.Equal(15, settings.ConnectTimeoutSeconds);
		Assert.Equal(30, settings.DataTimeoutSeconds);
		Assert.Equal(60, settings.KeepAliveSeconds);
		Assert.Equal(3, settings.AuthenticationAttempts);
		Assert.Equal(settings, settings.Clamped());
	}

	[Fact]
	public void Clamped_PullsValuesIntoRange()
	{
		FtpSettings clamped = new FtpSettings { ConnectTimeoutSeconds = 0, DataTimeoutSeconds = 99_999, KeepAliveSeconds = -5, AuthenticationAttempts = 0 }.Clamped();

		Assert.Equal(FtpSettings.MinTimeoutSeconds, clamped.ConnectTimeoutSeconds);
		Assert.Equal(FtpSettings.MaxDataTimeoutSeconds, clamped.DataTimeoutSeconds);
		Assert.Equal(0, clamped.KeepAliveSeconds);
		Assert.Equal(1, clamped.AuthenticationAttempts);
	}

	[Theory]
	[InlineData(FtpEncryption.None, FtpEncryptionMode.None)]
	[InlineData(FtpEncryption.Explicit, FtpEncryptionMode.Explicit)]
	[InlineData(FtpEncryption.Implicit, FtpEncryptionMode.Implicit)]
	public void CreateConfig_MapsEncryption(FtpEncryption encryption, FtpEncryptionMode expected)
	{
		FtpConfig config = CreateOptions(new FtpConnectionOptions { Encryption = encryption }).CreateConfig();

		Assert.Equal(expected, config.EncryptionMode);
	}

	[Theory]
	[InlineData(FtpDataConnectionMode.Passive, FtpDataConnectionType.PASVEX)]
	[InlineData(FtpDataConnectionMode.ExtendedPassive, FtpDataConnectionType.EPSV)]
	[InlineData(FtpDataConnectionMode.Active, FtpDataConnectionType.AutoActive)]
	public void CreateConfig_MapsDataConnection(FtpDataConnectionMode mode, FtpDataConnectionType expected)
	{
		FtpConfig config = CreateOptions(new FtpConnectionOptions { DataConnection = mode }).CreateConfig();

		Assert.Equal(expected, config.DataConnectionType);
	}

	[Fact]
	public void CreateConfig_AppliesTimeoutsAndSafeDefaults()
	{
		FtpClientOptions options = FtpClientOptions.Create("ftp.example.test", 21, FtpConnectionOptions.Default, new FtpSettings { ConnectTimeoutSeconds = 7, DataTimeoutSeconds = 45 });

		FtpConfig config = options.CreateConfig();

		Assert.Equal(7000, config.ConnectTimeout);
		Assert.Equal(7000, config.DataConnectionConnectTimeout);
		Assert.Equal(45000, config.ReadTimeout);
		Assert.Equal(45000, config.DataConnectionReadTimeout);
		Assert.Equal(45000, config.WriteTimeout);
		Assert.Equal(45000, config.DataConnectionWriteTimeout);
		Assert.Equal(SslProtocols.None, config.SslProtocols);
		Assert.False(config.ValidateAnyCertificate);
		Assert.False(config.Noop);
		Assert.False(config.LogPassword);
		Assert.False(config.LogUserName);
		Assert.False(config.LogHost);
	}

	[Fact]
	public void Create_ResolvesCharacterSet()
	{
		FtpClientOptions options = CreateOptions(new FtpConnectionOptions { EncodingName = "windows-1252" });

		Assert.Equal(1252, options.Encoding.CodePage);
	}

	[Theory]
	[InlineData("utf-8")]
	[InlineData(" UTF-8 ")]
	public void TryGet_Utf8_ReturnsTheSharedInstanceFluentFtpChecksFor(string name)
	{
		Assert.True(FtpEncodings.TryGet(name, out Encoding? encoding));
		Assert.Same(Encoding.UTF8, encoding);
	}

	[Theory]
	[InlineData("windows-1252", 1252)]
	[InlineData("iso-8859-1", 28591)]
	[InlineData("shift_jis", 932)]
	public void TryGet_KnowsLegacyCodePages(string name, int codePage)
	{
		Assert.True(FtpEncodings.TryGet(name, out Encoding? encoding));
		Assert.Equal(codePage, encoding.CodePage);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("klingon-9000")]
	public void TryGet_RejectsBlankAndUnknownNames(string? name)
	{
		Assert.False(FtpEncodings.TryGet(name, out _));
		Assert.Same(Encoding.UTF8, FtpEncodings.Resolve(name));
	}

	private static FtpClientOptions CreateOptions(FtpConnectionOptions connection) =>
		FtpClientOptions.Create("ftp.example.test", 21, connection, new FtpSettings());
}
