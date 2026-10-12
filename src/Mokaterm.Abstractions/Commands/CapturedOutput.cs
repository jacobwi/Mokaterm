using System.Text;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Abstractions.Commands;

/// <summary>
/// What a command wrote to one stream: the first bytes, as many as the request kept, and how many there were in all.
/// Prints its sizes and never its bytes.
/// </summary>
public sealed record CapturedOutput(ReadOnlyMemory<byte> Bytes, long TotalBytes)
{
	public static CapturedOutput Empty { get; } = new(ReadOnlyMemory<byte>.Empty, 0);

	/// <summary>True when the stream held more than <see cref="Bytes"/>.</summary>
	public bool IsTruncated => TotalBytes > Bytes.Length;

	/// <summary>The kept bytes as UTF-8 text, without a last character the cap cut in half.</summary>
	public string ToText()
	{
		ReadOnlySpan<byte> bytes = Bytes.Span;
		return Encoding.UTF8.GetString(IsTruncated ? bytes[..Utf8Cut.CompleteLength(bytes)] : bytes);
	}

	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Kept = ").Append(Bytes.Length).Append(", TotalBytes = ").Append(TotalBytes);
		return true;
	}
}
