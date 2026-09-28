using System.Security.Cryptography;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Core.Tests.TestSupport;

/// <summary>XORs with a random per-instance pad, standing in for DPAPI. Can be told to fail or to corrupt the key.</summary>
public sealed class FakeDeviceKeyProtector : IDeviceKeyProtector
{
	private readonly byte[] _pad = RandomNumberGenerator.GetBytes(64);

	public string Description => "Test device";

	public bool FailUnprotect { get; set; }

	public bool CorruptUnprotect { get; set; }

	public int ProtectCalls { get; private set; }

	public ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
	{
		ProtectCalls++;
		return ValueTask.FromResult(Xor(data.Span));
	}

	public ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedData, CancellationToken cancellationToken = default)
	{
		if (FailUnprotect)
		{
			throw new CryptographicException("The data was protected for a different user.");
		}

		byte[] data = Xor(protectedData.Span);
		if (CorruptUnprotect)
		{
			data[0] ^= 0xFF;
		}

		return ValueTask.FromResult(data);
	}

	private byte[] Xor(ReadOnlySpan<byte> input)
	{
		byte[] output = new byte[input.Length];
		for (int i = 0; i < input.Length; i++)
		{
			output[i] = (byte)(input[i] ^ _pad[i % _pad.Length]);
		}

		return output;
	}
}
