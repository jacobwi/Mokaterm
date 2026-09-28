using System.Text;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Telnet.Protocol;

namespace Mokaterm.Modules.Telnet.Tests;

public sealed class TelnetAutoLoginTests
{
	[Fact]
	public void Feed_LoginPrompt_TypesTheUserName()
	{
		using TelnetAutoLogin login = new("operator", SecretBuffer.FromString("s3cret"));

		Assert.False(login.Feed("\r\nWelcome to the switch\r\n").HasValue);
		TelnetAutoLoginStep step = Typed(login.Feed("login: "));

		Assert.Equal("operator", Encoding.UTF8.GetString(step.Input));
		Assert.True(step.Echo);
		Assert.False(login.IsFinished);
	}

	[Fact]
	public void Feed_PasswordPrompt_TypesThePasswordWithoutEchoingIt()
	{
		using TelnetAutoLogin login = new("operator", SecretBuffer.FromString("s3cret"));
		login.Feed("Username: ");

		TelnetAutoLoginStep step = Typed(login.Feed("operator\r\nPassword: "));

		Assert.Equal("s3cret", Encoding.UTF8.GetString(step.Input));
		Assert.False(step.Echo);
		Assert.True(login.IsFinished);
	}

	[Theory]
	[InlineData("login: ")]
	[InlineData("Login as: ")]
	[InlineData("Username:")]
	[InlineData("User Name: ")]
	[InlineData("user: ")]
	public void Feed_TheUsualPrompts_AreRecognised(string prompt)
	{
		using TelnetAutoLogin login = new("operator", null);

		Assert.True(login.Feed(prompt).HasValue);
	}

	[Fact]
	public void Feed_ColouredPrompt_StillMatches()
	{
		using TelnetAutoLogin login = new("operator", null);

		TelnetAutoLoginStep step = Typed(login.Feed("[1;32mlogin:[0m "));

		Assert.Equal("operator", Encoding.UTF8.GetString(step.Input));
	}

	[Fact]
	public void Feed_PromptSplitAcrossChunks_StillMatches()
	{
		using TelnetAutoLogin login = new("operator", null);

		Assert.False(login.Feed("log").HasValue);
		Assert.True(login.Feed("in: ").HasValue);
	}

	[Fact]
	public void Feed_TextThatIsNotAPrompt_TypesNothing()
	{
		using TelnetAutoLogin login = new("operator", SecretBuffer.FromString("s3cret"));

		Assert.False(login.Feed("Last login: Mon Sep 14 09:12:44\r\nmotd\r\n").HasValue);
		Assert.False(login.IsFinished);
	}

	[Fact]
	public void Feed_WithoutAPassword_IsFinishedAfterTheUserName()
	{
		using TelnetAutoLogin login = new("operator", null);

		Assert.True(login.Feed("login: ").HasValue);

		Assert.True(login.IsFinished);
		Assert.False(login.HasPassword);
	}

	[Fact]
	public void Feed_WithoutAUserName_WaitsForThePasswordPrompt()
	{
		using TelnetAutoLogin login = new(null, SecretBuffer.FromString("s3cret"));

		Assert.False(login.Feed("login: ").HasValue);
		TelnetAutoLoginStep step = Typed(login.Feed("\r\nPassword: "));

		Assert.Equal("s3cret", Encoding.UTF8.GetString(step.Input));
	}

	[Fact]
	public void Feed_WithNothingToType_IsFinishedFromTheStart()
	{
		using TelnetAutoLogin login = new(null, null);

		Assert.True(login.IsFinished);
		Assert.False(login.Feed("login: ").HasValue);
	}

	[Fact]
	public void Dispose_WipesThePasswordItWasGiven()
	{
		SecretBuffer password = SecretBuffer.FromString("s3cret");
		TelnetAutoLogin login = new("operator", password);

		login.Dispose();

		Assert.True(password.IsDisposed);
		Assert.True(login.IsFinished);
	}

	[Fact]
	public void Feed_LoginPromptAgainBeforeThePassword_TypesNothingMore()
	{
		SecretBuffer password = SecretBuffer.FromString("s3cret");
		using TelnetAutoLogin login = new("operator", password);
		Typed(login.Feed("login: "));

		// The name was refused. Whatever the user types next is their own attempt, not this login's.
		Assert.False(login.Feed("\r\nLogin incorrect\r\n\r\nlogin: ").HasValue);
		Assert.True(login.IsFinished);
		Assert.False(login.Feed("Password: ").HasValue);
		Assert.True(password.IsDisposed);
	}

	[Fact]
	public void LineSubmitted_AfterTheUserName_StopsBeforeThePassword()
	{
		SecretBuffer password = SecretBuffer.FromString("s3cret");
		using TelnetAutoLogin login = new("operator", password);
		Typed(login.Feed("login: "));

		// The account needed no password and the user went on to run su: its prompt is not this login's.
		login.LineSubmitted();

		Assert.True(login.IsFinished);
		Assert.False(login.Feed("$ su -\r\nPassword: ").HasValue);
		Assert.True(password.IsDisposed);
	}

	[Fact]
	public void LineSubmitted_BeforeTheUserName_KeepsWaitingForThePrompt()
	{
		using TelnetAutoLogin login = new("operator", SecretBuffer.FromString("s3cret"));

		// Enter is how a console server is asked for its prompt.
		login.LineSubmitted();

		Assert.False(login.IsFinished);
		Assert.Equal("operator", Encoding.UTF8.GetString(Typed(login.Feed("Press RETURN to get started\r\nUsername: ")).Input));
		Assert.Equal("s3cret", Encoding.UTF8.GetString(Typed(login.Feed("\r\nPassword: ")).Input));
	}

	[Fact]
	public void LineSubmitted_WithoutAUserName_KeepsWaitingForThePasswordPrompt()
	{
		using TelnetAutoLogin login = new(null, SecretBuffer.FromString("s3cret"));
		Assert.False(login.Feed("login: ").HasValue);

		// The user answers the login prompt; the saved password is for the prompt that follows.
		login.LineSubmitted();

		Assert.Equal("s3cret", Encoding.UTF8.GetString(Typed(login.Feed("\r\nPassword: ")).Input));
	}

	[Fact]
	public void Feed_AfterThePasswordWasTyped_WipesTheBuffer()
	{
		SecretBuffer password = SecretBuffer.FromString("s3cret");
		using TelnetAutoLogin login = new(null, password);

		login.Feed("Password: ");

		Assert.True(password.IsDisposed);
	}

	private static TelnetAutoLoginStep Typed(TelnetAutoLoginStep? step)
	{
		Assert.True(step.HasValue, "The automatic login typed nothing.");
		return step.Value;
	}
}
