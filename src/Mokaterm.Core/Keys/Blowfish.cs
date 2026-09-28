namespace Mokaterm.Core.Keys;

/// <summary>
/// Blowfish with the salted key schedule bcrypt needs (EksBlowfish). It exists only for
/// <see cref="BcryptPbkdf"/>, which is the one KDF OpenSSH private key files accept; nothing else in Mokaterm
/// encrypts with Blowfish.
/// </summary>
internal sealed class Blowfish
{
	private const int Rounds = 16;

	private readonly uint[] _p = new uint[18];
	private readonly uint[] _s = new uint[4 * 256];

	public Blowfish()
	{
		BlowfishTables.P.CopyTo(_p);
		BlowfishTables.S.CopyTo(_s);
	}

	/// <summary>Encrypts one 64 bit block in place.</summary>
	public void Encipher(ref uint left, ref uint right)
	{
		uint l = left;
		uint r = right;
		for (int round = 0; round < Rounds; round++)
		{
			l ^= _p[round];
			r ^= Feistel(l);
			(l, r) = (r, l);
		}

		// The last round's swap is not part of the cipher.
		(l, r) = (r, l);
		left = l ^ _p[Rounds + 1];
		right = r ^ _p[Rounds];
	}

	/// <summary>
	/// Mixes <paramref name="key"/> into the state, folding <paramref name="data"/> into every block as it goes.
	/// An empty <paramref name="data"/> is OpenBSD's expand0state, which bcrypt runs 128 times per hash.
	/// </summary>
	public void Expand(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
	{
		int keyPosition = 0;
		for (int i = 0; i < _p.Length; i++)
		{
			_p[i] ^= NextWord(key, ref keyPosition);
		}

		uint left = 0;
		uint right = 0;
		int dataPosition = 0;
		for (int i = 0; i < _p.Length; i += 2)
		{
			if (!data.IsEmpty)
			{
				left ^= NextWord(data, ref dataPosition);
				right ^= NextWord(data, ref dataPosition);
			}

			Encipher(ref left, ref right);
			_p[i] = left;
			_p[i + 1] = right;
		}

		for (int i = 0; i < _s.Length; i += 2)
		{
			if (!data.IsEmpty)
			{
				left ^= NextWord(data, ref dataPosition);
				right ^= NextWord(data, ref dataPosition);
			}

			Encipher(ref left, ref right);
			_s[i] = left;
			_s[i + 1] = right;
		}
	}

	/// <summary>Reads four bytes big endian, wrapping back to the start when it runs off the end.</summary>
	private static uint NextWord(ReadOnlySpan<byte> source, ref int position)
	{
		uint word = 0;
		for (int i = 0; i < 4; i++)
		{
			word = (word << 8) | source[position];
			position = (position + 1) % source.Length;
		}

		return word;
	}

	private uint Feistel(uint x)
	{
		uint a = _s[x >> 24];
		uint b = _s[256 + ((x >> 16) & 0xFF)];
		uint c = _s[512 + ((x >> 8) & 0xFF)];
		uint d = _s[768 + (x & 0xFF)];
		return unchecked(((a + b) ^ c) + d);
	}
}
