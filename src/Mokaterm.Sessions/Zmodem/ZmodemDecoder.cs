namespace Mokaterm.Sessions.Zmodem;

/// <summary>One data subpacket the decoder took off the wire: its bytes, the frame-end byte that closed it, and the check it carried.</summary>
/// <param name="End">One of ZCRCE, ZCRCG, ZCRCQ or ZCRCW: whether more subpackets follow and whether an answer is expected.</param>
internal sealed record ZmodemDataPacket(byte[] Data, byte End, bool Crc32);

/// <summary>What one decoded event is.</summary>
internal enum ZmodemReceivedKind
{
	/// <summary>A header whose frame check verified.</summary>
	Header,

	/// <summary>A data subpacket whose frame check verified.</summary>
	Packet,

	/// <summary>A frame attempt that failed its frame check or its grammar. The bytes are handed back as noise.</summary>
	Malformed,

	/// <summary>Five consecutive CAN characters: the other side aborted the session.</summary>
	Canceled,

	/// <summary>A byte that is not part of any frame: terminal output while watching, or noise once a transfer runs.</summary>
	Noise,
}

/// <summary>
/// Turns the other side's bytes into headers, data subpackets and noise. Keeps a partial frame across feeds, so a header
/// split over two channel chunks still parses, and holds back bytes that could still begin a frame, which is what lets the
/// watcher decide between "ZMODEM entry frame" and "text that merely contains an asterisk" before anything reaches the
/// terminal. Every byte is accounted for once: either a verified frame consumed it, or it comes back as noise. Not
/// thread-safe: one feeder at a time.
/// </summary>
internal sealed class ZmodemDecoder
{
	/// <summary>Header types after which a data subpacket follows immediately.</summary>
	private static bool ExpectsData(byte type) => type
		is ZmodemCode.ZFILE or ZmodemCode.ZSINIT or ZmodemCode.ZDATA or ZmodemCode.ZCOMMAND or ZmodemCode.ZSTDERR;

	private enum State
	{
		/// <summary>Outside any frame; only ZPAD starts an attempt and only CAN runs are counted.</summary>
		Scan,

		/// <summary>Saw one or more ZPAD; ZDLE must follow.</summary>
		Padding,

		/// <summary>Saw ZPAD(s) and ZDLE; ZHEX, ZBIN or ZBIN32 decides the frame.</summary>
		Kind,

		/// <summary>Collecting the 14 hex digits of a hex header.</summary>
		HexDigits,

		/// <summary>Consuming the CR LF (or XON) that closes a hex header.</summary>
		HexTail,

		/// <summary>Collecting the ZDLE-escaped bytes of a binary header.</summary>
		BinaryHeader,

		/// <summary>Collecting the ZDLE-escaped bytes of a data subpacket.</summary>
		Data,

		/// <summary>Collecting the escaped frame-check bytes at a subpacket's end.</summary>
		DataCheck,
	}

	private readonly List<byte> _pending = [];
	private readonly List<byte> _data = [];
	private readonly List<ZmodemReceived> _events = [];
	private readonly byte[] _hexDigits = new byte[14];
	private State _state = State.Scan;
	private ZmodemHeaderEncoding _encoding;
	private bool _pendingEscape;
	private ZmodemHeader? _heldHeader;
	private int _bodyIndex;
	private int _checkIndex;
	private int _frameStart;
	private int _resumeAt;
	private int _hexDigitsSeen;
	private int _canceledCount;
	private byte _frameEnd;

	/// <summary>Everything the last <see cref="Feed"/> produced, in the order the bytes arrived.</summary>
	public IReadOnlyList<ZmodemReceived> Events => _events;

	/// <summary>A partial frame is still open; more bytes are needed before the watcher can rule on it.</summary>
	public bool IsMidFrame => _state != State.Scan;

	public void Feed(ReadOnlySpan<byte> bytes)
	{
		_events.Clear();
		_pending.AddRange(bytes);
		Process();
	}

