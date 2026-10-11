using System.Security.Cryptography;
using System.Text;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>The checksum and read-from-offset scripts, run in a local POSIX shell when there is one, and their output parsed.</summary>
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
	public async Task ReadFrom_StreamsTheFileFromTheGivenByte()
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		byte[] content = RandomNumberGenerator.GetBytes(10_000);
		string directory = await MakeDirectoryAsync(shell, "");
		try
		{
			await File.WriteAllBytesAsync(await NativePathAsync(shell, directory + "/data.bin"), content, TestContext.Current.CancellationToken);

			// The caller passes the offset plus one: tail counts its bytes from one.
			(int exitCode, byte[] output, string error) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + RemoteScripts.ReadFrom, [directory + "/data.bin", "6001"]);

			Assert.True(exitCode == 0, error);
			Assert.True(RemoteOutput.TryGetPayload(output, out ReadOnlyMemory<byte> payload));
			Assert.Equal(content[6000..], payload.ToArray());
		}
		finally
		{
			await RemoveDirectoryAsync(shell, directory);
		}
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
