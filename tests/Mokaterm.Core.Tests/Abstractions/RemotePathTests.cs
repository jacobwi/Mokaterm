using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Core.Tests.Abstractions;

public sealed class RemotePathTests
{
	[Theory]
	[InlineData("/etc//nginx/./sites/../conf.d", "/etc/nginx/conf.d")]
	[InlineData("", "/")]
	[InlineData("   ", "/")]
	[InlineData("/..", "/")]
	[InlineData("/a/b/", "/a/b")]
	[InlineData("relative/path", "/relative/path")]
	[InlineData("/a/../../b", "/b")]
	public void Normalize_CollapsesSegments(string input, string expected) =>
		Assert.Equal(expected, RemotePath.Normalize(input));

	[Theory]
	[InlineData("/etc", "nginx", "/etc/nginx")]
	[InlineData("/etc/", "nginx", "/etc/nginx")]
	[InlineData("/etc", "/var/log", "/var/log")]
	[InlineData("/etc", "", "/etc")]
	[InlineData("/", "..", "/")]
	[InlineData("/a/b", "../c", "/a/c")]
	public void Combine_JoinsAndNormalizes(string directory, string name, string expected) =>
		Assert.Equal(expected, RemotePath.Combine(directory, name));

	[Theory]
	[InlineData("/etc/nginx", "/etc")]
	[InlineData("/etc", "/")]
	[InlineData("/", "/")]
	[InlineData("/etc/nginx/", "/etc")]
	public void GetParent_ReturnsContainingDirectory(string path, string expected) =>
		Assert.Equal(expected, RemotePath.GetParent(path));

	[Theory]
	[InlineData("/etc/nginx.conf", "nginx.conf")]
	[InlineData("/etc/", "etc")]
	[InlineData("/", "")]
	public void GetName_ReturnsLastSegment(string path, string expected) =>
		Assert.Equal(expected, RemotePath.GetName(path));

	[Fact]
	public void GetSegments_StartsAtRoot()
	{
		IReadOnlyList<RemotePathSegment> segments = RemotePath.GetSegments("/etc//nginx");

		Assert.Collection(
			segments,
			segment => Assert.Equal(new RemotePathSegment("/", "/"), segment),
			segment => Assert.Equal(new RemotePathSegment("etc", "/etc"), segment),
			segment => Assert.Equal(new RemotePathSegment("nginx", "/etc/nginx"), segment));
		Assert.Equal(new RemotePathSegment("/", "/"), Assert.Single(RemotePath.GetSegments("/")));
	}

	[Theory]
	[InlineData("file.txt", true)]
	[InlineData("...", true)]
	[InlineData(" ", true)]
	[InlineData("", false)]
	[InlineData(null, false)]
	[InlineData(".", false)]
	[InlineData("..", false)]
	[InlineData("a/b", false)]
	[InlineData("a\0b", false)]
	public void IsValidName_RejectsPathsAndDotEntries(string? name, bool expected) =>
		Assert.Equal(expected, RemotePath.IsValidName(name));

	[Theory]
	[InlineData("/etc/nginx", "/etc", true)]
	[InlineData("/etc", "/etc/", true)]
	[InlineData("/etcetera", "/etc", false)]
	[InlineData("/anything", "/", true)]
	[InlineData("/etc", "/etc/nginx", false)]
	public void IsSameOrInside_ComparesWholeSegments(string path, string ancestor, bool expected) =>
		Assert.Equal(expected, RemotePath.IsSameOrInside(path, ancestor));

	[Theory]
	[InlineData("/", true)]
	[InlineData("/..", true)]
	[InlineData("/a", false)]
	public void IsRoot_UsesNormalizedPath(string path, bool expected) =>
		Assert.Equal(expected, RemotePath.IsRoot(path));
}
