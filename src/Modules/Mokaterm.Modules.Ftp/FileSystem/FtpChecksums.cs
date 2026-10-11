using FluentFTP;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ftp.FileSystem;

/// <summary>
/// Which checksum an FTP server can give, read from its FEAT reply. The HASH command (draft-bryan-ftpext-hash) lists its
/// algorithms; older servers offer XSHA256 or XSHA512 as commands of their own. MD5, SHA-1 and CRC are left out on purpose:
/// see <see cref="ChecksumAlgorithm"/>.
/// </summary>
internal static class FtpChecksums
{
	/// <summary>SHA-256 when the server offers it either way, then SHA-512, otherwise null.</summary>
	public static ChecksumAlgorithm? Choose(FtpHashAlgorithm hashAlgorithms, Func<FtpCapability, bool> hasFeature)
	{
		ArgumentNullException.ThrowIfNull(hasFeature);
		if (hashAlgorithms.HasFlag(FtpHashAlgorithm.SHA256) || hasFeature(FtpCapability.XSHA256))
		{
			return ChecksumAlgorithm.Sha256;
		}

		return hashAlgorithms.HasFlag(FtpHashAlgorithm.SHA512) || hasFeature(FtpCapability.XSHA512)
			? ChecksumAlgorithm.Sha512
			: null;
	}

	public static FtpHashAlgorithm ToFluent(ChecksumAlgorithm algorithm) => algorithm switch
	{
		ChecksumAlgorithm.Sha256 => FtpHashAlgorithm.SHA256,
		ChecksumAlgorithm.Sha512 => FtpHashAlgorithm.SHA512,
		_ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null),
	};
}
