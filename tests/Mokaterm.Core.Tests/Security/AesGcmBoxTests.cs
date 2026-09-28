using System.Security.Cryptography;
using System.Text;
using Mokaterm.Core.Security;

namespace Mokaterm.Core.Tests.Security;

public sealed class AesGcmBoxTests
{
	private static readonly byte[] Key = RandomNumberGenerator.GetBytes(AesGcmBox.KeySize);
	private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("mokaterm:test:v1");
	private static readonly byte[] Plaintext = Encoding.UTF8.GetBytes("root@10.10.2.3 hunter2");

	[Fact]
	public void Seal_ThenOpen_ReturnsPlaintext()
	{
		byte[] sealedBox = Seal(Plaintext);

		byte[] opened = Open(Key, sealedBox, AssociatedData);

		Assert.Equal(Plaintext, opened);
		Assert.Equal(Plaintext.Length + AesGcmBox.Overhead, sealedBox.Length);
	}

	[Fact]
	public void Seal_SamePlaintextTwice_UsesDifferentNonces()
	{
		byte[] first = Seal(Plaintext);
		byte[] second = Seal(Plaintext);

		Assert.NotEqual(first.AsSpan(0, AesGcmBox.NonceSize).ToArray(), second.AsSpan(0, AesGcmBox.NonceSize).ToArray());
		Assert.NotEqual(first, second);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(AesGcmBox.NonceSize)]
	[InlineData(AesGcmBox.Overhead)]
	[InlineData(AesGcmBox.Overhead + 5)]
	public void Open_AnyTamperedByte_FailsAuthentication(int index)
	{
		byte[] sealedBox = Seal(Plaintext);
		sealedBox[index] ^= 0x01;

		Assert.Throws<AuthenticationTagMismatchException>(() => Open(Key, sealedBox, AssociatedData));
	}

	[Fact]
	public void Open_DifferentAssociatedData_FailsAuthentication()
	{
		byte[] sealedBox = Seal(Plaintext);

		Assert.Throws<AuthenticationTagMismatchException>(() => Open(Key, sealedBox, Encoding.UTF8.GetBytes("mokaterm:other:v1")));
	}

	[Fact]
	public void Open_WrongKey_FailsAuthentication()
	{
		byte[] sealedBox = Seal(Plaintext);

		Assert.Throws<AuthenticationTagMismatchException>(() => Open(RandomNumberGenerator.GetBytes(AesGcmBox.KeySize), sealedBox, AssociatedData));
	}

	[Fact]
	public void Open_TooShortForNonceAndTag_ThrowsInvalidData()
	{
		byte[] truncated = new byte[AesGcmBox.Overhead - 1];

		Assert.Throws<InvalidDataException>(() => AesGcmBox.GetPlaintextLength(truncated.Length));
	}

	[Fact]
	public void Seal_EmptyPlaintext_RoundTrips()
	{
		byte[] sealedBox = Seal([]);

		Assert.Empty(Open(Key, sealedBox, AssociatedData));
	}

	private static byte[] Seal(byte[] plaintext)
	{
		byte[] sealedBox = new byte[AesGcmBox.GetSealedLength(plaintext.Length)];
		AesGcmBox.Seal(Key, plaintext, AssociatedData, sealedBox);
		return sealedBox;
	}

	private static byte[] Open(byte[] key, byte[] sealedBox, byte[] associatedData)
	{
		byte[] plaintext = new byte[AesGcmBox.GetPlaintextLength(sealedBox.Length)];
		AesGcmBox.Open(key, sealedBox, associatedData, plaintext);
		return plaintext;
	}
}
