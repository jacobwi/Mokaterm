using System.Text;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Detection;
using Mokaterm.Modules.Ssh.Admin.Security;

namespace Mokaterm.Modules.Ssh.Tests.Admin;

/// <summary>
/// The security page: sshd's settings read the way sshd reads them, where a change has to go to win, the checks and
/// the changes they offer (marked as cutting the connection when they turn off how this session logged in), failed
/// logins and fail2ban.
/// </summary>
public sealed class SecurityTests
{
	private const string Main = """
		# The main file, Ubuntu's shape
		Include /etc/ssh/sshd_config.d/*.conf

		Port 22
		PermitRootLogin prohibit-password
		PasswordAuthentication no
		UsePAM yes
		X11Forwarding yes
		Match User backup
			PasswordAuthentication yes
			X11Forwarding no
		""";

	private const string CloudInit = "PasswordAuthentication yes\n";

	[Fact]
	public void Files_FirstValueWins_DropInsReadWhereIncluded_InNameOrder_AndMatchBlocksLeftOut()
	{
		SshdConfig config = Read(
			("/etc/ssh/sshd_config", Main),
			("/etc/ssh/sshd_config.d/50-cloud-init.conf", CloudInit),
			("/etc/ssh/sshd_config.d/10-first.conf", "PermitRootLogin no\nx11forwarding=\"no\"\n"));

		Assert.Equal("yes", config.Get("PasswordAuthentication"));
		Assert.Equal("/etc/ssh/sshd_config.d/50-cloud-init.conf", config.Sources["passwordauthentication"]);
		Assert.Equal("no", config.Get("permitrootlogin"));
		Assert.Equal("no", config.Get("X11Forwarding"));
		Assert.Equal("yes", config.Get("usepam"));
		Assert.Equal(
			["/etc/ssh/sshd_config", "/etc/ssh/sshd_config.d/10-first.conf", "/etc/ssh/sshd_config.d/50-cloud-init.conf"],
			config.Files);
		Assert.True(config.IncludesDropIns);
		Assert.Empty(config.SetBeforeInclude);
		Assert.False(config.Effective);
	}

	[Fact]
	public void Files_AnIncludeInsideAMatchBlock_IsNotRead()
	{
		SshdConfig config = Read(
			("/etc/ssh/sshd_config", "PasswordAuthentication no\nMatch Address 10.0.0.0/8\nInclude sshd_config.d/*.conf\n"),
			("/etc/ssh/sshd_config.d/50-cloud-init.conf", CloudInit));

		Assert.Equal("no", config.Get("passwordauthentication"));
		Assert.False(config.IncludesDropIns);
		Assert.Equal(["/etc/ssh/sshd_config"], config.Files);
	}

	[Fact]
	public void Files_ARelativeInclude_IsUnderEtcSsh()
	{
		SshdConfig config = Read(
			("/etc/ssh/sshd_config", "MaxAuthTries 3\nInclude sshd_config.d/*.conf\n"),
			("/etc/ssh/sshd_config.d/a.conf", "MaxAuthTries 9\nPubkeyAuthentication no\n"));

		Assert.Equal("3", config.Get("maxauthtries"));
		Assert.Equal("no", config.Get("pubkeyauthentication"));
		Assert.Contains("maxauthtries", config.SetBeforeInclude);
	}

	[Theory]
	[InlineData("/etc/ssh/sshd_config.d/*.conf", "/etc/ssh/sshd_config.d/50-cloud-init.conf", true)]
	[InlineData("/etc/ssh/sshd_config.d/*.conf", "/etc/ssh/sshd_config.d/sub/x.conf", false)]
	[InlineData("/etc/ssh/sshd_config.d/*.conf", "/etc/ssh/sshd_config.d/x.conf.bak", false)]
	[InlineData("/etc/ssh/conf?.d/a", "/etc/ssh/conf1.d/a", true)]
	public void IncludeGlobs_StayInsideOneFolder(string pattern, string path, bool matches) =>
		Assert.Equal(matches, SshdConfigParser.Matches(pattern, path));