	private void Process()
	{
		// A frame still open picks up where the last feed stopped; its earlier bytes stay queued for the noise
		// they become when the frame fails.
		int offset = _resumeAt;
		while (offset < _pending.Count)
		{
			byte value = _pending[offset];
			int consumed = 1;
			switch (_state)
			{
				case State.Scan:
					consumed = ScanByte(value, offset);
					break;
				case State.Padding:
					if (value == ZmodemCode.ZPAD)
					{
						break;
					}

					if (value == ZmodemCode.ZDLE)
					{
						_state = State.Kind;
						break;
					}

					FailFrame(offset);
					consumed = 1;
					break;
				case State.Kind:
					if (value is ZmodemCode.ZHEX or ZmodemCode.ZBIN or ZmodemCode.ZBIN32)
					{
						_encoding = value switch
						{
							ZmodemCode.ZBIN => ZmodemHeaderEncoding.Binary16,
							ZmodemCode.ZBIN32 => ZmodemHeaderEncoding.Binary32,
							_ => ZmodemHeaderEncoding.Hex,
						};
						_bodyIndex = 0;
						_hexDigitsSeen = 0;
						_heldHeader = null;
						_pendingEscape = false;
						_data.Clear();
						_state = value == ZmodemCode.ZHEX ? State.HexDigits : State.BinaryHeader;
						break;
					}

					FailFrame(offset);
					consumed = 1;
					break;
				case State.HexDigits:
					if (IsSkippedNoise(value))
					{
						break;
					}

					if (HexValue(value) < 0)
					{
						FailFrame(offset);
					consumed = 1;
						break;
					}

					_hexDigits[_hexDigitsSeen++] = value;
					if (_hexDigitsSeen >= _hexDigits.Length)
					{
						// However the frame check turns out, the tail belongs to the frame and is consumed before anything
						// of the header is reported.
						FinishHexHeader();
						_state = State.HexTail;
					}

					break;
				case State.HexTail:
					if (IsSkippedNoise(value))
					{
						break;
					}

					// The protocol sends CR LF; a lone CR, LF or XON closes too, and the byte after a CR is eaten when it is the LF.
					if (value is ((byte)'\r' or 0x8D))
					{
						if (offset + 1 >= _pending.Count)
						{
							// The CR is the last byte here: stay put and see whether the next feed brings the LF.
							break;
						}

						if (_pending[offset + 1] is ((byte)'\n' or 0x8A))
						{
							consumed = 2;
						}
					}
					else if (value is not ((byte)'\n' or 0x8A))
					{
						// Something else follows already; leave it for the scanner.
						consumed = 0;
					}

					_state = State.Scan;
					if (_heldHeader is { } held)
					{
						_heldHeader = null;
						DeliverHeader(held);
					}
					else
					{
						FailFrame(offset + consumed - 1);
					}

					break;
				case State.BinaryHeader:
				{
					if (IsSkippedNoise(value) && !_pendingEscape)
					{
						break;
					}

					if (ReadEscaped(value, out bool decoded))
					{
						if (decoded)
						{
							_bodyIndex++;
							if (_bodyIndex >= (_encoding == ZmodemHeaderEncoding.Binary32 ? 9 : 7))
							{
								FinishBinaryHeader();
								if (_state == State.BinaryHeader)
								{
									_state = State.Scan;
								}
							}
						}
					}
					else
					{
						FailFrame(offset);
					consumed = 1;
					}

					break;
				}
				case State.Data:
				{
					if (IsSkippedNoise(value) && !_pendingEscape)
					{
						break;
					}

					if (!_pendingEscape && value == ZmodemCode.ZDLE)
					{
						_pendingEscape = true;
						break;
					}

					if (_pendingEscape && value is ZmodemCode.ZCRCE or ZmodemCode.ZCRCG or ZmodemCode.ZCRCQ or ZmodemCode.ZCRCW)
					{
						_pendingEscape = false;
						_frameEnd = value;
						_checkIndex = 0;
						_state = State.DataCheck;
						break;
					}

					if (ReadEscaped(value, out bool decoded))
					{
						// ReadEscaped stored the decoded byte; a false result only marks a pending ZDLE.
						_ = decoded;
					}
					else
					{
						// A broken escape inside a subpacket: the frame check would not hold, so drop the subpacket.
						_events.Add(ZmodemReceived.Malformed());
						_data.Clear();
						_pendingEscape = false;
						_state = State.Scan;
					}

					break;
				}
				case State.DataCheck:
				{
					if (IsSkippedNoise(value) && !_pendingEscape)
					{
						break;
					}

					if (ReadEscaped(value, out bool decoded))
					{
						// ReadEscaped stored the decoded byte; count it towards the frame check.
						if (decoded)
						{
							_checkIndex++;
							if (_checkIndex >= (_encoding == ZmodemHeaderEncoding.Binary32 ? 4 : 2))
							{
								FinishDataPacket();
								if (_state == State.DataCheck)
								{
									_state = State.Scan;
								}
							}
						}
					}
					else
					{
						_events.Add(ZmodemReceived.Malformed());
						_data.Clear();
						_pendingEscape = false;
						_state = State.Scan;
					}

					break;
				}
			}

			// A failed frame rewinds: consumed is negative and the scanner re-reads from inside the frame.
			offset += consumed;
		}

		if (_state == State.Scan)
		{
			if (offset > 0)
			{
				_pending.RemoveRange(0, Math.Min(offset, _pending.Count));
			}

			_frameStart = 0;
			_resumeAt = 0;
		}
		else
		{
			// An open frame keeps its bytes queued for the noise a failure hands back, and resumes where it stopped.
			int remove = Math.Min(_frameStart, _pending.Count);
			if (remove > 0)
			{
				_pending.RemoveRange(0, remove);
				_frameStart -= remove;
			}

			_resumeAt = offset - remove;
		}
	}

