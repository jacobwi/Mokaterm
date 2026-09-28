using System.Text;

namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>Reads the stdout of <see cref="RemoteScripts"/>: skips to the output marker and splits NUL-terminated fields.</summary>
internal static class RemoteOutput
{
	/// <summary><see cref="RemoteScripts.OutputMarker"/> followed by its NUL terminator.</summary>
	public static ReadOnlySpan<byte> Marker => "MOKATERM-OUT\0"u8;

	/// <summary>The bytes after the marker, or false when the script never started (nothing but login noise, or nothing at all).</summary>
	public static bool TryGetPayload(ReadOnlyMemory<byte> output, out ReadOnlyMemory<byte> payload)
	{
		int index = output.Span.IndexOf(Marker);
		if (index < 0)
		{
			payload = ReadOnlyMemory<byte>.Empty;
			return false;
		}

		payload = output[(index + Marker.Length)..];
		return true;
	}

	/// <summary>Splits NUL-terminated UTF-8 fields. A trailing field without its terminator is incomplete and dropped.</summary>
	public static List<string> SplitFields(ReadOnlySpan<byte> payload)
	{
		List<string> fields = [];
		while (true)
		{
			int end = payload.IndexOf((byte)0);
			if (end < 0)
			{
				return fields;
			}

			fields.Add(Encoding.UTF8.GetString(payload[..end]));
			payload = payload[(end + 1)..];
		}
	}

	/// <summary>The payload as text without the one trailing newline commands such as <c>pwd</c> print.</summary>
	public static string ReadLine(ReadOnlySpan<byte> payload)
	{
		string text = Encoding.UTF8.GetString(payload);
		return text.EndsWith('\n') ? text[..^1] : text;
	}
}
