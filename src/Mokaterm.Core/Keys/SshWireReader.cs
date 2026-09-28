using System.Buffers.Binary;

namespace Mokaterm.Core.Keys;

/// <summary>Reads the length prefixed fields of an SSH blob, refusing anything that runs past the end.</summary>
internal ref struct SshWireReader
{
	private readonly ReadOnlySpan<byte> _data;
	private int _position;

	public SshWireReader(ReadOnlySpan<byte> data)
	{
		_data = data;
		_position = 0;
	}

	public readonly int Remaining => _data.Length - _position;

	public bool TryReadUInt32(out uint value)
	{
		if (Remaining < 4)
		{
			value = 0;
			return false;
		}

		value = BinaryPrimitives.ReadUInt32BigEndian(_data[_position..]);
		_position += 4;
		return true;
	}

	public bool TryReadString(out ReadOnlySpan<byte> value)
	{
		int start = _position;
		if (!TryReadUInt32(out uint length) || length > (uint)Remaining)
		{
			_position = start;
			value = default;
			return false;
		}

		value = _data.Slice(_position, (int)length);
		_position += (int)length;
		return true;
	}

	public bool TrySkip(int count)
	{
		if (Remaining < count)
		{
			return false;
		}

		_position += count;
		return true;
	}
}
