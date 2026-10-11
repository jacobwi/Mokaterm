using Mokaterm.Modules.Local.Pty.Windows;

namespace Mokaterm.Modules.Local.Tests;

public sealed class WindowsEnvironmentBlockTests
{
	[Fact]
	public void Build_SortsByNameWithoutCase_AndEndsWithTwoNuls()
	{
		Dictionary<string, string> variables = new(StringComparer.Ordinal)
		{
			["path"] = "C:\\bin",
			["Alpha"] = "1",
			["BETA"] = "two words",
		};

		string block = new(WindowsEnvironmentBlock.Build(variables));

		Assert.Equal("Alpha=1\0BETA=two words\0path=C:\\bin\0\0", block);
	}

	[Fact]
	public void Build_Empty_IsStillAValidBlock()
	{
		Assert.Equal("\0\0", new string(WindowsEnvironmentBlock.Build(new Dictionary<string, string>())));
	}

	[Fact]
	public void Build_LeavesOutWhatABlockCannotHold()
	{
		Dictionary<string, string> variables = new(StringComparer.Ordinal)
		{
			["A=B"] = "x",
			["NUL"] = "a\0b",
			[""] = "empty name",
			["KEPT"] = "",
		};

		Assert.Equal("KEPT=\0\0", new string(WindowsEnvironmentBlock.Build(variables)));
	}
}
