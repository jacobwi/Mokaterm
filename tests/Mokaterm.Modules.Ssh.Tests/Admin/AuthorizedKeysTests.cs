using System.Text;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Accounts;

namespace Mokaterm.Modules.Ssh.Tests.Admin;

/// <summary>
/// Reading a user's authorized_keys as sshd does, and the scripts that add and remove keys: keys matched by key alone,
/// options kept, no duplicates, nothing written through a link, and removing the key this session uses said in red.
/// </summary>
public sealed class AuthorizedKeysTests
{
	private const string Laptop = "AAAAC3NzaC1lZDI1NTE5AAAAIF7sDcQZqoM3v3JfAm/anHjByxxkLur/nW4REvN3g+lC";
	private const string LaptopFingerprint = "SHA256:3qbS2LJAPpDuuwCmPaN1jzp1GNPY8sgCeWlIcg0fKDs";
	private const string Ci = "AAAAC3NzaC1lZDI1NTE5AAAAIAX+ddjQ0/zM7n5OhO9mn3zqmoqGxcAfb4Kq5zkp9003";
	private const string CiFingerprint = "SHA256:O7TeYdElbA/FvJ5uqzSLHdBIXeLihXu1ibsxY6cRPgQ";
	private const string Backup = "AAAAC3NzaC1lZDI1NTE5AAAAIFTQDYZ3WM74FrxGhfWOMnuUlxKwfr0Xw0hfP/yen1Ez";

	private static readonly string File = string.Join('\n',
		"# keys for alice",
		$"ssh-ed25519 {Laptop} alice@laptop",
		"",
		$"from=\"10.0.0.0/8\",no-pty ssh-ed25519 {Ci} deploy ci",
		$"command=\"echo \\\"hi there\\\"\",restrict ssh-ed25519 {Backup}",
		"ssh-ed25519 not-base64 broken",
		$"ssh-rsa {Laptop} type does not match the key",
		"");

	[Fact]
	public void Parse_ReadsKeys_WithTheirOptionsAndComments_AndSkipsTheRest()
	{
		IReadOnlyList<AuthorizedKey> keys = AuthorizedKeysParser.Parse(File);

		Assert.Equal(3, keys.Count);
		Assert.Equal(new AuthorizedKey { LineNumber = 2, Algorithm = "ssh-ed25519", Blob = Laptop, Comment = "alice@laptop", Fingerprint = LaptopFingerprint }, keys[0]);
		Assert.Equal("from=\"10.0.0.0/8\",no-pty", keys[1].Options);
		Assert.Equal("deploy ci", keys[1].Comment);
		Assert.Equal(CiFingerprint, keys[1].Fingerprint);
		Assert.Equal("command=\"echo \\\"hi there\\\"\",restrict", keys[2].Options);
		Assert.Equal("", keys[2].Comment);
		Assert.Equal(5, keys[2].LineNumber);
	}

	[Fact]
	public void Parse_ACertificateLine_IsAKeyMarkedAsOne()
	{
		byte[] blob = [0, 0, 0, 32, .. Encoding.ASCII.GetBytes("ssh-ed25519-cert-v01@openssh.com"), 1, 2, 3];
		AuthorizedKey? key = AuthorizedKeysParser.ParseLine($"ssh-ed25519-cert-v01@openssh.com {Convert.ToBase64String(blob)} ca-signed", 1);

		Assert.NotNull(key);
		Assert.True(key.IsCertificate);
	}

	[Fact]
	public void ReadOutput_SaysWhatItFound()
	{
		string output = string.Join('\n',
			"@@mt:home", "/home/alice",
			"@@mt:state", "present",
			"@@mt:folder-mode", "700 alice",
			"@@mt:file-mode", "664 alice",
			"@@mt:size", "300000",
			"@@mt:keys", $"ssh-ed25519 {Laptop} alice@laptop", "");

		AuthorizedKeysFile file = AuthorizedKeyCommands.Parse(output, "alice");

		Assert.Equal(AuthorizedKeysState.Present, file.State);
		Assert.Equal("/home/alice/.ssh/authorized_keys", file.Path);
		Assert.Single(file.Keys);
		Assert.True(file.IsCut);
		Assert.False(file.IsLink);
		Assert.Contains("written by others", file.ModeProblem, StringComparison.Ordinal);
		Assert.NotNull(file.Find(LaptopFingerprint));
		Assert.Null(file.Find(CiFingerprint));
	}

