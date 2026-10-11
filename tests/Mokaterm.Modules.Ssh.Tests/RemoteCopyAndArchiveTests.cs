using System.IO.Compression;
using System.Text;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>
/// The copy and archive scripts: their arguments, and, in a local POSIX shell when one exists, what they do to real files
/// with the same cp, tar and unzip a server runs.
/// </summary>
public sealed class RemoteCopyAndArchiveTests
{
	[Fact]
	public void CopyArguments_AreSourceDestinationAndTheReplaceFlag()
	{
		Assert.Equal(["/srv/a", "/srv/b", ""], RemoteScriptArguments.Copy("/srv/a", "/srv/b", overwrite: false));
		Assert.Equal(["/srv/a", "/srv/b", "1"], RemoteScriptArguments.Copy("/srv/a", "/srv/b", overwrite: true));
	}

	// "./-rf" is a name to tar, where "-rf" would be options.
	[Fact]
	public void CompressArguments_PutEveryEntryBehindDotSlash() =>
		Assert.Equal(
			["/srv", "/srv/site.tar.gz", "./site", "./-rf", "./a b"],
			RemoteScriptArguments.Compress("/srv", ["site", "-rf", "a b"], "/srv/site.tar.gz"));

	[Theory]
	[InlineData("a/b")]
	[InlineData("..")]
	[InlineData("")]
	public void CompressArguments_RefuseWhatIsNotOneEntry(string name) =>
		Assert.Throws<RemoteFileSystemException>(() => RemoteScriptArguments.Compress("/srv", [name], "/srv/x.tar.gz"));

	[Fact]
	public void CompressArguments_RefuseNothing() =>
		Assert.Throws<RemoteFileSystemException>(() => RemoteScriptArguments.Compress("/srv", [], "/srv/x.tar.gz"));

	[Theory]
	[InlineData("/srv/site.tar", "tar")]
	[InlineData("/srv/site.tar.gz", "gz")]
	[InlineData("/srv/site.tgz", "gz")]
	[InlineData("/srv/site.tar.bz2", "bz2")]
	[InlineData("/srv/site.tar.xz", "xz")]
	[InlineData("/srv/site.ZIP", "zip")]
	public void ExtractArguments_NameTheKindTheScriptUnpacks(string archive, string kind) =>
		Assert.Equal([archive, "/srv/site", kind], RemoteScriptArguments.Extract(archive, "/srv/site"));

	[Fact]
	public void ExtractArguments_OfSomethingElse_AreRefused()
	{
		RemoteFileSystemException refused = Assert.Throws<RemoteFileSystemException>(() => RemoteScriptArguments.Extract("/srv/notes.txt", "/srv/notes"));

		Assert.Equal(RemoteFileErrorKind.NotSupported, refused.Kind);
	}

	[Fact]
	public void MissingTool_IsNamedInTheMessage()
	{
		RemoteFileSystemException failure = RemoteCommandErrors.ToException(
			RemoteScripts.ExitUnsupported,
			"\nThe server has no unzip command, which this archive needs.\n",
			"/srv/site.zip");

		Assert.Equal(RemoteFileErrorKind.NotSupported, failure.Kind);
		Assert.Equal("The server has no unzip command, which this archive needs.", failure.Message);
		Assert.Equal("The server has no tool for this.", RemoteCommandErrors.ToException(RemoteScripts.ExitUnsupported, "", "/x").Message);
	}

