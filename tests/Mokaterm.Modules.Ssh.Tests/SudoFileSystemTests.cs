using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Elevation;
using Mokaterm.Modules.Ssh.FileSystem;
using Mokaterm.Modules.Ssh.Shell;
using Mokaterm.Modules.Ssh.Tests.Fakes;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>
/// Root reads through the runner's own command lines, run against this machine's POSIX shell with a stand-in sudo: the
/// file arrives in bounded pieces, every piece carries the password sudo took at the start, a file that changes or a sudo
/// that stops letting the login in fails the read for what it is, and nothing streams a whole file through one channel
/// any more. Skipped where there is no sh, apart from what needs none.
/// </summary>
public sealed class SudoFileSystemTests : IAsyncLifetime
{
	private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

	private readonly string? _shell = PosixShellLocator.Find();
	private LocalSudoShell? _sudo;
	private string _folder = "";

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	public async ValueTask InitializeAsync()
	{
		if (_shell is null)
		{
			return;
		}

		_sudo = new LocalSudoShell(_shell);
		_folder = await ShellTextAsync("d=$(mktemp -d)\nmkdir \"$d/sub dir\"\nprintf '%s' \"$d\"");
	}

	public async ValueTask DisposeAsync()
	{
		_sudo?.Dispose();
		if (_shell is not null && _folder.Length > 0)
		{
			await PosixShellLocator.RunAsync(_shell, PosixShellLocator.UnixPathPrefix + "rm -rf -- \"$1\"", [_folder]).WaitAsync(Limit, Ct);
		}
	}

	[Fact]
	public async Task Read_ComesInPiecesAsRoot_EachWithThePasswordSudoTook()
	{
		string path = _folder + "/data.bin";
		byte[] expected = await MakeFileAsync(path, 300_004);
		await using SudoFileSystem files = Open();

		await using (Stream stream = await files.OpenReadAsync(path, Ct).AsTask().WaitAsync(Limit, Ct))
		{
			using MemoryStream read = new();
			await stream.CopyToAsync(read, Ct).WaitAsync(Limit, Ct);
			Assert.Equal(expected, read.ToArray());
		}

		// 256 KiB, then a 1 MiB piece that comes back short, which is the end of the file. Each is the same sudo -S with
		// the same password line, counted as long like the streamed read it replaces, and nothing else ran.
		IReadOnlyList<LocalSudoShell.Call> calls = Sudo.Calls;
		Assert.Equal(2, calls.Count);
		Assert.All(calls, call =>
		{
			Assert.Contains(PosixShellQuoted(RemoteScripts.ReadRange), call.Command, StringComparison.Ordinal);
			Assert.Contains("-S -p ''", call.Command, StringComparison.Ordinal);
			Assert.Equal(LocalSudoShell.Password, call.Input);
			Assert.True(call.RunsLong);
		});
	}

	[Fact]
	public async Task ADownloadPastTheWarmUp_ReadsAhead_AndAsksForNothingPastTheEnd()
	{
		string path = _folder + "/big.bin";
		byte[] expected = await MakeFileAsync(path, 6 * 1024 * 1024);
		await using SudoFileSystem files = Open();
		using MemoryStream destination = new();

		await files.DownloadAsync(path, destination, progress: null, Ct).AsTask().WaitAsync(Limit, Ct);

		// 256 KiB, 1 MiB, then the 4 MiB third and the short fourth asked for together; the size stat gave says that
		// is all, so no empty fifth piece follows.
		Assert.Equal(expected, destination.ToArray());
		IReadOnlyList<LocalSudoShell.Call> calls = Sudo.Calls;
		Assert.Equal(4, calls.Count);
		Assert.Single(calls, call => call.Command.EndsWith(" '20' '4194304'", StringComparison.Ordinal));
		Assert.Single(calls, call => call.Command.EndsWith(" '84' '4194304'", StringComparison.Ordinal));
		Assert.All(calls, call =>
		{
			Assert.Equal(LocalSudoShell.Password, call.Input);
			Assert.True(call.RunsLong);
		});
	}