	[Theory]
	[InlineData("no-file", nameof(AuthorizedKeysState.NoFile))]
	[InlineData("no-folder", nameof(AuthorizedKeysState.NoFolder))]
	[InlineData("no-home", nameof(AuthorizedKeysState.NoHome))]
	[InlineData("unreadable", nameof(AuthorizedKeysState.Unreadable))]
	[InlineData("", nameof(AuthorizedKeysState.Unreadable))]
	public void ReadOutput_States(string printed, string expected)
	{
		AuthorizedKeysFile file = AuthorizedKeyCommands.Parse($"@@mt:home\n/home/bob\n@@mt:state\n{printed}\n@@mt:link\n@@mt:folder-mode\n700 root\n", "bob");

		Assert.Equal(Enum.Parse<AuthorizedKeysState>(expected), file.State);
		Assert.True(file.IsLink);
		Assert.Empty(file.Keys);
		Assert.Null(file.ModeProblem);
	}

	[Fact]
	public void ReadOutput_AFolderSomeoneElseOwns_IsAStrictModesProblem() =>
		Assert.Contains("belongs to mallory", AuthorizedKeyCommands.Parse("@@mt:state\npresent\n@@mt:folder-mode\n700 mallory\n", "alice").ModeProblem, StringComparison.Ordinal);

	[Fact]
	public void Read_OthersNeedRoot_AndTheNameIsChecked()
	{
		AdminScript own = AuthorizedKeyCommands.Read("alice", asRoot: false);

		Assert.False(own.AsRoot);
		Assert.Contains("user='alice'", own.Body, StringComparison.Ordinal);
		Assert.True(AuthorizedKeyCommands.Read("bob", asRoot: true).AsRoot);
		Assert.Throws<ArgumentException>(() => AuthorizedKeyCommands.Read("-rf", asRoot: true));
		Assert.Throws<ArgumentException>(() => AuthorizedKeyCommands.Read("a b", asRoot: true));
	}

	[Fact]
	public void Add_ToTheLoginsOwnFile_RunsAsTheLogin_AndToAnothersAsRoot()
	{
		SshPublicKey key = new("ssh-ed25519", "", $"ssh-ed25519 {Laptop}", LaptopFingerprint);

		AdminAction own = AuthorizedKeyCommands.Add("alice", "alice", key, "Laptop key");
		AdminAction other = AuthorizedKeyCommands.Add("deploy", "alice", key, "Laptop key");

		Assert.False(own.Script.AsRoot);
		Assert.True(other.Script.AsRoot);
		Assert.False(own.Destructive);
		Assert.Contains($"key='ssh-ed25519 {Laptop} Laptop key'", own.Script.Body, StringComparison.Ordinal);
		Assert.Contains($"blob='{Laptop}'", own.Script.Body, StringComparison.Ordinal);
		Assert.Contains("mkdir -m 700 -- \"$dir\"", own.Script.Body, StringComparison.Ordinal);
		Assert.Contains("chmod 600 -- \"$file\"", own.Script.Body, StringComparison.Ordinal);
		Assert.Contains("chown \"$user:$group\"", own.Script.Body, StringComparison.Ordinal);
		Assert.Contains("if [ -L \"$dir\" ] || [ -L \"$file\" ]", own.Script.Body, StringComparison.Ordinal);
		Assert.Contains("printf '%s\\n' \"$key\" >> \"$file\"", own.Script.Body, StringComparison.Ordinal);
	}

	[Fact]
	public void LineFor_KeepsTheKeysOwnComment_ElseUsesTheName_WithoutControlCharacters()
	{
		SshPublicKey commented = new("ssh-ed25519", "bc@desk", $"ssh-ed25519 {Laptop} bc@desk", LaptopFingerprint);
		SshPublicKey bare = new("ssh-ed25519", "", $"ssh-ed25519 {Laptop}", LaptopFingerprint);

		Assert.Equal($"ssh-ed25519 {Laptop} bc@desk", AuthorizedKeyCommands.LineFor(commented, "Desk"));
		Assert.Equal($"ssh-ed25519 {Laptop} Desk key", AuthorizedKeyCommands.LineFor(bare, "  Desk key "));
		Assert.Equal($"ssh-ed25519 {Laptop} evil", AuthorizedKeyCommands.LineFor(bare, "\u0007evil\r\n"));
		Assert.Equal($"ssh-ed25519 {Laptop}", AuthorizedKeyCommands.LineFor(bare, null));
	}

