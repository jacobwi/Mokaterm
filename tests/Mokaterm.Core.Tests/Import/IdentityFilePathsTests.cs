using Mokaterm.Abstractions.Import;
using Mokaterm.Core.Import;

namespace Mokaterm.Core.Tests.Import;

/// <summary>How a key path an imported login names becomes a file on this machine, without touching the disk.</summary>
public sealed class IdentityFilePathsTests
{
	private static readonly string Home = Path.Combine(Path.GetTempPath(), "mokaterm-home");

	private static readonly IdentityPathContext Context = new()
	{
		HomeDirectory = Home,
		LocalUser = "me",
		LocalHost = "desk.example.com",
		Variable = name => name switch
		{
			"KEYS" => Path.Combine(Home, "keys"),
			"USERPROFILE" => Home,
			_ => null,
		},
		Alias = "web",
		HostName = "10.0.0.5",
		RemoteUser = "deploy",
		Port = 2222,
	};

	[Theory]
	[InlineData("~/.ssh/id_ed25519", ".ssh/id_ed25519")]
	[InlineData("~", "")]
	[InlineData("~me/.ssh/id_rsa", ".ssh/id_rsa")]
	[InlineData("%d/.ssh/id_rsa", ".ssh/id_rsa")]
	[InlineData("~/.ssh/%r@%h-%p", ".ssh/deploy@10.0.0.5-2222")]
	[InlineData("~/.ssh/%n_%u_%L_%l", ".ssh/web_me_desk_desk.example.com")]
	[InlineData("~/.ssh/100%%", ".ssh/100%")]
	[InlineData("${KEYS}/work", "keys/work")]
	[InlineData("id_work", ".ssh/id_work")]
	[InlineData("keys/id_work", ".ssh/keys/id_work")]
	[InlineData("\"~/My Keys/id\"", "My Keys/id")]
	public void OpenSsh_ExpandsTheWayOpenSshDoes(string raw, string underHome)
	{
		IdentityPathResult result = IdentityFilePaths.Resolve(raw, IdentityPathStyle.OpenSsh, Context);

		Assert.Null(result.Reason);
		Assert.Equal(Path.GetFullPath(Path.Combine(Home, underHome)), result.FullPath);
	}

	[Fact]
	public void OpenSsh_TakesARelativePathFromTheSshFolder_AndSaysItWasRelative()
	{
		Assert.True(IdentityFilePaths.Resolve("id_work", IdentityPathStyle.OpenSsh, Context).WasRelative);
		Assert.False(IdentityFilePaths.Resolve("~/.ssh/id_work", IdentityPathStyle.OpenSsh, Context).WasRelative);
	}

	[Fact]
	public void OpenSsh_WithoutARemoteUser_FillsInTheLocalOne()
	{
		IdentityPathResult result = IdentityFilePaths.Resolve("~/%r", IdentityPathStyle.OpenSsh, Context with { RemoteUser = null, Port = null });

		Assert.Equal(Path.GetFullPath(Path.Combine(Home, "me")), result.FullPath);
		Assert.Equal(
			Path.GetFullPath(Path.Combine(Home, "22")),
			IdentityFilePaths.Resolve("~/%p", IdentityPathStyle.OpenSsh, Context with { Port = null }).FullPath);
	}

	[Theory]
	[InlineData("~root/.ssh/id_rsa", "~root")]
	[InlineData("~/.ssh/%C", "%C")]
	[InlineData("~/.ssh/%i", "%i")]
	[InlineData("~/.ssh/id%", "lone %")]
	[InlineData("${NOPE}/id", "NOPE")]
	[InlineData("${KEYS/id", "closing brace")]
	[InlineData("  ", "empty")]
	public void OpenSsh_SaysWhyAPathCannotBeWorkedOut(string raw, string mentioned)
	{
		IdentityPathResult result = IdentityFilePaths.Resolve(raw, IdentityPathStyle.OpenSsh, Context);

		Assert.Null(result.FullPath);
		Assert.Contains(mentioned, result.Reason, StringComparison.Ordinal);
	}

	[Fact]
	public void Plain_KeepsAFullPath_AndExpandsWindowsVariables()
	{
		string full = Path.Combine(Home, "keys", "deploy.ppk");

		Assert.Equal(full, IdentityFilePaths.Resolve(full, IdentityPathStyle.Plain, Context).FullPath);
		Assert.Equal(full, IdentityFilePaths.Resolve("\"" + full + "\"", IdentityPathStyle.Plain, Context).FullPath);
		Assert.Equal(full, IdentityFilePaths.Resolve("%KEYS%" + Path.DirectorySeparatorChar + "deploy.ppk", IdentityPathStyle.Plain, Context).FullPath);
	}

	[Theory]
	[InlineData("deploy.ppk")]
	[InlineData("keys\\deploy.ppk")]
	[InlineData("%UNSET%\\deploy.ppk")]
	[InlineData("~/.ssh/id_rsa")]
	public void Plain_RefusesWhatIsNotAFullPath(string raw)
	{
		IdentityPathResult result = IdentityFilePaths.Resolve(raw, IdentityPathStyle.Plain, Context);

		Assert.Null(result.FullPath);
		Assert.Contains("not a full path", result.Reason, StringComparison.Ordinal);
	}
}