	[Fact]
	public async Task Copy_RefusesToReplaceUnlessAskedAndMergesFolders()
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		string directory = await MakeFolderAsync(shell, """
			mkdir "$d/site" "$d/old"
			printf 'new' > "$d/site/index.html"
			printf 'kept' > "$d/old/other.html"
			printf 'a' > "$d/a.txt"
			printf 'b' > "$d/b.txt"
			""");
		try
		{
			(int refused, _, _) = await RunAsync(shell, RemoteScripts.Copy, RemoteScriptArguments.Copy(directory + "/a.txt", directory + "/b.txt", overwrite: false));
			(int replaced, _, string replaceError) = await RunAsync(shell, RemoteScripts.Copy, RemoteScriptArguments.Copy(directory + "/a.txt", directory + "/b.txt", overwrite: true));
			(int copied, _, string copyError) = await RunAsync(shell, RemoteScripts.Copy, RemoteScriptArguments.Copy(directory + "/site", directory + "/site copy", overwrite: false));
			(int merged, _, string mergeError) = await RunAsync(shell, RemoteScripts.Copy, RemoteScriptArguments.Copy(directory + "/site", directory + "/old", overwrite: true));
			(int wrongKind, _, string wrongKindError) = await RunAsync(shell, RemoteScripts.Copy, RemoteScriptArguments.Copy(directory + "/site", directory + "/a.txt", overwrite: true));

			Assert.Equal(RemoteScripts.ExitAlreadyExists, refused);
			Assert.True(replaced == 0, replaceError);
			Assert.Equal("a", await ReadAsync(shell, directory + "/b.txt"));
			Assert.True(copied == 0, copyError);
			Assert.Equal("new", await ReadAsync(shell, directory + "/site copy/index.html"));
			Assert.True(merged == 0, mergeError);
			Assert.Equal("new", await ReadAsync(shell, directory + "/old/index.html"));
			Assert.Equal("kept", await ReadAsync(shell, directory + "/old/other.html"));
			Assert.Equal(RemoteScripts.ExitWrongKind, wrongKind);
			Assert.Contains("A folder cannot replace a file.", wrongKindError, StringComparison.Ordinal);
		}
		finally
		{
			await RemoveAsync(shell, directory);
		}
	}

	[Fact]
	public async Task Compress_ThenExtract_RoundTripsAndNeverReplaces()
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		string directory = await MakeFolderAsync(shell, """
			mkdir -p "$d/site/img"
			printf 'hello' > "$d/site/index.html"
			printf 'png' > "$d/site/img/a.png"
			printf 'dash' > "$d/-rf"
			""");
		try
		{
			string archive = directory + "/site.tar.gz";
			(int compressed, _, string compressError) = await RunAsync(shell, RemoteScripts.Compress, RemoteScriptArguments.Compress(directory, ["site", "-rf"], archive));
			(int again, _, _) = await RunAsync(shell, RemoteScripts.Compress, RemoteScriptArguments.Compress(directory, ["site"], archive));
			(int extracted, _, string extractError) = await RunAsync(shell, RemoteScripts.Extract, RemoteScriptArguments.Extract(archive, directory + "/out"));
			(int extractedAgain, _, _) = await RunAsync(shell, RemoteScripts.Extract, RemoteScriptArguments.Extract(archive, directory + "/out"));

			Assert.True(compressed == 0, compressError);
			Assert.Equal(RemoteScripts.ExitAlreadyExists, again);
			Assert.True(extracted == 0, extractError);
			Assert.Equal("hello", await ReadAsync(shell, directory + "/out/site/index.html"));
			Assert.Equal("png", await ReadAsync(shell, directory + "/out/site/img/a.png"));
			Assert.Equal("dash", await ReadAsync(shell, directory + "/out/-rf"));
			Assert.Equal(RemoteScripts.ExitAlreadyExists, extractedAgain);
		}
		finally
		{
			await RemoveAsync(shell, directory);
		}
	}

	[Fact]
	public async Task Extract_UnpacksAZipIntoANewFolder()
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		(int hasUnzip, _, _) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + "command -v unzip", []);
		if (hasUnzip != 0)
		{
			Assert.Skip("This machine's sh has no unzip.");
			return;
		}

		await using TempFolder temp = new();
		string zip = Path.Combine(temp.Path, "site.zip");
		await using (FileStream file = File.Create(zip))
		{
			await using ZipArchive archive = new(file, ZipArchiveMode.Create);
			ZipArchiveEntry entry = archive.CreateEntry("site/index.html");
			await using Stream content = await entry.OpenAsync(TestContext.Current.CancellationToken);
			await content.WriteAsync("zipped"u8.ToArray(), TestContext.Current.CancellationToken);
		}

		string folder = await ShellPathAsync(shell, temp.Path);
		(int extracted, _, string error) = await RunAsync(shell, RemoteScripts.Extract, RemoteScriptArguments.Extract(folder + "/site.zip", folder + "/site"));

		Assert.True(extracted == 0, error);
		Assert.Equal("zipped", await ReadAsync(shell, folder + "/site/site/index.html"));
	}

	[Fact]
	public async Task Extract_WithoutTheTool_SaysWhichOneIsMissingAndLeavesNoFolder()
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		string directory = await MakeFolderAsync(shell, "printf 'x' > \"$d/site.tar\"");
		try
		{
			// A PATH that leads nowhere is a server without tar.
			(int exitCode, byte[] output, string error) = await PosixShellLocator.RunAsync(
				shell,
				"PATH=/nonexistent-mokaterm\nexport PATH\n" + RemoteScripts.Extract,
				RemoteScriptArguments.Extract(directory + "/site.tar", directory + "/site"));

			Assert.True(RemoteOutput.TryGetPayload(output, out _));
			Assert.Equal(RemoteScripts.ExitUnsupported, exitCode);
			Assert.Equal("The server has no tar command, which this archive needs.", RemoteCommandErrors.ToException(exitCode, error, directory).Message);
			(int exists, _, _) = await PosixShellLocator.RunAsync(shell, "[ -e \"$1\" ]", [directory + "/site"]);
			Assert.NotEqual(0, exists);
		}
		finally
		{
			await RemoveAsync(shell, directory);
		}
	}

	private static Task<(int ExitCode, byte[] Output, string Error)> RunAsync(string shell, string script, IEnumerable<string> arguments) =>
		PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + script, arguments);

	private static async Task<string> MakeFolderAsync(string shell, string setup)
	{
		(int exitCode, byte[] created, string error) = await PosixShellLocator.RunAsync(
			shell,
			PosixShellLocator.UnixPathPrefix + "d=$(mktemp -d)\n" + setup + "\nprintf '%s' \"$d\"",
			[]);
		Assert.True(exitCode == 0, error);
		return Encoding.UTF8.GetString(created);
	}

	private static async Task<string> ReadAsync(string shell, string path)
	{
		(int exitCode, byte[] content, string error) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + "cat -- \"$1\"", [path]);
		Assert.True(exitCode == 0, error);
		return Encoding.UTF8.GetString(content);
	}

	private static async Task RemoveAsync(string shell, string directory) =>
		await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + "rm -rf -- \"$1\"", [directory]);

	// Git for Windows' sh sees C:\ as /c/; elsewhere a path is already the shell's own.
	private static async Task<string> ShellPathAsync(string shell, string path)
	{
		(int exitCode, byte[] converted, _) = await PosixShellLocator.RunAsync(
			shell,
			PosixShellLocator.UnixPathPrefix + "if command -v cygpath >/dev/null 2>&1; then cygpath -u \"$1\"; else printf '%s' \"$1\"; fi",
			[path]);
		Assert.Equal(0, exitCode);
		return Encoding.UTF8.GetString(converted).TrimEnd('\n', '\r');
	}
}
