using System.Text;

namespace Mokaterm.Core.Import;

/// <summary>
/// Decodes a picked file. Registry exports are UTF-16 with a byte order mark, ini files are usually UTF-8, and a
/// file with no mark is read as UTF-8 without throwing on stray bytes.
/// </summary>
internal static class TextFileDecoder
{
	public static string Decode(ReadOnlySpan<byte> content)
	{
		if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xFE)
		{
			return Encoding.Unicode.GetString(content[2..]);
		}

		if (content.Length >= 2 && content[0] == 0xFE && content[1] == 0xFF)
		{
			return Encoding.BigEndianUnicode.GetString(content[2..]);
		}

		if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
		{
			return Encoding.UTF8.GetString(content[3..]);
		}

		return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetString(content);
	}
}
