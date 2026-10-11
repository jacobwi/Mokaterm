namespace Mokaterm.Abstractions.FileSystem;

/// <summary>The archives <see cref="IRemoteFileSystem.ExtractAsync"/> unpacks, told apart by how their names end.</summary>
public enum RemoteArchiveKind
{
	None,
	Tar,
	TarGzip,
	TarBzip2,
	TarXz,
	Zip,
}
