using System.Security.Cryptography;
using System.Text;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Core.Tests.Abstractions;

public sealed class FileChecksumsTests
{
	private const string Sha256OfAbc = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

	[Fact]
	public async Task Compute_IsLowerCaseHex()
	{
		using MemoryStream source = new(Encoding.ASCII.GetBytes("abc"));

		Assert.Equal(Sha256OfAbc, await FileChecksums.ComputeAsync(source, ChecksumAlgorithm.Sha256, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Compute_Sha512_MatchesTheFramework()
	{
		byte[] content = RandomNumberGenerator.GetBytes(300_000);
		using MemoryStream source = new(content);

		string checksum = await FileChecksums.ComputeAsync(source, ChecksumAlgorithm.Sha512, TestContext.Current.CancellationToken);

		Assert.Equal(Convert.ToHexStringLower(SHA512.HashData(content)), checksum);
		Assert.Equal(FileChecksums.HexLength(ChecksumAlgorithm.Sha512), checksum.Length);
	}

	[Theory]
	[InlineData(Sha256OfAbc)]
	[InlineData(Sha256OfAbc + "  /srv/abc.txt")]
	[InlineData("  " + Sha256OfAbc + "\n")]
	[InlineData("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")]
	public void Parse_TakesTheHashFromTheStartOfAReply(string reply) =>
		Assert.Equal(Sha256OfAbc, FileChecksums.Parse(ChecksumAlgorithm.Sha256, reply));

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("ba7816bf8f01cfea")]
	[InlineData("zz7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
	[InlineData("550 Could not get the checksum.")]
	public void Parse_WithoutAWholeHash_IsNull(string? reply) =>
		Assert.Null(FileChecksums.Parse(ChecksumAlgorithm.Sha256, reply));

	// A SHA-512 starts with 64 hex digits too; reading it as a SHA-256 would compare half a hash.
	[Fact]
	public void Parse_ALongerHash_IsNotTakenForAShorterOne() =>
		Assert.Null(FileChecksums.Parse(ChecksumAlgorithm.Sha256, Convert.ToHexStringLower(SHA512.HashData([1, 2, 3]))));

	[Fact]
	public void Name_IsHowChecksumToolsPrintIt()
	{
		Assert.Equal("SHA-256", FileChecksums.Name(ChecksumAlgorithm.Sha256));
		Assert.Equal("SHA-512", FileChecksums.Name(ChecksumAlgorithm.Sha512));
	}
}
