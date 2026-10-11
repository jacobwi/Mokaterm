using System.Text;

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

	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Kept = ").Append(Bytes.Length).Append(", TotalBytes = ").Append(TotalBytes);
		return true;
	}
}
