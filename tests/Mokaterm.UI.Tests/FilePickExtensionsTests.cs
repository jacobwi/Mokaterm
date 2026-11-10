using Mokaterm.Maui.Services;

namespace Mokaterm.UI.Tests;

// The desktop's picker for Moka's own file inputs: Windows filters by extension, so a type it cannot name shows every file.
public sealed class FilePickExtensionsTests
{
	[Fact]
	public void Extensions_PassThrough_WithoutRepeats()
	{
		Assert.Equal([".pdf", ".docx"], FilePickExtensions.For([".pdf", ".docx", ".PDF"]));
	}

	[Fact]
	public void KnownTypes_BecomeTheirExtensions()
	{
		IReadOnlyList<string>? extensions = FilePickExtensions.For(["image/*", ".heic"]);

		Assert.NotNull(extensions);
		Assert.Contains(".png", extensions);
		Assert.Contains(".heic", extensions);
	}

	[Theory]
	[InlineData("application/x-unknown")]
	[InlineData(".")]
	public void AnyTypeItCannotName_ShowsEveryFile(string token)
	{
		Assert.Null(FilePickExtensions.For([".pdf", token]));
	}

	[Fact]
	public void NoTypes_ShowEveryFile()
	{
		Assert.Null(FilePickExtensions.For([]));
	}
}
