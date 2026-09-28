using Mokaterm.Abstractions.Commands;

namespace Mokaterm.Core.Tests.Commands;

public sealed class CommandSnippetNameTests
{
	[Theory]
	[InlineData("ls -la", "ls -la")]
	[InlineData("  docker   compose   up -d  ", "docker compose up -d")]
	[InlineData("echo one\necho two", "echo one")]
	[InlineData("", "")]
	[InlineData("   ", "")]
	public void Derive_ShortCommands_AreKeptWhole(string command, string expected) =>
		Assert.Equal(expected, CommandSnippetName.Derive(command));

	[Fact]
	public void Derive_LongCommand_CutsOnAWordBoundary()
	{
		string name = CommandSnippetName.Derive("find /var/log -name '*.log' -mtime +7 -print -delete");

		Assert.Equal("find /var/log -name '*.log' -mtime +7 -print...", name);
		Assert.True(name.Length <= CommandSnippetName.MaxLength);
	}

	[Fact]
	public void Derive_OneLongWord_IsCutAnyway()
	{
		string name = CommandSnippetName.Derive(new string('x', 200));

		Assert.Equal(new string('x', CommandSnippetName.MaxLength - 3) + "...", name);
	}
}
