using System.Globalization;
using System.Text;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>One piece of a file as <see cref="RemoteScripts.ReadRange"/> prints it: the file's version, then the bytes.</summary>
/// <param name="Version">
/// The file's device, inode, size and modification time as the server's <c>stat</c> printed them when the piece started,
/// compared whole between pieces; empty when the server could not say.
/// </param>
internal readonly record struct FilePiece(string Version, ReadOnlyMemory<byte> Bytes)
{
	// Four numbers; anything longer is not what the script prints.
	private const int MaxVersionBytes = 256;

	/// <summary>
	/// The file's size by <see cref="Version"/>, its third field, or null when the version is empty or not four numbers.
	/// It says where the file ends, so a reader never asks for a piece past it.
	/// </summary>
	public long? Size
	{
		get
		{
			string[] fields = Version.Split(' ');
			return fields.Length == 4
				&& fields.All(number => number.Length > 0 && number.All(char.IsAsciiDigit))
				&& long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out long size)
					? size
					: null;
		}
	}

	/// <summary>Reads what <see cref="RemoteScripts.ReadRange"/> printed after its marker.</summary>
	/// <exception cref="RemoteFileSystemException">The output does not start with a version, so it is not the script's.</exception>
	public static FilePiece Parse(ReadOnlyMemory<byte> payload, string path)
	{
		int end = payload.Span[..Math.Min(payload.Length, MaxVersionBytes + 1)].IndexOf((byte)0);
		if (end < 0)
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.Unknown, $"The server sent {path} in a form this app does not read.", path);
		}

		return new FilePiece(Encoding.UTF8.GetString(payload.Span[..end]), payload[(end + 1)..]);
	}

	/// <summary>The piece without its first <paramref name="count"/> bytes.</summary>
	public FilePiece Skip(int count) => this with { Bytes = count >= Bytes.Length ? ReadOnlyMemory<byte>.Empty : Bytes[count..] };
}
