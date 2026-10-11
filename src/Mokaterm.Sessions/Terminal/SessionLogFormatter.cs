using System.Buffers;
using System.Globalization;
using System.Text;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Sessions.Terminal;

/// <summary>
/// How one log format turns server output into file content: the header that opens a file, each chunk of output and
/// the notice that closes a file at its size cap. <see cref="SessionLogWriter"/> owns the file, the cap and the status;
/// a format only encodes, and keeps whatever state its encoding needs between chunks.
/// </summary>
internal abstract class SessionLogFormatter
{
	protected static readonly UTF8Encoding NoticeEncoding = new(encoderShouldEmitUTF8Identifier: false);

	/// <summary>The file name's extension, dot included.</summary>
	public abstract string Extension { get; }

	public static SessionLogFormatter For(SessionLogFormat format) => format switch
	{
		SessionLogFormat.Asciicast => new AsciicastLogFormatter(),
		SessionLogFormat.Raw => new TextLogFormatter(filter: null),
		_ => new TextLogFormatter(new TerminalTextFilter()),
	};

	/// <summary>What opens the file: what it is, when it started, and that nothing typed is in it.</summary>
	/// <param name="startedAt">Local time, which is what someone looking for the file afterwards remembers.</param>
	/// <param name="size">The terminal's size when the file opens.</param>
	public abstract byte[] Header(string endpoint, DateTimeOffset startedAt, TerminalSize size);

	/// <summary>
	/// Appends one chunk of output to <paramref name="output"/>, never more than <paramref name="room"/> bytes of it. False
	/// when the chunk did not fit whole, which tells the writer to close the file with <see cref="LimitNotice"/>.
	/// </summary>
	/// <param name="elapsed">Time since the file opened.</param>
	/// <param name="size">The terminal's size now. A format that records sizes notes a change before the output.</param>
	public abstract bool Append(ReadOnlySpan<byte> data, TimeSpan elapsed, TerminalSize size, long room, ArrayBufferWriter<byte> output);

	/// <summary>The last thing in a file that reached its cap. It goes past the cap: it is what says the file was cut.</summary>
	public abstract byte[] LimitNotice(long kilobytes, TimeSpan elapsed);

	/// <summary>The words every format closes a full file with, on lines of their own.</summary>
	protected static string LimitText(long kilobytes) => string.Create(
		CultureInfo.CurrentCulture,
		$"\r\n[mokaterm] the {kilobytes} KB limit was reached; the rest of this session is not recorded.\r\n");
}
