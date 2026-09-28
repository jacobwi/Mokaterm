using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Vnc.Tests;

public sealed class VncConnectionOptionsTests
{
	[Fact]
	public void From_Empty_ReturnsTheDefaults()
	{
		VncConnectionOptions options = VncConnectionOptions.From(ProtocolOptions.Empty);

		Assert.True(options.Shared);
		Assert.False(options.ViewOnly);
		Assert.Equal(VncScalingMode.Fit, options.Scaling);
		Assert.Equal(VncConnectionOptions.DefaultQuality, options.Quality);
		Assert.Equal(VncConnectionOptions.DefaultCompression, options.Compression);
		Assert.Equal(VncEncryptionMode.Preferred, options.Encryption);
	}

	[Fact]
	public void ApplyTo_RoundTripsEveryValue()
	{
		VncConnectionOptions original = new()
		{
			Shared = false,
			ViewOnly = true,
			Scaling = VncScalingMode.Remote,
			Quality = 9,
			Compression = 0,
			Encryption = VncEncryptionMode.Required,
		};

		VncConnectionOptions round = VncConnectionOptions.From(original.ApplyTo(ProtocolOptions.Empty));

		Assert.Equal(original, round);
	}

	[Fact]
	public void ApplyTo_KeepsKeysOfOtherModules()
	{
		ProtocolOptions options = ProtocolOptions.Empty.With("ssh.compression", "true");

		ProtocolOptions written = VncConnectionOptions.Default.ApplyTo(options);

		Assert.Equal("true", written.GetString("ssh.compression"));
	}

	[Fact]
	public void ApplyTo_DefaultsWriteNoSwitchKeys()
	{
		ProtocolOptions written = VncConnectionOptions.Default.ApplyTo(ProtocolOptions.Empty);

		Assert.False(written.ContainsKey(VncConnectionOptions.SharedKey));
		Assert.False(written.ContainsKey(VncConnectionOptions.ViewOnlyKey));
	}

	[Fact]
	public void From_LevelsOutsideTheRange_AreClamped()
	{
		ProtocolOptions stored = ProtocolOptions.Empty
			.With(VncConnectionOptions.QualityKey, 42)
			.With(VncConnectionOptions.CompressionKey, -3);

		VncConnectionOptions options = VncConnectionOptions.From(stored);

		Assert.Equal(VncConnectionOptions.MaxLevel, options.Quality);
		Assert.Equal(VncConnectionOptions.MinLevel, options.Compression);
	}

	[Fact]
	public void From_UndefinedEnumValues_FallBackToTheDefault()
	{
		ProtocolOptions stored = ProtocolOptions.Empty
			.With(VncConnectionOptions.ScalingKey, "sideways")
			.With(VncConnectionOptions.EncryptionKey, "7");

		VncConnectionOptions options = VncConnectionOptions.From(stored);

		Assert.Equal(VncScalingMode.Fit, options.Scaling);
		Assert.Equal(VncEncryptionMode.Preferred, options.Encryption);
	}

	[Fact]
	public void From_ANumberThatHappensToBeAMode_IsStillNotOne()
	{
		// ProtocolOptions.GetEnum takes the name of a mode and nothing else; 1 is Actual, and nobody ever stored "1".
		ProtocolOptions stored = ProtocolOptions.Empty.With(VncConnectionOptions.ScalingKey, "1");

		Assert.Equal(VncScalingMode.Fit, VncConnectionOptions.From(stored).Scaling);
	}
}
