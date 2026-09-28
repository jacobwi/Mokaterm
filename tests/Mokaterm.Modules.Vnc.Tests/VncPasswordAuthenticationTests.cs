using System.Text;
using Mokaterm.Modules.Vnc.Protocol;

namespace Mokaterm.Modules.Vnc.Tests;

public sealed class VncPasswordAuthenticationTests
{
	// The published DES vector (key 133457799BBCDFF1, plaintext 0123456789ABCDEF, cipher 85E813540F0AB405). VNC keys
	// DES with the password bytes in reverse bit order, so the password below is that key reversed bit by bit.
	private static readonly byte[] VectorPassword = [0xC8, 0x2C, 0xEA, 0x9E, 0xD9, 0x3D, 0xFB, 0x8F];

	private static readonly byte[] VectorBlock = [0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF];

	private static readonly byte[] VectorCipher = [0x85, 0xE8, 0x13, 0x54, 0x0F, 0x0A, 0xB4, 0x05];

	[Fact]
	public void CreateResponse_MatchesThePublishedDesVector()
	{
		byte[] challenge = [.. VectorBlock, .. VectorBlock];

		byte[] response = VncPasswordAuthentication.CreateResponse(challenge, VectorPassword);

		Assert.Equal([.. VectorCipher, .. VectorCipher], response);
	}

	[Fact]
	public void DeriveKey_ReversesEveryByteAndPadsWithZeros()
	{
		Span<byte> key = stackalloc byte[VncPasswordAuthentication.KeyLength];

		VncPasswordAuthentication.DeriveKey([0b0000_0001, 0b1000_0000], key);

		Assert.Equal(0b1000_0000, key[0]);
		Assert.Equal(0b0000_0001, key[1]);
		Assert.True(key[2..].IndexOfAnyExcept((byte)0) < 0);
	}

	[Fact]
	public void CreateResponse_UsesOnlyTheFirstEightBytesOfThePassword()
	{
		byte[] challenge = new byte[VncPasswordAuthentication.ChallengeLength];

		byte[] eight = VncPasswordAuthentication.CreateResponse(challenge, Encoding.UTF8.GetBytes("password"));
		byte[] longer = VncPasswordAuthentication.CreateResponse(challenge, Encoding.UTF8.GetBytes("password-and-more"));

		Assert.Equal(eight, longer);
	}

	[Fact]
	public void CreateResponse_ShortChallenge_Throws() =>
		Assert.Throws<ArgumentException>(() => VncPasswordAuthentication.CreateResponse(new byte[8], Encoding.UTF8.GetBytes("secret")));
}