	[Fact]
	public void Add_RefusesAKeySshdWouldNotRead()
	{
		SshPublicKey broken = new("ssh-ed25519", "", "ssh-ed25519 bm90IGEga2V5", "SHA256:x");

		Assert.Throws<ArgumentException>(() => AuthorizedKeyCommands.Add("alice", "alice", broken, null));
	}

	[Fact]
	public void Remove_TheKeyThisSessionLoggedInWith_CutsTheConnection()
	{
		AuthorizedKey key = AuthorizedKeysParser.Parse(File)[0];

		AdminAction action = AuthorizedKeyCommands.Remove("alice", new SessionLogin("alice", AuthenticationMethod.PublicKey, LaptopFingerprint), key);

		Assert.True(action.CutsConnection);
		Assert.True(action.Destructive);
		Assert.Contains("logged in with this key", action.Warning, StringComparison.Ordinal);
		Assert.False(action.Script.AsRoot);
	}

	[Fact]
	public void Remove_AnotherKey_OrAnotherUsersFile_OrAfterAPasswordLogin_DoesNot()
	{
		AuthorizedKey laptop = AuthorizedKeysParser.Parse(File)[0];
		AuthorizedKey ci = AuthorizedKeysParser.Parse(File)[1];

		Assert.False(AuthorizedKeyCommands.Remove("alice", new SessionLogin("alice", AuthenticationMethod.PublicKey, LaptopFingerprint), ci).CutsConnection);
		Assert.Null(AuthorizedKeyCommands.Remove("alice", new SessionLogin("alice", AuthenticationMethod.Password, null), laptop).Warning);

		AdminAction others = AuthorizedKeyCommands.Remove("deploy", new SessionLogin("alice", AuthenticationMethod.PublicKey, LaptopFingerprint), laptop);
		Assert.False(others.CutsConnection);
		Assert.True(others.Script.AsRoot);
	}

	[Fact]
	public void Remove_WhenTheSessionsKeyIsNotKnown_Warns_WithoutClaimingItCuts()
	{
		AdminAction action = AuthorizedKeyCommands.Remove("alice", new SessionLogin("alice", AuthenticationMethod.Agent, null), AuthorizedKeysParser.Parse(File)[0]);

		Assert.False(action.CutsConnection);
		Assert.Contains("cannot tell which", action.Warning, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheScriptsAwk_FindsAKey_AndRemovesOnlyItsLines_KeepingTheRest()
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		AdminAction add = AuthorizedKeyCommands.Add("alice", "alice", new SshPublicKey("ssh-ed25519", "", $"ssh-ed25519 {Ci}", CiFingerprint), null);
		AdminAction remove = AuthorizedKeyCommands.Remove("alice", new SessionLogin("alice", AuthenticationMethod.Password, null), AuthorizedKeysParser.Parse(File)[1]);
		string holds = add.Script.Body.Split('\n').Single(line => line.StartsWith("if awk", StringComparison.Ordinal))["if ".Length..^"; then".Length];
		string drops = remove.Script.Body.Split('\n').Single(line => line.StartsWith("awk ", StringComparison.Ordinal));

		string script = string.Join('\n',
			PosixShellLocator.UnixPathPrefix,
			"cd \"$(mktemp -d)\"",
			"file=keys",
			$"printf '%s' {PosixShell.Quote(File)} > \"$file\"",
			$"blob={PosixShell.Quote(Ci)}",
			$"if {holds}; then echo held; fi",
			$"kept=kept",
			drops,
			"cat \"$kept\"",
			$"if {holds.Replace("\"$file\"", "\"$kept\"", StringComparison.Ordinal)}; then echo still; else echo gone; fi");
		(int exit, byte[] output, string error) = await PosixShellLocator.RunAsync(shell, script, []).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
		string text = Encoding.UTF8.GetString(output).ReplaceLineEndings("\n");

		Assert.True(exit == 0, error);
		Assert.StartsWith("held\n", text, StringComparison.Ordinal);
		Assert.EndsWith("gone\n", text, StringComparison.Ordinal);
		Assert.DoesNotContain(Ci, text, StringComparison.Ordinal);
		Assert.Contains("# keys for alice", text, StringComparison.Ordinal);
		Assert.Contains($"command=\"echo \\\"hi there\\\"\",restrict ssh-ed25519 {Backup}", text, StringComparison.Ordinal);
		Assert.Contains($"ssh-ed25519 {Laptop} alice@laptop", text, StringComparison.Ordinal);
	}
}
