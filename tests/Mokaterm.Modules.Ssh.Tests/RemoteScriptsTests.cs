using System.Text;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Elevation;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>Runs the remote scripts in a local POSIX shell when one exists, so syntax and output format are checked for real.</summary>
public sealed class RemoteScriptsTests
{
	public static TheoryData<string, string> Scripts => new()
	{
		{ nameof(RemoteScripts.List), RemoteScripts.List },
		{ nameof(RemoteScripts.Resolve), RemoteScripts.Resolve },
		{ nameof(RemoteScripts.LinkTargets), RemoteScripts.LinkTargets },
		{ nameof(RemoteScripts.Accounts), RemoteScripts.Accounts },
		{ nameof(RemoteScripts.CreateDirectory), RemoteScripts.CreateDirectory },
		{ nameof(RemoteScripts.Delete), RemoteScripts.Delete },
		{ nameof(RemoteScripts.Rename), RemoteScripts.Rename },
		{ nameof(RemoteScripts.ChangeMode), RemoteScripts.ChangeMode },
		{ nameof(RemoteScripts.ChangeOwner), RemoteScripts.ChangeOwner },
		{ nameof(RemoteScripts.ChangeGroup), RemoteScripts.ChangeGroup },
		{ nameof(RemoteScripts.Read), RemoteScripts.Read },
		{ nameof(RemoteScripts.PlaceUpload), RemoteScripts.PlaceUpload },
	};

