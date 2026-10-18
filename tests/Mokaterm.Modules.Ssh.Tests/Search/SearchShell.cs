using System.Text;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Shell;
using Mokaterm.Modules.Ssh.Tests.Fakes;

namespace Mokaterm.Modules.Ssh.Tests.Search;

/// <summary>
/// The search script against this machine's own POSIX shell, Git Bash's GNU find on Windows: a test tree in a temp
/// folder, with links made the one way MSYS can without privileges (system-file links, which its find reads as links),
/// a folder made unreadable with chmod or, where NTFS ignores that, a deny entry, and finds of other kinds put first on
/// the PATH. The script runs through <see cref="LocalExecChannel"/>, so its arguments reach it inside a file, as they reach
/// a server inside the exec request. Everything made here is removed on dispose.
/// </summary>
internal sealed class SearchShell(string shell) : IAsyncDisposable
{
	public static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

	private const string SiteScript = """
		d=$(mktemp -d)
		s="$d/site"
		mkdir -p "$s/css" "$s/logs/deep" "$s/.git" "$s/secret" "$s/sub dir" "$s/-dash" "$s/a/b/c/d"
		printf 'hello' > "$s/index.html"
		printf 'a' > "$s/error.log"
		printf 'ab' > "$s/error.log.1"
		printf 'x' > "$s/.env"
		printf 'y' > "$s/.git/config"
		printf 'z' > "$s/css/Access.css"
		printf 'zz' > "$s/css/app.css"
		printf 'q' > "$s/logs/access.log"
		printf 'qq' > "$s/logs/deep/access.log.2"
		printf 't' > "$s/secret/token.txt"
		printf 'n' > "$s/sub dir/it's \"quoted\".log"
		printf 'n' > "$s/-dash/-rf.log"
		printf 'n' > "$s/new
		line.log"
		printf 'c' > "$s/café.txt"
		printf 'l' > "$s/ſetup.ini"
		printf 'deep' > "$s/a/b/c/d/deep.log"
		MSYS=winsymlinks:sys ln -s . "$s/loop"
		MSYS=winsymlinks:sys ln -s index.html "$s/current"
		MSYS=winsymlinks:sys ln -s nowhere "$s/broken"
		printf '%s' "$s"
		""";

	// icacls takes /deny as a switch; MSYS would turn it into a Windows path.
	private const string LockScript = """
		chmod 000 "$1"
		if ls "$1" >/dev/null 2>&1 && command -v icacls >/dev/null 2>&1; then
			MSYS2_ARG_CONV_EXCL='*' icacls "$(cygpath -w "$1")" /deny '*S-1-1-0:(RD)' >/dev/null
		fi
		if ls "$1" >/dev/null 2>&1; then printf open; else printf locked; fi
		""";

	private const string UnlockScript = """
		if command -v icacls >/dev/null 2>&1; then
			MSYS2_ARG_CONV_EXCL='*' icacls "$(cygpath -w "$1")" /remove:d '*S-1-1-0' >/dev/null 2>&1
		fi
		chmod 755 "$1"
		""";

	private readonly List<string> _folders = [];
	private readonly List<string> _locked = [];

	public string Shell => shell;

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	/// <summary>A new tree, returned as the folder <c>site</c> inside a temp folder of its own.</summary>
	public async Task<string> MakeSiteAsync()
	{
		string site = await TextAsync(SiteScript);
		_folders.Add(RemotePath.GetParent(site));
		return site;
	}

	/// <summary>Makes <paramref name="folder"/> unreadable; false when this machine cannot (root reads everything).</summary>
	public async Task<bool> LockAsync(string folder)
	{
		_locked.Add(folder);
		return await TextAsync(LockScript, folder) == "locked";
	}

	/// <summary>A folder holding an executable <c>find</c> whose script is <paramref name="body"/>, to put first on the PATH.</summary>
	public async Task<string> FakeFindAsync(string body)
	{
		// Through a quoted heredoc in the script rather than as an argument: Git for Windows' sh reads quotes in the
		// arguments it was started with as quoting, as no server would.
		string folder = await TextAsync("d=$(mktemp -d)\ncat > \"$d/find\" <<'MOKATERM_FIND'\n#!/bin/sh\n" + body + "\nMOKATERM_FIND\nchmod +x \"$d/find\"\nprintf '%s' \"$d\"");
		_folders.Add(folder);
		return folder;
	}

	/// <summary>Runs <see cref="RemoteScripts.Search"/> as a server would, with <paramref name="firstOnPath"/> searched first.</summary>
	public async Task<RemoteCommandResult> RunAsync(string root, RemoteSearchQuery query, ServerSearchBudget? budget = null, string? firstOnPath = null)
	{
		string prelude = PosixShellLocator.UnixPathPrefix
			+ (firstOnPath is null ? "" : "PATH=" + PosixShell.Quote(firstOnPath) + ":$PATH\nexport PATH\n");
		string command = PosixShell.ScriptCommand(RemoteScripts.Search, ServerSearch.Arguments(root, query, budget));
		IExecChannel channel = await LocalExecChannel.StartAsync(shell, prelude, command, input: null, Ct).WaitAsync(Limit, Ct);
		return await CommandExecution.ReadToEndAsync(channel, RemoteCommand.MaxOutputBytes, Ct).WaitAsync(Limit, Ct);
	}

	/// <summary>Runs the script and reads its output the way a file system does; fails the test when the script failed.</summary>
	public async Task<ServerSearchResult> SearchAsync(string root, RemoteSearchQuery query, ServerSearchBudget? budget = null, string? firstOnPath = null)
	{
		RemoteCommandResult result = await RunAsync(root, query, budget, firstOnPath);
		ReadOnlyMemory<byte> payload = RemoteOutput.Require(result.ExitStatus, result.Output, result.Error, root);
		return ServerSearchOutput.Read(payload.Span, root, query, wholeSeconds: false, budget)
			?? throw new InvalidOperationException("The search printed something it does not read: " + Encoding.UTF8.GetString(payload.Span));
	}

	/// <summary>Runs a script in the shell and returns its stdout, failing the test when it fails.</summary>
	public async Task<string> TextAsync(string script, params string[] arguments)
	{
		(int exitCode, byte[] output, string error) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + script, arguments).WaitAsync(Limit, Ct);
		Assert.True(exitCode == 0, error);
		return Encoding.UTF8.GetString(output);
	}

	public async ValueTask DisposeAsync()
	{
		foreach (string folder in _locked)
		{
			await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + UnlockScript, [folder]).WaitAsync(Limit);
		}

		foreach (string folder in _folders)
		{
			await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + "rm -rf -- \"$1\"", [folder]).WaitAsync(Limit);
		}
	}
}