	[Fact]
	public void SshdT_KeepsEveryValueOfARepeatedKeyword()
	{
		Dictionary<string, string> values = SshdConfigParser.ParseEffective("passwordauthentication no\nallowusers alice\nallowusers bob@10.0.0.*\n");

		Assert.Equal("no", values["passwordauthentication"]);
		Assert.Equal("alice bob@10.0.0.*", values["allowusers"]);
	}

	[Fact]
	public void Route_GoesThroughTheDropIn_WhenItIsReadFirst()
	{
		SshdConfig config = Read(("/etc/ssh/sshd_config", Main), ("/etc/ssh/sshd_config.d/50-cloud-init.conf", CloudInit));

		Assert.Equal(new SshdChangeRoute(true, SshdConfig.DropInFile), config.RouteFor("PasswordAuthentication"));
		Assert.Equal(new SshdChangeRoute(true, SshdConfig.DropInFile), config.RouteFor("MaxAuthTries"));
	}

	[Fact]
	public void Route_GoesToTheEditor_WhenTheMainFileSetsItFirst_OrHasNoInclude_OrAnEarlierDropInSetsIt()
	{
		SshdConfig before = Read(("/etc/ssh/sshd_config", "PasswordAuthentication yes\nInclude /etc/ssh/sshd_config.d/*.conf\n"));
		SshdConfig without = Read(("/etc/ssh/sshd_config", "PasswordAuthentication yes\n"));
		SshdConfig earlier = Read(
			("/etc/ssh/sshd_config", Main),
			("/etc/ssh/sshd_config.d/00-aaa.conf", "PasswordAuthentication yes\n"));

		Assert.Equal(new SshdChangeRoute(false, SshdConfig.MainFile), before.RouteFor("PasswordAuthentication"));
		Assert.Equal(new SshdChangeRoute(false, SshdConfig.MainFile), without.RouteFor("PasswordAuthentication"));
		Assert.Equal(new SshdChangeRoute(false, SshdConfig.MainFile), without.RouteFor("MaxAuthTries"));
		Assert.Equal(new SshdChangeRoute(false, "/etc/ssh/sshd_config.d/00-aaa.conf"), earlier.RouteFor("PasswordAuthentication"));
		Assert.Null(SshdConfig.None.RouteFor("PasswordAuthentication"));
	}

	[Fact]
	public void SetInDropIn_ChecksBeforeReloading_AndPutsTheOldFileBack()
	{
		AdminAction action = SshdCommands.SetInDropIn(Fix("PermitRootLogin", "prohibit-password", "prohibit-password", "without-password"), InitSystem.Systemd);
		string body = action.Script.Body;

		Assert.True(action.Script.AsRoot);
		Assert.Equal("Set PermitRootLogin prohibit-password", action.Title);
		Assert.Contains("file=/etc/ssh/sshd_config.d/00-mokaterm.conf", body, StringComparison.Ordinal);
		Assert.Contains("grep -v -i -E '^[[:space:]]*PermitRootLogin([[:space:]]|=)' \"$file\" > \"$new\" || true", body, StringComparison.Ordinal);
		Assert.Contains("echo 'PermitRootLogin prohibit-password' >> \"$new\"", body, StringComparison.Ordinal);
		Assert.Contains("if ! sshd -t || ! sshd -T | grep -q -i -E '^permitrootlogin (prohibit-password|without-password)$'; then", body, StringComparison.Ordinal);
		Assert.Contains("mv -f \"$file.mokaterm-old\" \"$file\"", body, StringComparison.Ordinal);
		Assert.True(body.IndexOf("sshd -t", StringComparison.Ordinal) < body.IndexOf("try-reload-or-restart", StringComparison.Ordinal));
	}

