using System.Globalization;
using System.Text;

namespace Mokaterm.Core.Import;

/// <summary>
/// Reads a <c>.reg</c> export, because people carry those between machines far more often than they carry a live
/// registry. Only the value kinds sessions use are decoded: strings, dwords and hex-encoded strings.
/// </summary>
internal static class RegFileReader
{
	public static IReadOnlyList<ImportSection> Parse(ReadOnlySpan<byte> content) => ParseText(TextFileDecoder.Decode(content));

	internal static IReadOnlyList<ImportSection> ParseText(string text)
	{
		List<ImportSection> sections = [];
		Dictionary<string, string>? values = null;
		string path = "";

		foreach (string line in JoinContinuations(text))
		{
			string trimmed = line.Trim();
			if (trimmed.Length == 0 || trimmed[0] == ';')
			{
				continue;
			}

			if (trimmed[0] == '[' && trimmed[^1] == ']')
			{
				Flush();
				string inner = trimmed[1..^1];

				// A leading minus means the export deletes the key; there is nothing to import from it.
				if (inner.StartsWith('-'))
				{
					path = "";
					values = null;
					continue;
				}

				path = inner;
				values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				continue;
			}

			if (values is null)
			{
				continue;
			}

			if (TryReadValue(trimmed, out string name, out string? value) && value is not null)
			{
				values[name] = value;
			}
		}

		Flush();
		return sections;

		void Flush()
		{
			if (values is { Count: > 0 })
			{
				sections.Add(new ImportSection(path, values));
			}

			values = null;
		}
	}

	/// <summary>A hex value is written over several lines, each one but the last ending in a backslash.</summary>
	private static List<string> JoinContinuations(string text)
	{
		List<string> lines = [];
		StringBuilder pending = new();
		foreach (string raw in text.Split('\n'))
		{
			string line = raw.TrimEnd('\r');
			string trimmed = line.TrimEnd();
			if (trimmed.EndsWith('\\'))
			{
				pending.Append(trimmed.AsSpan(0, trimmed.Length - 1).TrimEnd());
				continue;
			}

			if (pending.Length > 0)
			{
				pending.Append(line.TrimStart());
				lines.Add(pending.ToString());
				pending.Clear();
				continue;
			}

			lines.Add(line);
		}

		if (pending.Length > 0)
		{
			lines.Add(pending.ToString());
		}

		return lines;
	}

	private static bool TryReadValue(string line, out string name, out string? value)
	{
		name = "";
		value = null;
		if (line.Length == 0)
		{
			return false;
		}

		int separator;
		if (line[0] == '"')
		{
			int closing = FindClosingQuote(line, 1);
			if (closing < 0 || closing + 1 >= line.Length || line[closing + 1] != '=')
			{
				return false;
			}

			name = Unescape(line[1..closing]);
			separator = closing + 1;
		}
		else if (line.StartsWith("@=", StringComparison.Ordinal))
		{
			name = "";
			separator = 1;
		}
		else
		{
			return false;
		}

		value = ReadPayload(line[(separator + 1)..]);
		return value is not null;
	}

	private static string? ReadPayload(string payload)
	{
		string trimmed = payload.Trim();
		if (trimmed.Length == 0)
		{
			return null;
		}

		if (trimmed[0] == '"')
		{
			int closing = FindClosingQuote(trimmed, 1);
			return closing < 0 ? null : Unescape(trimmed[1..closing]);
		}

		if (trimmed.StartsWith("dword:", StringComparison.OrdinalIgnoreCase))
		{
			return uint.TryParse(trimmed.AsSpan(6).Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint number)
				? number.ToString(CultureInfo.InvariantCulture)
				: null;
		}

		// hex(1) and hex(2) are strings stored as their UTF-16 bytes; plain hex and hex(7) are not single strings.
		if (trimmed.StartsWith("hex(1):", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("hex(2):", StringComparison.OrdinalIgnoreCase))
		{
			byte[]? bytes = ReadHexBytes(trimmed[7..]);
			if (bytes is null)
			{
				return null;
			}

			string decoded = Encoding.Unicode.GetString(bytes);
			int end = decoded.IndexOf('\0', StringComparison.Ordinal);
			return end < 0 ? decoded : decoded[..end];
		}

		return null;
	}

	private static byte[]? ReadHexBytes(string payload)
	{
		string[] parts = payload.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		byte[] bytes = new byte[parts.Length];
		for (int i = 0; i < parts.Length; i++)
		{
			if (!byte.TryParse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
			{
				return null;
			}
		}

		return bytes;
	}

	private static int FindClosingQuote(string text, int start)
	{
		for (int i = start; i < text.Length; i++)
		{
			if (text[i] == '\\')
			{
				i++;
				continue;
			}

			if (text[i] == '"')
			{
				return i;
			}
		}

		return -1;
	}

	private static string Unescape(string text)
	{
		if (!text.Contains('\\', StringComparison.Ordinal))
		{
			return text;
		}

		StringBuilder builder = new(text.Length);
		for (int i = 0; i < text.Length; i++)
		{
			if (text[i] == '\\' && i + 1 < text.Length)
			{
				i++;
			}

			builder.Append(text[i]);
		}

		return builder.ToString();
	}
}
