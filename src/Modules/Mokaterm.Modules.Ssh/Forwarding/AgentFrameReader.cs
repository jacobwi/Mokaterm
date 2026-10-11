using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Mokaterm.Modules.Ssh.Forwarding;

/// <summary>
/// Cuts agent protocol messages (a 32-bit big-endian length, then that many bytes) out of what a forwarded channel
/// delivers, however the server split them. A length of 0 or over <see cref="MaxMessageBytes"/> is not the protocol,
/// so the reader stops there for good. What it holds never exceeds one message plus the last piece appended.
/// </summary>
internal sealed class AgentFrameReader
{
	/// <summary>OpenSSH's own agent refuses anything longer (<c>AGENT_MAX_LEN</c>).</summary>
	public const int MaxMessageBytes = 256 * 1024;

	private const int HeaderBytes = sizeof(uint);

	private byte[] _buffer = new byte[1024];
	private int _start;
	private int _count;

	/// <summary>A message announced a length the protocol does not allow; nothing after it can be read.</summary>
	public bool IsBroken { get; private set; }

	/// <summary>Frames a message for the wire.</summary>
	public static byte[] Frame(ReadOnlySpan<byte> message)
	{
		byte[] frame = new byte[HeaderBytes + message.Length];
		BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)message.Length);
		message.CopyTo(frame.AsSpan(HeaderBytes));
		return frame;
	}

	/// <summary>Adds what arrived. False once the reader is broken.</summary>
	public bool Append(ReadOnlySpan<byte> data)
	{
		if (IsBroken)
		{
			return false;
		}

		if (_buffer.Length - _start - _count < data.Length)
		{
			byte[] grown = _buffer.Length - _count >= data.Length ? _buffer : new byte[Math.Max(_buffer.Length * 2, _count + data.Length)];
			_buffer.AsSpan(_start, _count).CopyTo(grown);
			if (ReferenceEquals(grown, _buffer))
			{
				CryptographicOperations.ZeroMemory(_buffer.AsSpan(_count));
			}
			else
			{
				CryptographicOperations.ZeroMemory(_buffer);
			}

			_buffer = grown;
			_start = 0;
		}

		data.CopyTo(_buffer.AsSpan(_start + _count));
		_count += data.Length;
		return CheckHeader();
	}

	/// <summary>The next whole message, without its length. False while it has not all arrived.</summary>
	public bool TryTake([NotNullWhen(true)] out byte[]? message)
	{
		message = null;
		if (IsBroken || _count < HeaderBytes)
		{
			return false;
		}

		int length = (int)BinaryPrimitives.ReadUInt32BigEndian(_buffer.AsSpan(_start, HeaderBytes));
		if (_count - HeaderBytes < length)
		{
			return false;
		}

		message = _buffer.AsSpan(_start + HeaderBytes, length).ToArray();
		CryptographicOperations.ZeroMemory(_buffer.AsSpan(_start, HeaderBytes + length));
		_start += HeaderBytes + length;
		_count -= HeaderBytes + length;
		if (_count == 0)
		{
			_start = 0;
		}

		_ = CheckHeader();
		return true;
	}

	/// <summary>Wipes what is held: a forwarded agent carries keys that are being added as well as data to sign.</summary>
	public void Clear()
	{
		CryptographicOperations.ZeroMemory(_buffer);
		_start = 0;
		_count = 0;
	}

	private bool CheckHeader()
	{
		if (_count >= HeaderBytes)
		{
			uint length = BinaryPrimitives.ReadUInt32BigEndian(_buffer.AsSpan(_start, HeaderBytes));
			if (length is 0 or > MaxMessageBytes)
			{
				IsBroken = true;
				Clear();
				return false;
			}
		}

		return true;
	}
}
