using FluentFTP;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ftp.FileSystem;

namespace Mokaterm.Modules.Ftp.Tests;

public sealed class FtpChecksumsTests
{
	[Theory]
	[InlineData(FtpHashAlgorithm.SHA256, null, ChecksumAlgorithm.Sha256)]
	[InlineData(FtpHashAlgorithm.SHA512 | FtpHashAlgorithm.SHA256, null, ChecksumAlgorithm.Sha256)]
	[InlineData(FtpHashAlgorithm.SHA512, null, ChecksumAlgorithm.Sha512)]
	[InlineData(FtpHashAlgorithm.NONE, FtpCapability.XSHA256, ChecksumAlgorithm.Sha256)]
	[InlineData(FtpHashAlgorithm.NONE, FtpCapability.XSHA512, ChecksumAlgorithm.Sha512)]
	public void Choose_PrefersSha256OverSha512(FtpHashAlgorithm hashAlgorithms, FtpCapability? feature, ChecksumAlgorithm expected) =>
		Assert.Equal(expected, FtpChecksums.Choose(hashAlgorithms, capability => capability == feature));

	// A match in a weak hash would read as proof it cannot give.
	[Theory]
	[InlineData(FtpHashAlgorithm.MD5, FtpCapability.XMD5)]
	[InlineData(FtpHashAlgorithm.SHA1, FtpCapability.XSHA1)]
	[InlineData(FtpHashAlgorithm.CRC, FtpCapability.XCRC)]
	[InlineData(FtpHashAlgorithm.NONE, FtpCapability.MFMT)]
	public void Choose_WithOnlyWeakOrNoHashes_OffersNothing(FtpHashAlgorithm hashAlgorithms, FtpCapability feature) =>
		Assert.Null(FtpChecksums.Choose(hashAlgorithms, capability => capability == feature));
}
