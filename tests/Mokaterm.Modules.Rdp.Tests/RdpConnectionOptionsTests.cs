using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Rdp.Tests;

public sealed class RdpConnectionOptionsTests
{
	[Fact]
	public void From_AnEmptyMap_GivesTheDefaults()
	{
		RdpConnectionOptions options = RdpConnectionOptions.From(ProtocolOptions.Empty);

		Assert.Equal(RdpConnectionOptions.Default, options);
		Assert.True(options.NetworkLevelAuthentication);
		Assert.Equal("", options.Domain);
		Assert.Equal(RdpDesktopSizeMode.Automatic, options.DesktopSize);
		Assert.Equal(RdpScalingMode.Fit, options.Scaling);
		Assert.False(options.ViewOnly);
		Assert.Equal(RdpKeyboardLayout.ServerDefault, options.KeyboardLayout);
		Assert.Equal(RdpPerformanceOptions.Default, options.Performance);
	}

	[Fact]
	public void ApplyTo_TheDefaults_WritesOnlyWhatIsNotTheDefault()
	{
		ProtocolOptions written = RdpConnectionOptions.Default.ApplyTo(ProtocolOptions.Empty);

		Assert.False(written.ContainsKey(RdpConnectionOptions.NetworkLevelAuthenticationKey));
		Assert.False(written.ContainsKey(RdpConnectionOptions.DomainKey));
		Assert.False(written.ContainsKey(RdpConnectionOptions.ViewOnlyKey));
		Assert.False(written.ContainsKey(RdpConnectionOptions.KeyboardLayoutKey));
		Assert.False(written.ContainsKey(RdpConnectionOptions.WallpaperKey));
		Assert.False(written.ContainsKey(RdpConnectionOptions.FontSmoothingKey));
	}

	[Fact]
	public void ApplyTo_ThenFrom_KeepsEveryValue()
	{
		RdpConnectionOptions options = new()
		{
			NetworkLevelAuthentication = false,
			Domain = "CORP",
			DesktopSize = RdpDesktopSizeMode.Fixed,
			Width = 1280,
			Height = 800,
			Scaling = RdpScalingMode.Remote,
			ViewOnly = true,
			KeyboardLayout = 0x0407,
			Performance = new RdpPerformanceOptions { Wallpaper = true, Themes = true, FontSmoothing = false, FullWindowDrag = true },
		};

		Assert.Equal(options, RdpConnectionOptions.From(options.ApplyTo(ProtocolOptions.Empty)));
	}

	[Fact]
	public void ApplyTo_KeepsKeysThatBelongToSomethingElse()
	{
		ProtocolOptions other = ProtocolOptions.Empty.With("ssh.compression", "true");

		ProtocolOptions written = RdpConnectionOptions.Default.ApplyTo(other);

		Assert.Equal("true", written.GetString("ssh.compression"));
	}

	[Fact]
	public void ApplyTo_TrimsTheDomain()
	{
		ProtocolOptions written = (RdpConnectionOptions.Default with { Domain = "  CORP  " }).ApplyTo(ProtocolOptions.Empty);

		Assert.Equal("CORP", written.GetString(RdpConnectionOptions.DomainKey));
	}

	[Theory]
	[InlineData(0, RdpConnectionOptions.MinDesktopSize)]
	[InlineData(-100, RdpConnectionOptions.MinDesktopSize)]
	[InlineData(99999, RdpConnectionOptions.MaxDesktopSize)]
	[InlineData(1280, 1280)]
	public void From_ClampsTheDesktopSize(int stored, int expected)
	{
		ProtocolOptions options = ProtocolOptions.Empty
			.With(RdpConnectionOptions.WidthKey, stored)
			.With(RdpConnectionOptions.HeightKey, stored);

		RdpConnectionOptions parsed = RdpConnectionOptions.From(options);

		Assert.Equal(expected, parsed.Width);
		Assert.Equal(expected, parsed.Height);
	}

	[Theory]
	[InlineData("99")]
	[InlineData("Sideways")]
	[InlineData("")]
	public void From_FallsBackWhenAnEnumValueMakesNoSense(string stored)
	{
		ProtocolOptions options = ProtocolOptions.Empty
			.With(RdpConnectionOptions.ScalingKey, stored)
			.With(RdpConnectionOptions.DesktopSizeKey, stored);

		RdpConnectionOptions parsed = RdpConnectionOptions.From(options);

		Assert.Equal(RdpScalingMode.Fit, parsed.Scaling);
		Assert.Equal(RdpDesktopSizeMode.Automatic, parsed.DesktopSize);
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(0x10000)]
	public void From_DropsAKeyboardLayoutOutsideTheRange(int stored)
	{
		ProtocolOptions options = ProtocolOptions.Empty.With(RdpConnectionOptions.KeyboardLayoutKey, stored);

		Assert.Equal(RdpKeyboardLayout.ServerDefault, RdpConnectionOptions.From(options).KeyboardLayout);
	}

	[Fact]
	public void From_ReadsThePerformanceFlags()
	{
		ProtocolOptions options = ProtocolOptions.Empty
			.With(RdpConnectionOptions.WallpaperKey, true)
			.With(RdpConnectionOptions.ThemesKey, true)
			.With(RdpConnectionOptions.FontSmoothingKey, false)
			.With(RdpConnectionOptions.FullWindowDragKey, true);

		RdpPerformanceOptions performance = RdpConnectionOptions.From(options).Performance;

		Assert.True(performance.Wallpaper);
		Assert.True(performance.Themes);
		Assert.False(performance.FontSmoothing);
		Assert.True(performance.FullWindowDrag);
	}

	[Fact]
	public void From_RejectsNull() => Assert.Throws<ArgumentNullException>(() => RdpConnectionOptions.From(null!));

	[Fact]
	public void KeyboardLayoutFor_NamesAnUnknownLayoutAfterItsNumber()
	{
		RdpKeyboardLayout layout = RdpKeyboardLayout.For(0x0C0C);

		Assert.Equal(0x0C0C, layout.Id);
		Assert.Contains("0C0C", layout.Name, StringComparison.Ordinal);
	}

	[Fact]
	public void KeyboardLayoutFor_AKnownLayout_ReturnsTheListedOne() =>
		Assert.Same(RdpKeyboardLayout.Common.First(layout => layout.Id == 0x0409), RdpKeyboardLayout.For(0x0409));
}
