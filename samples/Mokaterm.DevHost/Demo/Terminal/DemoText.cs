using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Unicode;

namespace Mokaterm.DevHost.Demo.Terminal;

/// <summary>Decides whether stored file bytes are text a terminal can print.</summary>
internal static class DemoText
{
	/// <summary>True for valid UTF-8 without NUL bytes, the way <c>grep</c> tells text from binary.</summary>
	public static bool TryDecode(byte[] content, [NotNullWhen(true)] out string? text)
	{
		if (content.AsSpan().Contains((byte)0) || !Utf8.IsValid(content))
		{
			text = null;
			return false;
		}

		text = Encoding.UTF8.GetString(content);
		return true;
	}

	/// <summary>Turns bare line feeds into CRLF, the way a pty does with output.</summary>
	public static string ToTerminalLines(string text) =>
		text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
}