	[Fact]
	public void SetInDropIn_RefusesAnythingButPlainWords()
	{
		Assert.Throws<ArgumentException>(() => SshdCommands.SetInDropIn(Fix("PasswordAuthentication", "no; reboot", "no"), InitSystem.Systemd));
		Assert.Throws<ArgumentException>(() => SshdCommands.SetInDropIn(Fix("Password Authentication", "no", "no"), InitSystem.Systemd));
		Assert.Throws<ArgumentException>(() => SshdCommands.SetInDropIn(Fix("PasswordAuthentication", "no", "no|yes"), InitSystem.Systemd));
	}

	[Theory]
	[InlineData(nameof(InitSystem.Systemd), "systemctl try-reload-or-restart \"$unit\"")]
	[InlineData(nameof(InitSystem.OpenRc), "rc-service sshd reload")]
	[InlineData(nameof(InitSystem.SysV), "/etc/init.d/ssh reload")]
	[InlineData(nameof(InitSystem.BusyBox), "kill -HUP \"$(cat /var/run/sshd.pid)\"")]
	public void Reload_IsTheInitSystemsOwn(string init, string expected) =>
		Assert.Contains(expected, SshdCommands.Reload(Enum.Parse<InitSystem>(init)), StringComparison.Ordinal);

	[Fact]
	public void CheckAndReload_RunsSshdTFirst_AndCarriesTheFixsWarning()
	{
		SshdFix cutting = Fix("PasswordAuthentication", "no", "no") with { CutsConnection = true, Warning = "This session logged in with a password." };

		AdminAction action = SshdCommands.CheckAndReload(InitSystem.Systemd, SshdConfig.MainFile, cutting);

		Assert.StartsWith("set -e\nsshd -t\n", action.Script.Body, StringComparison.Ordinal);
		Assert.True(action.CutsConnection);
		Assert.StartsWith("This session logged in with a password.", action.Warning, StringComparison.Ordinal);
		Assert.False(SshdCommands.CheckAndReload(InitSystem.Systemd, SshdConfig.MainFile, null).CutsConnection);
	}

	[Fact]
	public void Checks_OfferFixes_OnlyFromSshdsOwnReading()
	{
		SecuritySnapshot files = Snapshot(effective: false);
		SecuritySnapshot root = Snapshot(effective: true);

		Assert.All(SecurityChecks.Build(files, new SessionLogin("alice", AuthenticationMethod.PublicKey, null), 0, null), check => Assert.Null(check.Fix));
		SecurityCheck password = Find(SecurityChecks.Build(root, new SessionLogin("alice", AuthenticationMethod.PublicKey, null), 0, null), "sshd.passwordauthentication");
		Assert.Equal(SecurityLevel.Warning, password.Level);
		Assert.Equal("yes", password.Value);
		Assert.NotNull(password.Fix);
		Assert.Equal("no", password.Fix.Value);
		Assert.False(password.Fix.CutsConnection);
	}

	[Theory]
	[InlineData(nameof(AuthenticationMethod.Password), true)]
	[InlineData(nameof(AuthenticationMethod.KeyboardInteractive), true)]
	[InlineData(nameof(AuthenticationMethod.PublicKey), false)]
	[InlineData(nameof(AuthenticationMethod.Agent), false)]
	[InlineData(null, true)]
	public void TurningPasswordsOff_CutsTheConnection_WhenThisSessionTypedOne(string? method, bool cuts)
	{
		SessionLogin login = new("alice", method is null ? null : Enum.Parse<AuthenticationMethod>(method), null);

		SshdFix fix = Find(SecurityChecks.Build(Snapshot(effective: true), login, 0, null), "sshd.passwordauthentication").Fix!;

		Assert.Equal(cuts, fix.CutsConnection);
		Assert.Equal(cuts, fix.Warning is not null);
		Assert.Equal(cuts, SshdCommands.SetInDropIn(fix, InitSystem.Systemd).CutsConnection);
	}

