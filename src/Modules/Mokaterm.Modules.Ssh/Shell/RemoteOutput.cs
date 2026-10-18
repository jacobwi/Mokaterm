using System.Text;
using Mokaterm.Abstractions.FileSystem;

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

	/// <summary>
	/// What a script printed after the marker when it went through, or the <see cref="RemoteFileSystemException"/> its exit
	/// status and stderr describe. With <paramref name="allowPartialListing"/> a listing that exits 1 with entries still
	/// counts, because find and the shell fallback both exit 1 after an entry they could not read. A listing always starts
	/// with its format tag, so one that holds nothing past the tag is a folder that could not be read at all, which is
	/// refused like any other failure rather than shown as an empty folder.
	/// </summary>
	/// <param name="error">stderr of the script itself, without anything a wrapper such as sudo printed first.</param>
	public static ReadOnlyMemory<byte> Require(
		int? exitStatus,
		ReadOnlyMemory<byte> output,
		string error,
		string path,
		string? destination = null,
		bool allowPartialListing = false)
	{
		bool hasPayload = TryGetPayload(output, out ReadOnlyMemory<byte> payload);
		if (exitStatus == 0 || (allowPartialListing && exitStatus == 1 && hasPayload && HoldsMoreThanItsTag(payload.Span)))
		{
			return payload;
		}

		throw RemoteCommandErrors.ToException(exitStatus, error, path, destination);
	}

	private static bool HoldsMoreThanItsTag(ReadOnlySpan<byte> payload)
	{
		int tagEnd = payload.IndexOf((byte)0);
		return tagEnd >= 0 && tagEnd + 1 < payload.Length;
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

	/// <summary>
	/// The SHA-256 that <see cref="RemoteScripts.Checksum"/> printed, or null when the output does not start with one. GNU
	/// tools put a backslash in front of the line when the file name holds a backslash or a line break.
	/// </summary>
	public static string? ReadSha256(ReadOnlySpan<byte> payload) =>
		FileChecksums.Parse(ChecksumAlgorithm.Sha256, Encoding.UTF8.GetString(payload).TrimStart().TrimStart('\\'));
}
