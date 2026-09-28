using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Mokaterm.Abstractions.Terminal;

/// <summary>
/// Resolves the character set a terminal connection names, such as <c>telnet.encoding</c> or <c>serial.encoding</c>.
/// </summary>
/// <remarks>
/// A file transfer resolves its own: a byte a name cannot hold must be reported rather than substituted, which is the
/// opposite of what a terminal wants, so the FTP module keeps its own lookup.
/// </remarks>
public static class TerminalEncodings
{
	/// <summary>What a connection uses when it names no character set.</summary>
	public const string Default = "utf-8";

	// Switch consoles, bootloaders, PLCs and other old hardware speak code pages such as windows-1252 or ibm437,
	// which .NET only knows once this provider is registered. The type initializer runs once, before any lookup on
	// any thread.
	static TerminalEncodings() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

	public static bool TryGet(string? name, [NotNullWhen(true)] out Encoding? encoding)
	{
		encoding = null;
		string trimmed = name?.Trim() ?? "";
		if (trimmed.Length == 0)
		{
			return false;
		}

		try
		{
			// A damaged character must not end a session, so both directions substitute instead of throwing.
			encoding = Encoding.GetEncoding(trimmed, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
		}
		catch (ArgumentException)
		{
			return false;
		}
		catch (NotSupportedException)
		{
			return false;
		}

		return true;
	}

	/// <summary>The named character set, or UTF-8 when the name is blank or unknown.</summary>
	public static Encoding Resolve(string? name) => TryGet(name, out Encoding? encoding) ? encoding : Encoding.UTF8;

	/// <summary>True when the terminal's own UTF-8 bytes can travel unchanged, which skips all transcoding.</summary>
	public static bool IsUtf8(Encoding encoding)
	{
		ArgumentNullException.ThrowIfNull(encoding);
		return encoding.CodePage == Encoding.UTF8.CodePage;
	}
}
