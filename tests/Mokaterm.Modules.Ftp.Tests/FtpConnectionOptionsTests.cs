using System.Text.Json;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Ftp.Tests;

public sealed class FtpConnectionOptionsTests
{
	[Fact]
	public void From_EmptyOptions_UsesDefaults()
	{
		FtpConnectionOptions options = FtpConnectionOptions.From(ProtocolOptions.Empty);

		Assert.Equal(FtpEncryption.None, options.Encryption);
		Assert.Equal(FtpDataConnectionMode.Passive, options.DataConnection);
		Assert.Equal("utf-8", options.EncodingName);
		Assert.Null(options.InitialDirectory);
		Assert.Equal(FtpConnectionOptions.Default, options);
	}

	[Fact]
	public void ApplyTo_ThenFrom_RoundTrips()
	{
		FtpConnectionOptions original = new()
		{
			Encryption = FtpEncryption.Implicit,
			DataConnection = FtpDataConnectionMode.ExtendedPassive,
			EncodingName = "windows-1252",
			InitialDirectory = "public_html",
		};

		ProtocolOptions stored = original.ApplyTo(ProtocolOptions.Empty);

		Assert.Equal("Implicit", stored[FtpConnectionOptions.EncryptionKey]);
		Assert.Equal("ExtendedPassive", stored[FtpConnectionOptions.DataConnectionKey]);
		Assert.Equal("windows-1252", stored[FtpConnectionOptions.EncodingKey]);
		Assert.Equal("public_html", stored[FtpConnectionOptions.InitialDirectoryKey]);
		Assert.Equal(original, FtpConnectionOptions.From(stored));
	}

	[Fact]
	public void RoundTrip_ThroughJson_KeepsValues()
	{
		FtpConnectionOptions original = new() { Encryption = FtpEncryption.Explicit, DataConnection = FtpDataConnectionMode.Active, InitialDirectory = "/srv/ftp" };

		string json = JsonSerializer.Serialize(original.ApplyTo(ProtocolOptions.Empty));
		ProtocolOptions? restored = JsonSerializer.Deserialize<ProtocolOptions>(json);

		Assert.NotNull(restored);
		Assert.Equal(original, FtpConnectionOptions.From(restored));
	}

	[Fact]
	public void ApplyTo_ThenFrom_RoundTripsTheProxy()
	{
		FtpConnectionOptions original = FtpConnectionOptions.Default with
		{
			Proxy = new ProxyOptions { Kind = ProxyKind.Http, Host = "proxy.example.com", Port = 3128, User = "bob" },
		};

		ProtocolOptions stored = original.ApplyTo(ProtocolOptions.Empty);

		Assert.Equal("Http", stored["ftp.proxy.kind"]);
		Assert.Equal("proxy.example.com", stored["ftp.proxy.host"]);
		Assert.Equal("3128", stored["ftp.proxy.port"]);
		Assert.Equal("bob", stored["ftp.proxy.user"]);
		Assert.Equal(original, FtpConnectionOptions.From(stored));
	}

	[Fact]
	public void ApplyTo_ProxyTurnedOff_RemovesTheWholeGroup()
	{
		ProtocolOptions existing = (FtpConnectionOptions.Default with
		{
			Proxy = new ProxyOptions { Kind = ProxyKind.Socks5, Host = "proxy", Port = 1081, User = "bob" },
		}).ApplyTo(ProtocolOptions.Empty);

		ProtocolOptions stored = FtpConnectionOptions.Default.ApplyTo(existing);

		Assert.DoesNotContain(stored.Keys, key => key.StartsWith(FtpConnectionOptions.ProxyKeyPrefix, StringComparison.Ordinal));
		Assert.Equal(ProxyOptions.None, FtpConnectionOptions.From(stored).Proxy);
	}

	[Fact]
	public void From_ProxyWithoutAKind_IsOff()
	{
		ProtocolOptions options = ProtocolOptions.Empty.With("ftp.proxy.host", "proxy").With("ftp.proxy.user", "bob");

		Assert.Equal(ProxyOptions.None, FtpConnectionOptions.From(options).Proxy);
	}

	[Fact]
	public void ApplyTo_KeepsKeysOwnedByOthers()
	{
		ProtocolOptions existing = ProtocolOptions.Empty.With("ssh.keepAliveSeconds", 30);

		ProtocolOptions stored = FtpConnectionOptions.Default.ApplyTo(existing);

		Assert.Equal("30", stored["ssh.keepAliveSeconds"]);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void ApplyTo_BlankInitialDirectory_RemovesKey(string? initialDirectory)
	{
		ProtocolOptions existing = ProtocolOptions.Empty.With(FtpConnectionOptions.InitialDirectoryKey, "/old");

		ProtocolOptions stored = (FtpConnectionOptions.Default with { InitialDirectory = initialDirectory }).ApplyTo(existing);

		Assert.False(stored.ContainsKey(FtpConnectionOptions.InitialDirectoryKey));
	}

	[Fact]
	public void ApplyTo_BlankEncoding_ReadsBackAsDefault()
	{
		ProtocolOptions stored = (FtpConnectionOptions.Default with { EncodingName = " " }).ApplyTo(ProtocolOptions.Empty);

		Assert.False(stored.ContainsKey(FtpConnectionOptions.EncodingKey));
		Assert.Equal("utf-8", FtpConnectionOptions.From(stored).EncodingName);
	}

	[Theory]
	[InlineData("explicit", FtpEncryption.Explicit)]
	[InlineData("IMPLICIT", FtpEncryption.Implicit)]
	[InlineData("Bogus", FtpEncryption.None)]
	[InlineData("7", FtpEncryption.None)]
	[InlineData("Explicit, Implicit", FtpEncryption.None)]
	public void From_UnexpectedEncryptionValues_FallBackToNone(string stored, FtpEncryption expected)
	{
		ProtocolOptions options = ProtocolOptions.Empty.With(FtpConnectionOptions.EncryptionKey, stored);

		Assert.Equal(expected, FtpConnectionOptions.From(options).Encryption);
	}

	[Fact]
	public void From_UnknownDataConnection_FallsBackToPassive()
	{
		ProtocolOptions options = ProtocolOptions.Empty.With(FtpConnectionOptions.DataConnectionKey, "Carrier pigeon");

		Assert.Equal(FtpDataConnectionMode.Passive, FtpConnectionOptions.From(options).DataConnection);
	}
}