	[Fact]
	public void RootLogin_YesIsAWarning_WhoseFixKeepsKeys_AndCutsOnlyARootPasswordSession()
	{
		SecuritySnapshot snapshot = Snapshot(effective: true, ("permitrootlogin", "yes"));

		SshdFix forKeyRoot = Find(SecurityChecks.Build(snapshot, new SessionLogin("root", AuthenticationMethod.PublicKey, null), 0, null), "sshd.permitrootlogin").Fix!;
		SshdFix forPasswordRoot = Find(SecurityChecks.Build(snapshot, new SessionLogin("root", AuthenticationMethod.Password, null), 0, null), "sshd.permitrootlogin").Fix!;

		Assert.Equal("prohibit-password", forKeyRoot.Value);
		Assert.Equal(["prohibit-password", "without-password"], forKeyRoot.Accepts);
		Assert.False(forKeyRoot.CutsConnection);
		Assert.True(forPasswordRoot.CutsConnection);
	}

	[Fact]
	public void RootLogin_ByKeyOnly_IsFine_AndTurningItOff_CutsARootSession()
	{
		SecuritySnapshot snapshot = Snapshot(effective: true, ("permitrootlogin", "without-password"));

		SecurityCheck forAlice = Find(SecurityChecks.Build(snapshot, new SessionLogin("alice", AuthenticationMethod.Password, null), 0, null), "sshd.permitrootlogin");
		SecurityCheck forRoot = Find(SecurityChecks.Build(snapshot, new SessionLogin("root", AuthenticationMethod.PublicKey, null), 0, null), "sshd.permitrootlogin");

		Assert.Equal(SecurityLevel.Good, forAlice.Level);
		Assert.Equal("no", forAlice.Fix!.Value);
		Assert.False(forAlice.Fix.CutsConnection);
		Assert.True(forRoot.Fix!.CutsConnection);
		Assert.Contains("logged in as root", forRoot.Fix.Warning, StringComparison.Ordinal);
	}

	[Fact]
	public void KeyboardInteractive_UsesTheKeywordTheServerKnows()
	{
		SecuritySnapshot snapshot = Snapshot(effective: true, ("challengeresponseauthentication", "yes"));
		SecuritySnapshot old = snapshot with { Sshd = snapshot.Sshd with { Values = Without(snapshot.Sshd.Values, "kbdinteractiveauthentication") } };

		SecurityCheck check = Find(SecurityChecks.Build(old, new SessionLogin("alice", AuthenticationMethod.PublicKey, null), 0, null), "sshd.challengeresponseauthentication");

		Assert.Equal("yes", check.Value);
		Assert.Equal("ChallengeResponseAuthentication", check.Fix!.Keyword);
	}

	[Fact]
	public void Checks_TheOthers_SayWhereTheRestIs()
	{
		IReadOnlyList<SecurityCheck> checks = SecurityChecks.Build(Snapshot(effective: true), new SessionLogin("alice", AuthenticationMethod.PublicKey, null), 2, null);

		Assert.Equal(SecurityLevel.Warning, Find(checks, "updates.security").Level);
		Assert.Equal("security-updates", Find(checks, "updates.security").Shows);
		Assert.Equal(SecurityLevel.Good, Find(checks, "fail2ban").Level);
		Assert.Equal(SecurityLevel.Notice, Find(checks, "logins").Level);
		Assert.Equal("2", Find(checks, "ports").Value);
		Assert.Equal(SecurityLevel.Unknown, Find(SecurityChecks.Build(Snapshot(effective: true), new SessionLogin("alice", null, null), null, "apk keeps none."), "updates.security").Level);
	}

