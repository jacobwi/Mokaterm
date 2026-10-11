using System.Security.Cryptography;
using System.Text;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Ssh.Elevation;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Admin;

/// <summary>
/// What goes around an <see cref="AdminScript"/> on the wire, and how its output comes back. The parts here are
/// plumbing, which is why an action shows <see cref="AdminScript.Body"/> and not this.
/// </summary>
internal static class AdminScriptText
{
	/// <summary>The line in front of the stdin payload. Anything before it is skipped, see <see cref="InputReader"/>.</summary>
	public const string InputMarker = "MOKATERM-STDIN";

	/// <summary>Starts every script, so a demo or a log reader can tell which one it is without parsing the body.</summary>
	public const string TagPrefix = "# mokaterm system view: ";

	// C locale keeps tool messages in English and their columns stable. The sbin folders are appended because Debian
	// leaves them off an ordinary login's PATH, where ip, ufw and useradd live, and command -v would miss them.
	private const string Preamble = """
		LC_ALL=C
		export LC_ALL
		PATH="$PATH:/usr/local/sbin:/usr/sbin:/sbin"
		export PATH
		printf 'MOKATERM-OUT\0'

		""";

	// sudo -S reads its password line from stdin only when it needs one: with a cached ticket or NOPASSWD it reads
	// nothing, and that line would reach chpasswd or crontab as data. So the payload follows a marker line, and
	// whatever comes before the marker is dropped. read takes one byte at a time from a pipe, so the command after it
	// starts exactly at the payload. A payload that never arrived must not become an empty crontab.
	private const string InputReader = """
		mokaterm_found=
		while IFS= read -r mokaterm_line; do
			if [ "$mokaterm_line" = MOKATERM-STDIN ]; then mokaterm_found=1; break; fi
		done
		mokaterm_line=
		if [ -z "$mokaterm_found" ]; then echo 'The input for this command did not arrive.' >&2; exit 1; fi

		""";

	/// <summary>The whole script that is sent: the tag, the preamble, the stdin reader when needed, then the body.</summary>
	public static string Compose(AdminScript script)
	{
		ArgumentNullException.ThrowIfNull(script);
		string text = TagPrefix + script.Name + "\n" + Preamble + (script.ReadsInput ? InputReader : "") + script.Body + "\n";

		// Raw strings take this file's line endings, and a carriage return breaks sh.
		return text.ReplaceLineEndings("\n");
	}

	/// <summary>
	/// The stdin bytes after any password line: the marker line, then the payload without its final line break, which
	/// <see cref="RemoteCommand.StartAsync"/> adds back when it writes the line.
	/// </summary>
	public static SecretBuffer FrameInput(AdminInput input)
	{
		ArgumentNullException.ThrowIfNull(input);
		ReadOnlySpan<byte> payload = input.Bytes.Span;
		if (!payload.IsEmpty && payload[^1] == (byte)'\n')
		{
			payload = payload[..^1];
		}

		ReadOnlySpan<byte> marker = "MOKATERM-STDIN\n"u8;
		byte[] framed = GC.AllocateArray<byte>(marker.Length + payload.Length, pinned: true);
		try
		{
			marker.CopyTo(framed);
			payload.CopyTo(framed.AsSpan(marker.Length));
			return SecretBuffer.FromBytes(framed);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(framed);
		}
	}

	/// <summary>The request a script runs under: its own output cap.</summary>
	public static RemoteCommandRequest Request(AdminScript script, string commandText) => new()
	{
		Command = commandText,
		OutputLimitBytes = Math.Max(script.OutputLimitBytes, RemoteCommandRequest.MinOutputLimitBytes),
	};

	/// <summary>
	/// Reads an exec outcome back: stdout from the output marker on, stderr as text. A script whose marker never came
	/// did not run at all (a restricted shell, a forced command), and that is reported rather than parsed.
	/// </summary>
	/// <exception cref="AdminException">The script never started.</exception>
	public static AdminOutcome ToOutcome(RemoteCommandOutcome outcome, string scriptError)
	{
		ArgumentNullException.ThrowIfNull(outcome);
		if (!RemoteOutput.TryGetPayload(outcome.Output.Bytes, out ReadOnlyMemory<byte> payload))
		{
			string reason = SudoErrors.FirstLine(scriptError);
			throw new AdminException(reason.Length > 0
				? $"The server did not run the script: {reason}"
				: "The server did not run the script. The login may be limited to a forced command or a restricted shell.");
		}

		bool truncated = outcome.Output.IsTruncated;
		ReadOnlySpan<byte> bytes = payload.Span;
		if (truncated)
		{
			// Half a line would parse as a value that is not there.
			int lastBreak = bytes.LastIndexOf((byte)'\n');
			bytes = lastBreak < 0 ? [] : bytes[..(lastBreak + 1)];
		}

		return new AdminOutcome
		{
			ExitCode = outcome.ExitCode,
			ExitSignal = outcome.ExitSignal,
			Output = Encoding.UTF8.GetString(bytes).ReplaceLineEndings("\n"),
			Error = scriptError.ReplaceLineEndings("\n"),
			IsTruncated = truncated,
			Duration = outcome.Duration,
		};
	}

	/// <summary>Stderr as text.</summary>
	public static string ErrorText(RemoteCommandOutcome outcome)
	{
		ArgumentNullException.ThrowIfNull(outcome);
		return Encoding.UTF8.GetString(outcome.Error.Bytes.Span);
	}
}
