using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Local.Tests;

public sealed class LocalConnectionOptionsTests
{
	[Fact]
	public void ApplyToThenFrom_RoundTripsEveryValue()
	{
		LocalConnectionOptions options = new()
		{
			Shell = "custom",
			Program = @"C:\tools\nu.exe",
			Arguments = "--login -c \"ls -la\"",
			StartingFolder = @"~\src",
			EnvironmentVariables = "EDITOR=hx\nPAGER=",
		};

		LocalConnectionOptions read = LocalConnectionOptions.From(options.ApplyTo(ProtocolOptions.Empty));

		Assert.Equal(options, read);
		Assert.True(read.IsCustom);
	}

	[Fact]
	public void ApplyTo_KeepsKeysThatBelongToAnythingElse_AndDropsBlankValues()
	{
		ProtocolOptions existing = ProtocolOptions.Empty.With("ssh.keepAlive", 30).With(LocalConnectionOptions.FolderKey, @"C:\old");

		ProtocolOptions written = new LocalConnectionOptions { Shell = "cmd", StartingFolder = "   " }.ApplyTo(existing);

		Assert.Equal("30", written.GetString("ssh.keepAlive"));
		Assert.Equal("cmd", written.GetString(LocalConnectionOptions.ShellKey));

		// Blank means "the default", which is no key at all.
		Assert.False(written.ContainsKey(LocalConnectionOptions.FolderKey));
		Assert.False(written.ContainsKey(LocalConnectionOptions.ArgumentsKey));
	}

	[Fact]
	public void From_KeepsValuesAsTyped()
	{
		// The editor reports every keystroke, so a space typed between two arguments must survive the round trip.
		ProtocolOptions stored = ProtocolOptions.Empty.With(LocalConnectionOptions.ArgumentsKey, "-NoLogo ");

		Assert.Equal("-NoLogo ", LocalConnectionOptions.From(stored).Arguments);
	}

	[Fact]
	public void Default_HasNoOverrides()
	{
		LocalConnectionOptions options = LocalConnectionOptions.From(ProtocolOptions.Empty);

		Assert.Equal(LocalConnectionOptions.Default, options);
		Assert.Null(options.Shell);
		Assert.False(options.IsCustom);
		Assert.Empty(options.ApplyTo(ProtocolOptions.Empty));
	}

	[Theory]
	[InlineData("\"C:\\Program Files\\nu\\nu.exe\"", "C:\\Program Files\\nu\\nu.exe")]
	[InlineData("  nu.exe  ", "nu.exe")]
	[InlineData("\" \"", null)]
	[InlineData("   ", null)]
	public void ProgramPath_TrimsAndTakesOffCopyAsPathQuotes(string typed, string? expected)
	{
		Assert.Equal(expected, LocalConnectionOptions.ProgramPath(typed));
	}

	[Fact]
	public void ToString_SaysWhetherArgumentsAndVariablesAreSet_ButNotWhatTheyAre()
	{
		LocalConnectionOptions options = new()
		{
			Shell = "pwsh",
			Arguments = "-Token s3cret-arg",
			EnvironmentVariables = "API_TOKEN=s3cret-env",
		};

		string printed = options.ToString();

		Assert.Contains("pwsh", printed, StringComparison.Ordinal);
		Assert.DoesNotContain("s3cret", printed, StringComparison.Ordinal);
		Assert.Contains("(set)", printed, StringComparison.Ordinal);
	}

	[Fact]
	public void SettingsClamped_DropsACustomDefaultAndTrims()
	{
		LocalSettings settings = new LocalSettings { DefaultShell = " custom ", StartingFolder = @"  D:\work  " }.Clamped();

		Assert.Equal("", settings.DefaultShell);
		Assert.Equal(@"D:\work", settings.StartingFolder);
		Assert.True(settings.ReportExitCode);
	}

	[Fact]
	public void SettingsClamped_TakesNullsFromAHandEditedFile()
	{
		LocalSettings settings = new LocalSettings { DefaultShell = null!, StartingFolder = null! }.Clamped();

		Assert.Equal("", settings.DefaultShell);
		Assert.Equal("", settings.StartingFolder);
	}
}
