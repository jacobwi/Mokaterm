using System.Globalization;
using Devolutions.IronRdp;
using Action = Devolutions.IronRdp.Action;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// Cuts the byte stream from an RDP server into whole messages. IronRDP's own <c>Framed&lt;T&gt;</c> does the same
/// without a cancellation token, copies everything it holds on every read and takes any length a server announces; this
/// one follows a token, looks at a short head first and refuses a message bigger than <see cref="MaxMessageBytes"/>.
/// Only one reader may use it at a time.
/// </summary>
internal sealed class RdpFrameReader : IDisposable
{
	/// <summary>
	/// Far above anything RDP sends (a TPKT frame is at most 64 KiB, a CredSSP message a few). IronRDP's hints read no
	/// longer lengths today; this keeps a future or broken one from making the client hold whatever a server announces.
	/// </summary>
	public const int MaxMessageBytes = 16 * 1024 * 1024;

	// Every length IronRDP reads (TPKT, fast path, a DER sequence) sits in the first few bytes. Looking at a short head
	// keeps a buffer full of queued messages from being copied whole for every one of them.
	private const int HeadBytes = 64;

	private const int ReadChunk = 16 * 1024;

	private readonly Stream _stream;
	private byte[] _buffer = new byte[ReadChunk * 4];
	private int _start;
	private int _end;
	private IronRdpPdu? _pdu;

	public RdpFrameReader(Stream stream)
	{
		ArgumentNullException.ThrowIfNull(stream);
		_stream = stream;
	}

	/// <summary>Bytes read from the wire that no message has taken yet.</summary>
	public int Buffered => _end - _start;

	/// <summary>The next message whose length <paramref name="hint"/> knows how to find.</summary>
	/// <exception cref="EndOfStreamException">The server closed the connection first.</exception>
	public async ValueTask<byte[]> ReadByHintAsync(PduHint hint, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(hint);
		while (true)
		{
			int? size = FindSize(bytes =>
			{
				using OptionalUsize found = hint.FindSize(bytes);
				return found.IsSome() ? found.Get() : null;
			});

			if (size is int length)
			{
				return await ReadExactAsync(length, cancellationToken);
			}

			await FillAsync(cancellationToken);
		}
	}

	/// <summary>The next message of an active session, with what IronRDP should do with it.</summary>
	/// <exception cref="EndOfStreamException">The server closed the connection first.</exception>
	public async ValueTask<(Action Action, byte[] Payload)> ReadPduAsync(CancellationToken cancellationToken)
	{
		_pdu ??= IronRdpPdu.New();
		while (true)
		{
			Action? action = null;
			int? size = FindSize(bytes =>
			{
				using PduInfo? info = _pdu.FindSize(bytes);
				if (info is null)
				{
					return null;
				}

				action = info.GetAction();
				return info.GetLength();
			});

			if (size is int length && action is not null)
			{
				try
				{
					return (action, await ReadExactAsync(length, cancellationToken));
				}
				catch
				{
					action.Dispose();
					throw;
				}
			}

			action?.Dispose();
			await FillAsync(cancellationToken);
		}
	}

	public void Dispose()
	{
		_pdu?.Dispose();
		_pdu = null;
	}

	/// <summary>
	/// Asks <paramref name="find"/> for the length of the next message, first over the head of what is buffered and
	/// then over all of it, since a hint that cannot decide from the head may still decide from more.
	/// </summary>
	private int? FindSize(Func<byte[], nuint?> find)
	{
		int buffered = Buffered;
		if (buffered == 0)
		{
			return null;
		}

		nuint? found = find(_buffer.AsSpan(_start, Math.Min(buffered, HeadBytes)).ToArray());
		if (found is null && buffered > HeadBytes)
		{
			found = find(_buffer.AsSpan(_start, buffered).ToArray());
		}

		if (found is not nuint size)
		{
			return null;
		}

		if (size == 0 || size > MaxMessageBytes)
		{
			throw new InvalidDataException(string.Create(
				CultureInfo.InvariantCulture,
				$"it announced a message of {size} bytes, which is not something RDP sends"));
		}

		return (int)size;
	}

	private async ValueTask<byte[]> ReadExactAsync(int length, CancellationToken cancellationToken)
	{
		while (Buffered < length)
		{
			await FillAsync(cancellationToken, length);
		}

		byte[] message = _buffer.AsSpan(_start, length).ToArray();
		_start += length;
		if (_start == _end)
		{
			_start = 0;
			_end = 0;
		}

		return message;
	}

	/// <summary>Reads what the wire has, making room for at least <paramref name="wanted"/> bytes in the buffer.</summary>
	private async ValueTask FillAsync(CancellationToken cancellationToken, int wanted = 0)
	{
		int buffered = Buffered;
		int needed = Math.Max(wanted, buffered) + ReadChunk;
		if (_buffer.Length - _start < needed)
		{
			// Move what is left to the front, and grow when that is still not enough room.
			byte[] target = _buffer.Length < needed ? new byte[Math.Max(needed, _buffer.Length * 2)] : _buffer;
			Buffer.BlockCopy(_buffer, _start, target, 0, buffered);
			_buffer = target;
			_start = 0;
			_end = buffered;
		}

		int read = await _stream.ReadAsync(_buffer.AsMemory(_end), cancellationToken);
		if (read == 0)
		{
			throw new EndOfStreamException("The server closed the connection.");
		}

		_end += read;
	}
}