	/// <summary>Returns how many bytes were consumed: 1 normally, 0 to re-read this byte in the new state.</summary>
	private int ScanByte(byte value, int offset)
	{
		if (IsSkippedNoise(value))
		{
			// Flow-control bytes the protocol says a receiver ignores; a terminal ignores them as well.
			_canceledCount = 0;
			return 1;
		}

		if (value == ZmodemCode.ZDLE)
		{
			_canceledCount++;
			if (_canceledCount >= 5)
			{
				_events.Add(ZmodemReceived.Canceled());
				_canceledCount = 0;
			}

			return 1;
		}

		_canceledCount = 0;
		if (value == ZmodemCode.ZPAD)
		{
			_frameStart = offset;
			_state = State.Padding;
			return 1;
		}

		_events.Add(ZmodemReceived.Noise(value));
		return 1;
	}

	/// <summary>
	/// Gives up on the frame being read: every byte of it, through <paramref name="lastOffset"/>, comes back as noise and
	/// scanning continues after them. A frame that began inside the failed one is not looked for — senders do not nest
	/// frames, and re-scanning a failed frame across feeds costs more than it can ever find.
	/// </summary>
	private void FailFrame(int lastOffset)
	{
		_events.Add(ZmodemReceived.Malformed());
		int end = Math.Min(lastOffset, _pending.Count - 1);
		for (int index = Math.Max(_frameStart, 0); index <= end; index++)
		{
			_events.Add(ZmodemReceived.Noise(_pending[index]));
		}

		_state = State.Scan;
		_canceledCount = 0;
		_pendingEscape = false;
	}

	private void FinishHexHeader()
	{
		byte[] body = new byte[7];
		for (int index = 0; index < body.Length; index++)
		{
			body[index] = (byte)((HexValue(_hexDigits[index * 2]) << 4) | HexValue(_hexDigits[index * 2 + 1]));
		}

		// A frame holds when folding its check bytes in leaves nothing, the way lrzsz's updcrc-based receivers test it.
		if (ZmodemCrc.Running16(body) != 0)
		{
			return;
		}

		_heldHeader = new ZmodemHeader(body[0], body[1], body[2], body[3], body[4], ZmodemHeaderEncoding.Hex);
	}

	private void FinishBinaryHeader()
	{
		if (_data.Count < 5)
		{
			return;
		}

		bool valid;
		if (_encoding == ZmodemHeaderEncoding.Binary32)
		{
			// Folding the four check bytes in must land on the protocol's constant.
			uint crc = 0xFFFFFFFF;
			foreach (byte value in _data)
			{
				crc = ZmodemCrc.Append32(crc, value);
			}

			valid = crc == ZmodemCrc.Verified32;
		}
		else
		{
			// The last two of the seven escaped bytes are the check; folding them in must leave nothing.
			ushort crc = 0;
			for (int index = 0; index < 7; index++)
			{
				crc = ZmodemCrc.Append16(crc, _data[index]);
			}

			valid = crc == 0;
		}

		if (!valid)
		{
			return;
		}

		DeliverHeader(new ZmodemHeader(_data[0], _data[1], _data[2], _data[3], _data[4], _encoding));
	}

	private void DeliverHeader(ZmodemHeader header)
	{
		_events.Add(ZmodemReceived.Of(header));
		if (ExpectsData(header.Type))
		{
			_data.Clear();
			_pendingEscape = false;
			_state = State.Data;
		}
		else
		{
			_state = State.Scan;
		}
	}

