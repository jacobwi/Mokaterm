using Microsoft.AspNetCore.Components.Forms;

namespace Mokaterm.Maui.Services;

// A file from the native picker in the shape a Moka component takes from the browser's. It is read from disk, so its
// stream seeks where a browser file's would not.
internal sealed class PickedLocalFile(FileResult result) : IBrowserFile
{
	private const string UnknownType = "application/octet-stream";

	private readonly FileInfo _file = new(result.FullPath);

	public string Name => result.FileName;

	public DateTimeOffset LastModified => _file.LastWriteTimeUtc;

	public long Size => _file.Length;

	public string ContentType => string.IsNullOrEmpty(result.ContentType) ? UnknownType : result.ContentType;

	public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
	{
		long size = Size;
		if (size > maxAllowedSize)
		{
			throw new IOException($"{Name} is {size} bytes, more than the {maxAllowedSize} allowed.");
		}

		return new FileStream(_file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
	}
}
