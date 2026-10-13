using System.Security.Cryptography;
using System.Text;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>The checksum and ranged read scripts, run in a local POSIX shell when there is one, and their output parsed.</summary>
public sealed class RemoteChecksumTests
{
	private const string Sha256OfHello = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";

	[Theory]
	[InlineData(Sha256OfHello + "  /srv/hello.txt\n")]
	[InlineData(Sha256OfHello + " *hello.txt\n")]
	[InlineData(Sha256OfHello + "\n")]
	[InlineData("\\" + Sha256OfHello + "  /srv/odd\\\\name\n")]
	public void ReadSha256_TakesTheHashAtTheStartOfWhateverTheToolPrinted(string output) =>
		Assert.Equal(Sha256OfHello, RemoteOutput.ReadSha256(Encoding.UTF8.GetBytes(output)));

	[Theory]
	[InlineData("")]
	[InlineData("sha256sum: /srv/hello.txt: Permission denied\n")]
	[InlineData("2cf24dba5fb0a30e26e83b2ac5b9e29e\n")]
	public void ReadSha256_WithoutAHash_IsNull(string output) =>
		Assert.Null(RemoteOutput.ReadSha256(Encoding.UTF8.GetBytes(output)));

	[Fact]
	public async Task Checksum_OfAFile_IsItsSha256()
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		string directory = await MakeDirectoryAsync(shell, "printf 'hello' > \"$d/hello.txt\"");
		try
		{
			(int exitCode, byte[] output, string error) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + RemoteScripts.Checksum, [directory + "/hello.txt"]);

			Assert.True(exitCode == 0, error);
			Assert.True(RemoteOutput.TryGetPayload(output, out ReadOnlyMemory<byte> payload));
			Assert.Equal(Sha256OfHello, RemoteOutput.ReadSha256(payload.Span));
		}
		finally
		{
			await RemoveDirectoryAsync(shell, directory);
		}
	}

	[Theory]
	[InlineData("missing.txt", RemoteScripts.ExitNotFound)]
	[InlineData("", RemoteScripts.ExitWrongKind)]
	public async Task Checksum_OfSomethingThatIsNotAFile_ExitsWithItsCode(string name, int expected)
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		string directory = await MakeDirectoryAsync(shell, "");
		try
		{
			(int exitCode, _, _) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + RemoteScripts.Checksum, [directory + "/" + name]);

			Assert.Equal(expected, exitCode);
		}
		finally
		{
			await RemoveDirectoryAsync(shell, directory);
		}
	}

	[Fact]
	public async Task ReadRange_PrintsTheFilesVersion_ThenThatPartOfIt_FromWholeBlocks()
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		byte[] content = RandomNumberGenerator.GetBytes(200_000);
		string directory = await MakeDirectoryAsync(shell, "");
		try
		{
			string path = directory + "/data.bin";
			await File.WriteAllBytesAsync(await NativePathAsync(shell, path), content, TestContext.Current.CancellationToken);

			// Block 1 is byte 65,536 on; 100,000 bytes from there.
			FilePiece middle = await ReadRangeAsync(shell, path, 1, 100_000);

			// From block 2 the file holds less than asked, which is how the end shows.
			FilePiece end = await ReadRangeAsync(shell, path, 2, 100_000);

			// Past the end there is nothing, and still a version.
			FilePiece past = await ReadRangeAsync(shell, path, 10, 4096);

			Assert.Equal(content.AsSpan(65_536, 100_000).ToArray(), middle.Bytes.ToArray());
			Assert.Equal(content.AsSpan(131_072).ToArray(), end.Bytes.ToArray());
			Assert.True(past.Bytes.IsEmpty);

			// device, inode, size, modification time: the size is the third field, and an unchanged file says the same.
			string[] fields = middle.Version.Split(' ');
			Assert.Equal(4, fields.Length);
			Assert.Equal("200000", fields[2]);
			Assert.Equal(middle.Version, end.Version);
			Assert.Equal(middle.Version, past.Version);

			await File.AppendAllTextAsync(await NativePathAsync(shell, path), "more", TestContext.Current.CancellationToken);
			FilePiece grown = await ReadRangeAsync(shell, path, 0, 16);

			Assert.NotEqual(middle.Version, grown.Version);
			Assert.Equal(content.AsSpan(0, 16).ToArray(), grown.Bytes.ToArray());
		}
		finally
		{
			await RemoveDirectoryAsync(shell, directory);
		}
	}

	[Theory]
	[InlineData("missing.bin", RemoteScripts.ExitNotFound)]
	[InlineData("", RemoteScripts.ExitWrongKind)]
	public async Task ReadRange_OfSomethingThatIsNotAFile_ExitsWithItsCode(string name, int expected)
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		string directory = await MakeDirectoryAsync(shell, "");
		try
		{
			(int exitCode, _, _) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + RemoteScripts.ReadRange, [directory + "/" + name, "0", "16"])
				.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

			Assert.Equal(expected, exitCode);
		}
		finally
		{
			await RemoveDirectoryAsync(shell, directory);
		}
	}

	private static async Task<FilePiece> ReadRangeAsync(string shell, string path, long block, int count)
	{
		(int exitCode, byte[] output, string error) = await PosixShellLocator.RunAsync(
			shell,
			PosixShellLocator.UnixPathPrefix + RemoteScripts.ReadRange,
			[path, block.ToString(System.Globalization.CultureInfo.InvariantCulture), count.ToString(System.Globalization.CultureInfo.InvariantCulture)])
			.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

		Assert.True(exitCode == 0, error);
		Assert.True(RemoteOutput.TryGetPayload(output, out ReadOnlyMemory<byte> payload));
		return FilePiece.Parse(payload, path);
	}

	private static async Task<string> MakeDirectoryAsync(string shell, string fill)
	{
		(int exitCode, byte[] created, string error) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + "d=$(mktemp -d)\n" + fill + "\nprintf '%s' \"$d\"\n", []);
		Assert.True(exitCode == 0, error);
		return Encoding.UTF8.GetString(created);
	}

	private static async Task RemoveDirectoryAsync(string shell, string directory) =>
		await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + "rm -rf -- \"$1\"\n", [directory]);

	// Git for Windows hands out /tmp paths; a .NET file call needs the Windows form of the same place.
	private static async Task<string> NativePathAsync(string shell, string path)
	{
		if (!OperatingSystem.IsWindows())
		{
			return path;
		}

		(int exitCode, byte[] output, string error) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + "cygpath -w -- \"$1\"\n", [path]);
		Assert.True(exitCode == 0, error);
		return Encoding.UTF8.GetString(output).TrimEnd();
	}
}
