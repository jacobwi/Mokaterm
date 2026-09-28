using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.FileSystem;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests;

public sealed class SmallHelpersTests
{
	[Theory]
	[InlineData("Password: ", true)]
	[InlineData("abc@web01's password:", true)]
	[InlineData("Password for abc@example.com: ", true)]
	[InlineData("Verification code: ", false)]
	[InlineData("Enter PASSCODE:", false)]
	[InlineData("New password: ", false)]
	[InlineData("Retype new password: ", false)]
	public void IsPasswordPrompt_OnlyMatchesTheLoginPassword(string request, bool expected) =>
		Assert.Equal(expected, SshLogin.IsPasswordPrompt(request));

	[Theory]
	[InlineData("", "/home/abc")]
	[InlineData("~", "/home/abc")]
	[InlineData("~/sites/app", "/home/abc/sites/app")]
	[InlineData("logs", "/home/abc/logs")]
	[InlineData("/etc/nginx", "/etc/nginx")]
	[InlineData("~other", "/home/abc/~other")]
	public void ExpandHome_ResolvesAgainstTheHomeDirectory(string path, string expected) =>
		Assert.Equal(expected, SftpFileSystem.ExpandHome(path, "/home/abc"));

	[Theory]
	[InlineData("sudo", true)]
	[InlineData("/usr/local/bin/sudo", true)]
	[InlineData("doas", true)]
	[InlineData("sudo -E", false)]
	[InlineData("", false)]
	[InlineData("su'do", false)]
	public void IsValidSudoCommand_AcceptsOneCommandWord(string command, bool expected) =>
		Assert.Equal(expected, SshSettings.IsValidSudoCommand(command));

	[Theory]
	[InlineData("/tmp", true)]
	[InlineData("/var/tmp/mokaterm", true)]
	[InlineData("tmp", false)]
	[InlineData("", false)]
	public void IsValidStagingDirectory_RequiresAnAbsolutePath(string directory, bool expected) =>
		Assert.Equal(expected, SshSettings.IsValidStagingDirectory(directory));

	[Fact]
	public void Settings_ClampOutOfRangeValues()
	{
		SshSettings settings = new()
		{
			ConnectTimeoutSeconds = 0,
			PromptTimeoutSeconds = 1_000_000,
			AuthenticationAttempts = -3,
			SudoCommand = "sudo -i",
			StagingDirectory = "relative",
			KeepAliveSeconds = 0,
		};

		Assert.Equal(TimeSpan.FromSeconds(SshSettings.MinConnectTimeoutSeconds), settings.ConnectTimeout);
		Assert.Equal(TimeSpan.FromSeconds(SshSettings.MaxPromptTimeoutSeconds), settings.PromptTimeout);
		Assert.Equal(SshSettings.MinAuthenticationAttempts, settings.EffectiveAuthenticationAttempts);
		Assert.Equal("sudo", settings.EffectiveSudoCommand);
		Assert.Equal("/tmp", settings.EffectiveStagingDirectory);
		Assert.Null(settings.GetKeepAlive(new SshConnectionOptions()));
		Assert.Equal(TimeSpan.FromSeconds(20), settings.GetKeepAlive(new SshConnectionOptions { KeepAliveSeconds = 20 }));
		Assert.Null(new SshSettings().GetKeepAlive(new SshConnectionOptions { KeepAliveSeconds = 0 }));
	}

	[Fact]
	public void ConnectWatchdog_TimesOutOnlyWhileArmed()
	{
		using ConnectWatchdog watchdog = new(TimeSpan.FromMilliseconds(50));
		watchdog.Suspend();
		Thread.Sleep(150);
		Assert.False(watchdog.TimedOut);

		watchdog.Arm();
		Assert.True(watchdog.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)));
		Assert.True(watchdog.TimedOut);
	}

	[Fact]
	public void ReadLine_DropsOnlyOneTrailingNewline()
	{
		Assert.Equal("/srv/app", RemoteOutput.ReadLine("/srv/app\n"u8));
		Assert.Equal("/srv/app\n", RemoteOutput.ReadLine("/srv/app\n\n"u8));
		Assert.Equal("", RemoteOutput.ReadLine(ReadOnlySpan<byte>.Empty));
	}
}
