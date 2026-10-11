namespace Mokaterm.Abstractions.FileSystem;

/// <summary>
/// The checksums a transfer can be verified with. Only strong ones: a server that offers nothing better than MD5 or CRC is
/// treated as offering nothing, because a weak match would read as a promise it cannot keep.
/// </summary>
public enum ChecksumAlgorithm
{
	Sha256,
	Sha512,
}
