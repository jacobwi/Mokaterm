using System.Text;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Ssh.Elevation;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests.Fakes;

/// <summary>
/// sudo on this machine's own POSIX shell. A <see cref="SudoRunner"/> made here sends the very command lines it would
/// send a server, and they run locally, where <c>sudo</c> is a shell function that reads the password line from stdin
/// the way <c>sudo -S</c> does, refuses a wrong one, and refuses every password once <see cref="Refuse"/> was called.
/// Every command is recorded with the input it was given.
/// </summary>
/// <param name="firstOnPath">A folder searched before the system's own, for a stand-in tool such as a find of another kind.</param>
internal sealed class LocalSudoShell(string shell, string? firstOnPath = null) : IDisposable
{
	/// <summary>The password this sudo takes, a value of the test's own.</summary>
	public const string Password = "local-test-password";

	private readonly string _refuseFile = Path.Combine(Path.GetTempPath(), "mokaterm-sudo-refuse-" + Guid.NewGuid().ToString("N"));
	private readonly List<Call> _calls = [];
	private readonly Lock _lock = new();

	public IReadOnlyList<Call> Calls
	{
		get
		{
			lock (_lock)
			{
				return [.. _calls];
			}
		}
	}

	/// <summary>A runner that sudo already let in with <see cref="Password"/>, as <c>AuthenticateAsync</c> leaves it.</summary>
	public SudoRunner Runner(string account = "abc@web01") =>
		new((command, input, runsLong, token) => StartAsync(command, input, runsLong, token), "sudo", account, SecretBuffer.FromString(Password));

	/// <summary>From now on sudo refuses, as it does when the password changed or the sudoers entry went.</summary>
	public void Refuse() => File.WriteAllText(_refuseFile, "");

	public void Dispose() => File.Delete(_refuseFile);

	private Task<IExecChannel> StartAsync(string command, SecretBuffer? input, bool runsLong, CancellationToken cancellationToken)
	{
		lock (_lock)
		{
			_calls.Add(new Call(command, input is null ? null : Encoding.UTF8.GetString(input.Span), runsLong));
		}

		return LocalExecChannel.StartAsync(shell, Prelude(), command, input, cancellationToken);
	}

	// sudo -S -p '' -- sh -c SCRIPT sh ARG...: four words before the command. Without -S there is no password to read.
	private string Prelude() => $$"""
		PATH={{(firstOnPath is null ? "" : PosixShell.Quote(firstOnPath) + ":")}}/usr/bin:/bin:$PATH
		export PATH
		sudo() {
			if [ "$1" != -S ]; then
				printf 'sudo: a password is required\n' >&2
				exit 1
			fi
			IFS= read -r given || given=
			shift 4
			if [ "$given" != {{PosixShell.Quote(Password)}} ] || [ -e {{PosixShell.Quote(_refuseFile.Replace('\\', '/'))}} ]; then
				printf 'Sorry, try again.\nsudo: 1 incorrect password attempt\n' >&2
				exit 1
			fi
			"$@"
		}
		""";

	/// <summary>One command as the runner sent it.</summary>
	/// <param name="Input">What went on stdin, without the line break; null for nothing.</param>
	public sealed record Call(string Command, string? Input, bool RunsLong);
}