	[Fact]
	public async Task Download_FromAnOffsetInsideABlock_SendsExactlyTheRest()
	{
		string path = _folder + "/data.bin";
		byte[] expected = await MakeFileAsync(path, 150_000);
		await using SudoFileSystem files = Open();
		using MemoryStream destination = new();
		List<long> progress = [];

		await files.DownloadAsync(path, destination, 70_000, new Progress(progress), Ct).AsTask().WaitAsync(Limit, Ct);

		Assert.Equal(expected.AsSpan(70_000).ToArray(), destination.ToArray());
		Assert.Equal(80_000, progress[^1]);

		// The size check of a download that continues, then pieces from the block that holds byte 70,000, the bytes
		// before it in that block read and dropped: block 1, 4,464 bytes of it and then the first piece.
		Assert.Contains(PosixShellQuoted(RemoteScripts.List), Sudo.Calls[0].Command, StringComparison.Ordinal);
		Assert.EndsWith(" '1' '266608'", Sudo.Calls[1].Command, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Read_OfAFolderOrNothing_FailsWhenOpened()
	{
		await using SudoFileSystem files = Open();

		RemoteFileSystemException missing = await Assert.ThrowsAsync<RemoteFileSystemException>(
			async () => await files.OpenReadAsync(_folder + "/nope", Ct).AsTask().WaitAsync(Limit, Ct));
		RemoteFileSystemException folder = await Assert.ThrowsAsync<RemoteFileSystemException>(
			async () => await files.OpenReadAsync(_folder + "/sub dir", Ct).AsTask().WaitAsync(Limit, Ct));

		Assert.Equal(RemoteFileErrorKind.NotFound, missing.Kind);
		Assert.Equal(_folder + "/nope does not exist.", missing.Message);
		Assert.Contains("is a directory", folder.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AFileThatGrowsWhileItIsRead_IsRefused_RatherThanJoinedFromTwoVersions()
	{
		string path = _folder + "/app.log";
		await MakeFileAsync(path, 300_000);
		await using SudoFileSystem files = Open();
		await using Stream stream = await files.OpenReadAsync(path, Ct).AsTask().WaitAsync(Limit, Ct);
		byte[] first = new byte[PieceReadStream.FirstPieceBytes];
		await stream.ReadExactlyAsync(first, Ct).AsTask().WaitAsync(Limit, Ct);

		await ShellTextAsync("printf 'one more line\\n' >> \"$1\"", path);
		RemoteFileSystemException changed = await Assert.ThrowsAsync<RemoteFileSystemException>(
			async () => await stream.ReadAsync(first, Ct).AsTask().WaitAsync(Limit, Ct));

		Assert.Contains("changed on the server while it was being read", changed.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ASudoThatRefusesInTheMiddle_FailsTheReadAsElevationFailed_WithoutAskingAgain()
	{
		string path = _folder + "/data.bin";
		await MakeFileAsync(path, 600_000);
		await using SudoFileSystem files = Open();
		await using Stream stream = await files.OpenReadAsync(path, Ct).AsTask().WaitAsync(Limit, Ct);
		byte[] first = new byte[PieceReadStream.FirstPieceBytes];
		await stream.ReadExactlyAsync(first, Ct).AsTask().WaitAsync(Limit, Ct);

		Sudo.Refuse();
		RemoteFileSystemException refused = await Assert.ThrowsAsync<RemoteFileSystemException>(
			async () => await stream.ReadAsync(first, Ct).AsTask().WaitAsync(Limit, Ct));

		Assert.Equal(RemoteFileErrorKind.ElevationFailed, refused.Kind);
		Assert.Equal("sudo did not accept the password for abc@web01.", refused.Message);
		Assert.Equal(2, Sudo.Calls.Count);
	}

	[Fact]
	public async Task AServerThatSendsMoreThanAPiece_IsStoppedAtThePiece()
	{
		EndlessStream flood = new();
		SudoRunner runner = new(
			(_, _, _, _) => Task.FromResult<IExecChannel>(new FakeExecChannel(flood, new MemoryStream(), exitStatus: null, execution: new TaskCompletionSource().Task)),
			"sudo",
			"abc@web01",
			password: null);
		await using SudoFileSystem files = Open(runner);

		RemoteFileSystemException refused = await Assert.ThrowsAsync<RemoteFileSystemException>(
			async () => await files.OpenReadAsync("/var/log/syslog", Ct).AsTask().WaitAsync(Limit, Ct));

		Assert.Contains("more than 320 KB for one command", refused.Message, StringComparison.Ordinal);
		Assert.InRange(flood.Served, PieceReadStream.FirstPieceBytes, PieceReadStream.FirstPieceBytes + (256 * 1024));
	}

	[Fact]
	public void NothingReadsAWholeFileThroughOneChannel()
	{
		Assembly module = typeof(RemoteScripts).Assembly;
		Assert.Null(module.GetType("Mokaterm.Modules.Ssh.Elevation.ElevatedReadStream"));
		Assert.Null(module.GetType("Mokaterm.Modules.Ssh.Docker.ContainerReadStream"));
		Assert.Null(typeof(RemoteCommand).GetMethod("ReadError", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));

		// Nothing the runner offers hands out a live stream: a script runs to its end and its output is capped.
		MethodInfo[] runnerMethods = typeof(SudoRunner).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		Assert.DoesNotContain(runnerMethods, method => ReturnsStream(method.ReturnType));

		// The scripts that once sent a whole file to stdout are gone, and no script does it under another name.
		List<string> scripts = [.. typeof(RemoteScripts).GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(field => field.FieldType == typeof(string) && field.IsInitOnly)
			.Select(field => field.Name)];
		Assert.DoesNotContain("Read", scripts);
		Assert.DoesNotContain("ReadFrom", scripts);
		Assert.All(
			typeof(RemoteScripts).GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.FieldType == typeof(string) && field.IsInitOnly),
			field =>
			{
				string script = (string)field.GetValue(null)!;
				Assert.DoesNotContain("exec cat --", script, StringComparison.Ordinal);
				Assert.DoesNotContain("exec tail -c", script, StringComparison.Ordinal);
			});

		// Both file systems read through the one piece stream of their base.
		Assert.Null(typeof(SudoFileSystem).GetMethod(nameof(IRemoteFileSystem.OpenReadAsync), BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly));
		Assert.Null(typeof(ContainerFileSystem).GetMethod(nameof(IRemoteFileSystem.OpenReadAsync), BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly));
	}

	private static bool ReturnsStream(Type type) =>
		typeof(Stream).IsAssignableFrom(type)
		|| (type.IsGenericType && type.GetGenericArguments().Any(ReturnsStream));

	private static string PosixShellQuoted(string script) =>
		Abstractions.Commands.PosixShell.Quote(RemoteScripts.ElevatedPreamble + script);

	private LocalSudoShell Sudo => _sudo ?? throw new InvalidOperationException("No POSIX sh on this machine.");

	private SudoFileSystem Open(SudoRunner? runner = null)
	{
		if (runner is null)
		{
			SkipWithoutShell();
		}

		return new SudoFileSystem(
			runner ?? Sudo.Runner(),
			_ => ValueTask.FromException<SftpFileSystem>(new InvalidOperationException("Reads never need the login's SFTP.")),
			new RemoteAccountNames(() => null, NullLogger.Instance),
			"/tmp",
			NullLogger.Instance,
			_ => { });
	}

	private async Task<byte[]> MakeFileAsync(string path, int length)
	{
		SkipWithoutShell();
		byte[] content = new byte[length];
		Random.Shared.NextBytes(content);
		string native = await ShellTextAsync(OperatingSystem.IsWindows() ? "cygpath -w -- \"$1\"" : "printf '%s' \"$1\"", path);
		await File.WriteAllBytesAsync(native.TrimEnd(), content, Ct).WaitAsync(Limit, Ct);
		return content;
	}

	private void SkipWithoutShell()
	{
		if (_shell is null)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
		}
	}

	private async Task<string> ShellTextAsync(string script, params string[] arguments)
	{
		SkipWithoutShell();
		(int exitCode, byte[] output, string error) = await PosixShellLocator.RunAsync(_shell!, PosixShellLocator.UnixPathPrefix + script, arguments).WaitAsync(Limit, Ct);
		Assert.True(exitCode == 0, error);
		return Encoding.UTF8.GetString(output);
	}

	/// <summary>Reports synchronously, so the last value is there when the download returns.</summary>
	private sealed class Progress(List<long> values) : IProgress<long>
	{
		public void Report(long value) => values.Add(value);
	}
}
