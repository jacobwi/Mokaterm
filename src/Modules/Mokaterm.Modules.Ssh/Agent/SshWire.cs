using System.Buffers.Binary;
using System.Text;

namespace Mokaterm.Modules.Ssh.Agent;

/// <summary>Reads the SSH wire types the agent protocol is built from: bytes, 32-bit lengths and length-prefixed strings.</summary>
internal sealed class SshWireReader
{
	private readonly ReadOnlyMemory<byte> _data;
	private int _position;

	public SshWireReader(ReadOnlyMemory<byte> data) => _data = data;

	public int Remaining => _data.Length - _position;

	public bool TryReadByte(out byte value)
	{
		if (Remaining < 1)
		{
			value = 0;
			return false;
		}

		value = _data.Span[_position];
		_position++;
		return true;
	}

	public bool TryReadUInt32(out uint value)
	{
		if (Remaining < sizeof(uint))
		{
			value = 0;
			return false;
		}

		value = BinaryPrimitives.ReadUInt32BigEndian(_data.Span.Slice(_position, sizeof(uint)));
		_position += sizeof(uint);
		return true;
	}

	/// <summary>An SSH string: a 32-bit length then that many bytes. The bytes are copied out.</summary>
	public bool TryReadString(out byte[] value)
	{
		value = [];
		if (!TryReadUInt32(out uint length) || length > (uint)Remaining)
		{
			return false;
		}

		value = _data.Slice(_position, (int)length).ToArray();
		_position += (int)length;
		return true;
	}

	public bool TryReadText(out string value)
	{
		if (!TryReadString(out byte[] bytes))
		{
			value = "";
			return false;
		}

		value = Encoding.UTF8.GetString(bytes);
		return true;
	}
}

/// <summary>Builds an agent request body. The 32-bit frame length is added when it is sent.</summary>
internal sealed class SshWireWriter
{
	private readonly List<byte> _bytes = [];

	public int Length => _bytes.Count;

	public SshWireWriter WriteByte(byte value)
	{
		_bytes.Add(value);
		return this;
	}

	public SshWireWriter WriteUInt32(uint value)
	{
		Span<byte> buffer = stackalloc byte[sizeof(uint)];
		BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
		_bytes.AddRange(buffer);
		return this;
	}

	public SshWireWriter WriteString(ReadOnlySpan<byte> value)
	{
		WriteUInt32((uint)value.Length);
		_bytes.AddRange(value);
		return this;
	}

	public byte[] ToArray() => [.. _bytes];
}