	[Fact]
	public void FailedLogins_FromTheJournal_CountAnUnknownUserOnce()
	{
		const string Log = """
			2026-10-11T08:00:01+0000 web1 sshd[1]: Invalid user admin from 198.51.100.23 port 40122
			2026-10-11T08:00:02+0000 web1 sshd[1]: Failed password for invalid user admin from 198.51.100.23 port 40122 ssh2
			2026-10-11T08:00:05+0000 web1 sshd[2]: Failed publickey for deploy from 192.0.2.40 port 60122 ssh2: ED25519 SHA256:abc
			2026-10-11T08:00:09+0000 web1 sshd-session[3]: Invalid user oracle from 2001:db8::7 port 51544
			""";

		IReadOnlyList<FailedLogin> logins = FailedLoginParser.ParseLog(Log.ReplaceLineEndings("\n"));

		Assert.Equal(3, logins.Count);
		Assert.Equal(new FailedLogin { Time = "2026-10-11T08:00:02+0000", User = "admin", Address = "198.51.100.23", Port = 40122, Method = "password", InvalidUser = true }, logins[0]);
		Assert.Equal("publickey", logins[1].Method);
		Assert.Equal(new FailedLogin { Time = "2026-10-11T08:00:09+0000", User = "oracle", Address = "2001:db8::7", Port = 51544, InvalidUser = true }, logins[2]);
	}

	[Fact]
	public void FailedLogins_FromASyslogFile_KeepItsTime()
	{
		IReadOnlyList<FailedLogin> logins = FailedLoginParser.ParseLog("Oct  1 07:55:01 web1 sshd[1234]: Failed password for root from 203.0.113.5 port 52314 ssh2\n");

		Assert.Equal("Oct  1 07:55:01", Assert.Single(logins).Time);
		Assert.Equal([("203.0.113.5", 1, "Oct  1 07:55:01")], FailedLoginParser.ByAddress(logins));
	}

	[Fact]
	public void FailedLogins_FromLastb_OnlyOverSsh_OldestFirst()
	{
		const string Lastb = """
			root     ssh:notty    203.0.113.5      2026-10-11T07:55:01+00:00 - 2026-10-11T07:55:01+00:00  (00:00)
			admin    tty1                          2026-10-11T07:50:00+00:00 - 2026-10-11T07:50:00+00:00  (00:00)
			oracle   ssh:notty    198.51.100.7     2026-10-11T07:40:01+00:00 - 2026-10-11T07:40:01+00:00  (00:00)

			btmp begins 2026-10-01T00:00:01+00:00
			""";

		IReadOnlyList<FailedLogin> logins = FailedLoginParser.ParseLastb(Lastb.ReplaceLineEndings("\n"));

		Assert.Equal(["oracle", "root"], logins.Select(login => login.User));
		Assert.Equal("198.51.100.7", logins[0].Address);
	}

	[Fact]
	public void Fail2Ban_ReadsTheJails_OrSaysWhyNot()
	{
		Fail2BanStatus status = Fail2BanStatus.Parse(
			"Status\n|- Number of jail:\t1\n`- Jail list:\tsshd\n",
			[("sshd", "Status for the jail: sshd\n|- Filter\n|  |- Currently failed:\t2\n|  |- Total failed:\t1288\n|  `- File list:\t/var/log/auth.log\n`- Actions\n   |- Currently banned:\t1\n   |- Total banned:\t93\n   `- Banned IP list:\t198.51.100.23 203.0.113.9\n")]);

		Fail2BanJail jail = Assert.Single(status.Jails);
		Assert.Equal(2, jail.CurrentlyFailed);
		Assert.Equal(1288, jail.TotalFailed);
		Assert.Equal(1, jail.CurrentlyBanned);
		Assert.Equal(93, jail.TotalBanned);
		Assert.Equal(["198.51.100.23", "203.0.113.9"], jail.Banned);
		Assert.Equal("/var/log/auth.log", jail.Watches);
		Assert.Null(status.Problem);

		Assert.Contains("root", Fail2BanStatus.Parse("Permission denied to socket: /var/run/fail2ban/fail2ban.sock, (you must be root)", []).Problem, StringComparison.Ordinal);
		Assert.NotNull(Fail2BanStatus.Parse("ERROR   Failed to access socket path: /var/run/fail2ban/fail2ban.sock. Is fail2ban running?", []).Problem);
	}