	[Theory]
	[MemberData(nameof(Scripts))]
	public void Scripts_UseUnixLineEndingsAndStartWithTheMarker(string name, string script)
	{
		Assert.False(script.Contains('\r', StringComparison.Ordinal), name);
		Assert.Contains("printf 'MOKATERM-OUT\\0'", script, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(Scripts))]
	public async Task Scripts_ParseInAPosixShell(string name, string script)
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		// "sh -n" reads the script without running it.
		(int exitCode, _, string error) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + "sh -n <<'MOKATERM_SCRIPT'\n" + script + "\nMOKATERM_SCRIPT\n", []);

		Assert.True(exitCode == 0, $"{name}: {error}");
	}

	[Fact]
	public async Task List_OutputParsesBackIntoEntries()
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		const string Setup = """
			d=$(mktemp -d)
			mkdir "$d/sub dir"
			printf 'hello' > "$d/file with space.txt"
			printf 'x' > "$d/.hidden"
			printf '%s' "$d"
			""";

		(int setupExit, byte[] created, string setupError) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + Setup, []);
		Assert.True(setupExit == 0, setupError);
		string directory = Encoding.UTF8.GetString(created);
		try
		{
			(int exitCode, byte[] output, string error) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + RemoteScripts.List, [directory, "list"]);
			Assert.True(exitCode == 0, error);
			Assert.True(RemoteOutput.TryGetPayload(output, out ReadOnlyMemory<byte> payload));

			List<RemoteFileEntry> entries = [.. RemoteListingParser.Parse(payload.Span, directory, isStat: false).OrderBy(entry => entry.Name, StringComparer.Ordinal)];

			Assert.Equal([".hidden", "file with space.txt", "sub dir"], entries.Select(entry => entry.Name));
			Assert.Equal(RemoteEntryKind.File, entries[1].Kind);
			Assert.Equal(5, entries[1].Size);
			Assert.Equal(RemotePath.Combine(directory, "file with space.txt"), entries[1].Path);
			Assert.Equal(RemoteEntryKind.Directory, entries[2].Kind);
			Assert.NotNull(entries[1].LastModified);
			Assert.NotNull(entries[1].Permissions);

			(int statExit, byte[] statOutput, string statError) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + RemoteScripts.List, [directory + "/sub dir", "stat"]);
			Assert.True(statExit == 0, statError);
			Assert.True(RemoteOutput.TryGetPayload(statOutput, out ReadOnlyMemory<byte> statPayload));
			RemoteFileEntry stat = Assert.Single(RemoteListingParser.Parse(statPayload.Span, directory + "/sub dir", isStat: true));
			Assert.Equal("sub dir", stat.Name);
			Assert.Equal(RemoteEntryKind.Directory, stat.Kind);

			(int missingExit, _, _) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + RemoteScripts.List, [directory + "/nope", "stat"]);
			Assert.Equal(RemoteScripts.ExitNotFound, missingExit);
		}
		finally
		{
			await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + "rm -rf -- \"$1\"", [directory]);
		}
	}

	[Fact]
	public async Task Rename_RefusesToReplaceUnlessAsked()
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		(_, byte[] created, _) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + "d=$(mktemp -d); printf a > \"$d/a\"; printf b > \"$d/b\"; printf '%s' \"$d\"", []);
		string directory = Encoding.UTF8.GetString(created);
		try
		{
			string a = directory + "/a";
			string b = directory + "/b";
			(int refused, _, _) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + RemoteScripts.Rename, [a, b, ""]);
			(int replaced, _, string error) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + RemoteScripts.Rename, [a, b, "1"]);
			(_, byte[] content, _) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + "cat -- \"$1\"", [b]);

			Assert.Equal(RemoteScripts.ExitAlreadyExists, refused);
			Assert.True(replaced == 0, error);
			Assert.Equal("a", Encoding.UTF8.GetString(content));
		}
		finally
		{
			await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + "rm -rf -- \"$1\"", [directory]);
		}
	}

	[Fact]
	public async Task ChangeMode_HandsChmodTheEntriesItsTargetsPick()
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		// A chmod earlier in PATH logs its arguments, so the test sees what the script chose without changing any modes.
		const string Setup = """
			d=$(mktemp -d)
			mkdir -p "$d/tree/sub" "$d/bin"
			printf x > "$d/tree/a.txt"
			printf x > "$d/tree/sub/b.txt"
			cat > "$d/bin/chmod" <<'FAKE'
			#!/bin/sh
			printf '%s\n' "$*" >> "$CHMOD_LOG"
			FAKE
			chmod +x "$d/bin/chmod"
			printf '%s' "$d"
			""";

		(int setupExit, byte[] created, string setupError) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + Setup, []);
		Assert.True(setupExit == 0, setupError);
		string directory = Encoding.UTF8.GetString(created);
		string tree = directory + "/tree";
		try
		{
			Assert.Equal(["--", "00755", tree, tree + "/sub"], await RunAsync(tree, "00755", "1", "d"));
			// find lists a folder in whatever order the file system keeps it.
			Assert.Equal(
				["--", tree + "/a.txt", tree + "/sub/b.txt", "u+rwX,g+rX,o+rX"],
				(await RunAsync(tree, "u+rwX,g+rX,o+rX", "1", "f")).Order(StringComparer.Ordinal));
			Assert.Equal(["-R", "--", "00700", tree], await RunAsync(tree, "00700", "1", ""));
			Assert.Empty(await RunAsync(tree + "/a.txt", "00600", "", "d"));
			Assert.Equal(["--", "00600", tree + "/a.txt"], await RunAsync(tree + "/a.txt", "00600", "", "f"));

			(int missingExit, _, _) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + RemoteScripts.ChangeMode, [tree + "/nope", "00600", "", ""]);
			Assert.Equal(RemoteScripts.ExitNotFound, missingExit);
		}
		finally
		{
			await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + "rm -rf -- \"$1\"", [directory]);
		}

		async Task<string[]> RunAsync(params string[] arguments)
		{
			string environment = $"PATH='{directory}/bin':/usr/bin:/bin:$PATH\nexport PATH\nCHMOD_LOG='{directory}/log'\nexport CHMOD_LOG\nrm -f -- \"$CHMOD_LOG\"\n";
			(int exitCode, _, string error) = await PosixShellLocator.RunAsync(shell, environment + RemoteScripts.ChangeMode, arguments);
			Assert.True(exitCode == 0, error);

			(_, byte[] log, _) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + "cat -- \"$1\" 2>/dev/null", [directory + "/log"]);
			return Encoding.UTF8.GetString(log).Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries);
		}
	}

	[Fact]
	public void SudoCommand_KeepsThePasswordOffTheCommandLineAndQuotesArguments()
	{
		string withPassword = SudoRunner.BuildCommand("sudo", withPassword: true, "exec cat -- \"$1\"", ["/etc/it's secret"]);
		string passwordless = SudoRunner.BuildCommand("/usr/bin/sudo", withPassword: false, "true", []);

		Assert.StartsWith("LC_ALL=C 'sudo' -S -p '' -- sh -c '", withPassword, StringComparison.Ordinal);
		Assert.EndsWith(@" sh '/etc/it'\''s secret'", withPassword, StringComparison.Ordinal);
		Assert.Contains(RemoteScripts.ElevatedMarker, withPassword, StringComparison.Ordinal);
		Assert.StartsWith("LC_ALL=C '/usr/bin/sudo' -n -- sh -c '", passwordless, StringComparison.Ordinal);
	}
}