	private void FinishDataPacket()
	{
		int checkLength = _encoding == ZmodemHeaderEncoding.Binary32 ? 4 : 2;
		if (_data.Count < checkLength)
		{
			_events.Add(ZmodemReceived.Malformed());
			_data.Clear();
			_state = State.Scan;
			return;
		}

		int checkOffset = _data.Count - checkLength;
		bool crc32 = _encoding == ZmodemHeaderEncoding.Binary32;
		bool valid;
		if (crc32)
		{
			// Fold the data, the frame-end byte and the four check bytes in; what is left must be the complement check.
			uint crc = 0xFFFFFFFF;
			for (int index = 0; index < checkOffset; index++)
			{
				crc = ZmodemCrc.Append32(crc, _data[index]);
			}

			crc = ZmodemCrc.Append32(crc, _frameEnd);
			uint check = (uint)(_data[checkOffset] | (_data[checkOffset + 1] << 8) | (_data[checkOffset + 2] << 16) | (_data[checkOffset + 3] << 24));
			valid = ~crc == check;
		}
		else
		{
			// Fold the data, the frame-end byte and the two check bytes in; what is left must be nothing.
			ushort crc = 0;
			for (int index = 0; index < checkOffset; index++)
			{
				crc = ZmodemCrc.Append16(crc, _data[index]);
			}

			crc = ZmodemCrc.Append16(crc, _frameEnd);
			crc = ZmodemCrc.Append16(crc, _data[checkOffset]);
			crc = ZmodemCrc.Append16(crc, _data[checkOffset + 1]);
			valid = crc == 0;
		}

		if (!valid)
		{
			_events.Add(ZmodemReceived.Malformed());
			_data.Clear();
			_state = State.Scan;
			return;
		}

		byte[] data = _data.GetRange(0, checkOffset).ToArray();
		_events.Add(ZmodemReceived.Of(new ZmodemDataPacket(data, _frameEnd, crc32)));

		// ZCRCE and ZCRCW close the subpacket run: a header follows. ZCRCG and ZCRCQ promise another subpacket at once.
		_data.Clear();
		_state = _frameEnd is (ZmodemCode.ZCRCE or ZmodemCode.ZCRCW) ? State.Scan : State.Data;
	}

	/// <summary>
	/// Reads one byte of an escaped run. The pair (<see cref="ZmodemReceivedKind.Malformed"/>-free form) reports false
	/// while a ZDLE is pending; <paramref name="decoded"/> is false for that byte and for a broken escape, which the
	/// caller treats as a failed frame.
	/// </summary>
	private bool ReadEscaped(byte value, out bool decoded)
	{
		if (!_pendingEscape)
		{
			if (value == ZmodemCode.ZDLE)
			{
				_pendingEscape = true;
				decoded = false;
				return true;
			}

			_data.Add(value);
			decoded = true;
			return true;
		}

		_pendingEscape = false;
		decoded = true;
		if ((value & 0x60) == 0x40)
		{
			_data.Add((byte)(value ^ 0x40));
			return true;
		}

		if (value == ZmodemCode.ZRUB0)
		{
			_data.Add(0x7F);
			return true;
		}

		if (value == ZmodemCode.ZRUB1)
		{
			_data.Add(0xFF);
			return true;
		}

		decoded = false;
		return false;
	}

	private static bool IsSkippedNoise(byte value) => value is (ZmodemCode.XON or ZmodemCode.XOFF or 0x91 or 0x93);

	/// <summary>The value of a hex digit, with parity masked off; -1 when the byte is not one.</summary>
	private static int HexValue(byte value)
	{
		byte plain = (byte)(value & 0x7F);
		return plain switch
		{
			>= (byte)'0' and <= (byte)'9' => plain - (byte)'0',
			>= (byte)'a' and <= (byte)'f' => plain - (byte)'a' + 10,
			>= (byte)'A' and <= (byte)'F' => plain - (byte)'A' + 10,
			_ => -1,
		};
	}
}

/// <summary>One decoded event. Only the field matching <see cref="ZmodemReceivedKind"/> is set.</summary>
internal readonly record struct ZmodemReceived(ZmodemReceivedKind Kind, ZmodemHeader Header, ZmodemDataPacket? Packet, byte NoiseByte)
{
	public static ZmodemReceived Of(ZmodemHeader header) => new(ZmodemReceivedKind.Header, header, null, 0);

	public static ZmodemReceived Of(ZmodemDataPacket packet) => new(ZmodemReceivedKind.Packet, default, packet, 0);

	public static ZmodemReceived Malformed() => new(ZmodemReceivedKind.Malformed, default, null, 0);

	public static ZmodemReceived Canceled() => new(ZmodemReceivedKind.Canceled, default, null, 0);

	public static ZmodemReceived Noise(byte value) => new(ZmodemReceivedKind.Noise, default, null, value);
}