	[Fact]
	public void Read_ParsesAWholeAnswer()
	{
		string output = string.Join('\n',
			"@@mt:uid", "0",
			"@@mt:sshd", "/usr/sbin/sshd",
			"@@mt:sshd-t", "passwordauthentication yes", "permitrootlogin without-password", "kbdinteractiveauthentication no",
			"@@mt:sshd-t-exit", "0",
			"@@mt:sshd-file /etc/ssh/sshd_config", Main.ReplaceLineEndings("\n"),
			"@@mt:sshd-file /etc/ssh/sshd_config.d/50-cloud-init.conf", CloudInit,
			"@@mt:logins-source", "journal",
			"@@mt:logins", "2026-10-11T08:00:02+0000 web1 sshd[1]: Failed password for root from 198.51.100.23 port 40122 ssh2",
			"@@mt:fail2ban", "Status", "`- Jail list:\tsshd",
			"@@mt:jail sshd", "`- Actions", "   |- Currently banned:\t0",
			"@@mt:ss", "Netid State  Recv-Q Send-Q Local Address:Port  Peer Address:PortProcess",
			"tcp   LISTEN 0      4096         0.0.0.0:22         0.0.0.0:*    users:((\"sshd\",pid=801,fd=3))",
			"");

		SecuritySnapshot snapshot = SecurityCommands.Parse(output);

		Assert.True(snapshot.AsRoot);
		Assert.True(snapshot.Sshd.Effective);
		Assert.Equal("without-password", snapshot.Sshd.Get("PermitRootLogin"));
		Assert.Equal("/etc/ssh/sshd_config.d/50-cloud-init.conf", snapshot.Sshd.Sources["passwordauthentication"]);
		Assert.Equal("journal", snapshot.FailedLoginSource);
		Assert.Single(snapshot.FailedLogins);
		Assert.Single(snapshot.Fail2Ban!.Jails);
		Assert.Equal("ss", snapshot.ListeningSource);
		Assert.Equal("sshd", Assert.Single(snapshot.Listening).Process);
	}

	[Fact]
	public void Read_AnSshdTThatRefused_SaysWhy_AndFallsBackToTheFiles()
	{
		SecuritySnapshot snapshot = SecurityCommands.Parse(string.Join('\n',
			"@@mt:sshd", "/usr/sbin/sshd",
			"@@mt:sshd-t", "/etc/ssh/sshd_config line 9: Bad configuration option: Foo",
			"@@mt:sshd-t-exit", "255",
			"@@mt:sshd-file /etc/ssh/sshd_config", "PasswordAuthentication no", ""));

		Assert.False(snapshot.Sshd.Effective);
		Assert.Contains("Bad configuration option", snapshot.Sshd.TestProblem, StringComparison.Ordinal);
		Assert.Equal("no", snapshot.Sshd.Get("passwordauthentication"));
		Assert.Equal("", snapshot.FailedLoginSource);
		Assert.Null(snapshot.Fail2Ban);
	}

	[Fact]
	public void Read_WithoutSshdOrItsFiles_SaysSo()
	{
		SecurityCheck check = Assert.Single(SecurityChecks.Build(SecurityCommands.Parse("@@mt:uid\n1000\n"), new SessionLogin("alice", null, null), null, null), candidate => candidate.Id == "sshd");

		Assert.Equal(SecurityLevel.Unknown, check.Level);
	}

	[Fact]
	public async Task Scripts_ParseInAPosixShell()
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		AdminScript[] scripts =
		[
			SecurityCommands.Read(asRoot: true),
			SshdCommands.SetInDropIn(Fix("PasswordAuthentication", "no", "no"), InitSystem.Systemd).Script,
			SshdCommands.CheckAndReload(InitSystem.SysV, SshdConfig.MainFile, null).Script,
			SshdCommands.CheckAndReload(InitSystem.BusyBox, SshdConfig.MainFile, null).Script,
		];

