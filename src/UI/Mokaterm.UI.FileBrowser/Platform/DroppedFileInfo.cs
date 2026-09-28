using System.Text.Json.Serialization;

namespace Mokaterm.UI.FileBrowser.Platform;

/// <summary>What <c>filedrop.js</c> reports about one dropped file or folder. The File object itself stays in the page.</summary>
/// <param name="FileIndex">Position of the File in the page's registry for this drop; -1 for folders.</param>
/// <param name="LastModified">Milliseconds since the Unix epoch; 0 for folders.</param>
internal sealed record DroppedFileInfo(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("relativePath")] string RelativePath,
	[property: JsonPropertyName("isDirectory")] bool IsDirectory,
	[property: JsonPropertyName("size")] long Size,
	[property: JsonPropertyName("lastModified")] long LastModified,
	[property: JsonPropertyName("fileIndex")] int FileIndex);