		foreach (AdminScript script in scripts)
		{
			(int exit, _, string error) = await PosixShellLocator.RunAsync(shell, "sh -n <<'MOKATERM_SCRIPT'\n" + AdminScriptText.Compose(script) + "\nMOKATERM_SCRIPT\n", [])
				.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
			Assert.True(exit == 0, $"{script.Name}: {error}");
		}
	}

	[Fact]
	public async Task TheDropInsGrep_DropsOnlyTheKeywordsOwnLines()
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		string grep = SshdCommands.SetInDropIn(Fix("PasswordAuthentication", "no", "no"), InitSystem.Systemd).Script.Body
			.Split('\n').Single(line => line.TrimStart().StartsWith("grep -v", StringComparison.Ordinal)).Trim();
		string script = string.Join('\n',
			PosixShellLocator.UnixPathPrefix,
			"cd \"$(mktemp -d)\"",
			"printf '# mine\\nPasswordAuthentication yes\\n  passwordauthentication=yes\\nPasswordAuthenticationX yes\\nX11Forwarding no\\n' > file",
			grep.Replace("\"$file\"", "file", StringComparison.Ordinal).Replace("\"$new\"", "new", StringComparison.Ordinal),
			"cat new");
		(int exit, byte[] output, string error) = await PosixShellLocator.RunAsync(shell, script, []).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

		Assert.True(exit == 0, error);
		Assert.Equal("# mine\nPasswordAuthenticationX yes\nX11Forwarding no\n", Encoding.UTF8.GetString(output).ReplaceLineEndings("\n"));
	}

	private static SshdFix Fix(string keyword, string value, params string[] accepts) =>
		new() { Keyword = keyword, Value = value, Accepts = accepts, Label = "Fix" };

	private static SecurityCheck Find(IReadOnlyList<SecurityCheck> checks, string id) => Assert.Single(checks, check => check.Id == id);

	private static SshdConfig Read(params (string Path, string Text)[] files) =>
		SshdConfigParser.ParseFiles(files.ToDictionary(file => file.Path, file => file.Text.ReplaceLineEndings("\n"), StringComparer.Ordinal), []);

	private static Dictionary<string, string> Without(IReadOnlyDictionary<string, string> values, string key)
	{
		Dictionary<string, string> copy = new(values, StringComparer.Ordinal);
		copy.Remove(key);
		return copy;
	}

	/// <summary>An Ubuntu server: files as cloud-init leaves them, and sshd -T when read as root.</summary>
	private static SecuritySnapshot Snapshot(bool effective, params (string Key, string Value)[] overrides)
	{
		SshdConfig config = Read(("/etc/ssh/sshd_config", Main), ("/etc/ssh/sshd_config.d/50-cloud-init.conf", CloudInit));
		if (effective)
		{
			Dictionary<string, string> values = new(StringComparer.Ordinal)
			{
				["passwordauthentication"] = "yes",
				["kbdinteractiveauthentication"] = "no",
				["permitrootlogin"] = "without-password",
				["pubkeyauthentication"] = "yes",
				["x11forwarding"] = "yes",
				["maxauthtries"] = "6",
				["usepam"] = "yes",
			};

			foreach ((string key, string value) in overrides)
			{
				values[key] = value;
			}

			config = config with { Values = values, Effective = true };
		}

		return new SecuritySnapshot
		{
			AsRoot = effective,
			Sshd = config,
			FailedLogins = [new FailedLogin { User = "root", Address = "198.51.100.23", Method = "password" }],
			FailedLoginSource = "journal",
			Fail2Ban = new Fail2BanStatus { Jails = [new Fail2BanJail { Name = "sshd", CurrentlyBanned = 1 }] },
			Listening =
			[
				new Mokaterm.Modules.Ssh.Admin.Network.ListeningSocket { Protocol = "tcp", Address = "0.0.0.0", Port = 22 },
				new Mokaterm.Modules.Ssh.Admin.Network.ListeningSocket { Protocol = "tcp", Address = "::", Port = 22 },
				new Mokaterm.Modules.Ssh.Admin.Network.ListeningSocket { Protocol = "tcp", Address = "0.0.0.0", Port = 80 },
				new Mokaterm.Modules.Ssh.Admin.Network.ListeningSocket { Protocol = "tcp", Address = "127.0.0.1", Port = 5432 },
			],
			ListeningSource = "ss",
		};
	}
}
