namespace Mokaterm.Sessions.Zmodem;

/// <summary>Where the session's own state machine stands.</summary>
internal enum ZmodemSessionState
{
	/// <summary>Watching the terminal output for a ZMODEM entry frame; nothing is intercepted yet.</summary>
	Watching,

	/// <summary>An entry frame arrived; the other side has not yet shown whether it sends or receives.</summary>
	Negotiating,

	/// <summary>The other side offers a file; the user has to accept or skip it.</summary>
	Offering,

	/// <summary>The offered file is coming in.</summary>
	Receiving,

	/// <summary>The other side waits to receive; the user has to pick files.</summary>
	ChoosingFiles,

	/// <summary>Sending files to the other side.</summary>
	Sending,

	/// <summary>The session ended; only the peer's closing "OO" may still be swallowed.</summary>
	Done,
}

/// <summary>
/// One ZMODEM transfer as a pure state machine: it decodes the channel's bytes, decides what to answer, and reports file
/// offers, file data and progress — with no clock, no IO and no thread of its own. The caller feeds it from the terminal
/// pump and carries out its steps: writing <see cref="ZmodemStep.Send"/> to the channel, <see cref="ZmodemStep.FileData"/>
/// to the local file, and supplying the sender with bytes when it asks. Roles settle on the entry frame: a ZRINIT means
/// the peer waits to receive (we send, which is what running <c>rz</c> there looks like); a ZRQINIT or ZFILE means the
/// peer sends (which is <c>sz</c>).
/// </summary>
internal sealed class ZmodemSession
{
	/// <summary>The largest data subpacket this side sends; the protocol's own maximum.</summary>
	private const int SubpacketBytes = 1024;

	/// <summary>How much file data the sender asks for at once: a few subpackets to fill the pipe without a big buffer.</summary>
	private const int DataChunkBytes = 4 * SubpacketBytes;

	private enum Role
	{
		Unknown,
		Receiver,
		Sender,
	}

	private readonly ZmodemDecoder _decoder = new();
	private readonly ZmodemEncoder _encoder = new();

	private Role _role;
	private ZmodemSessionState _state = ZmodemSessionState.Watching;
	private readonly Queue<ZmodemFileInfo> _outbound = new();
	private ZmodemFileInfo? _inbound;
	private ZmodemFileInfo? _offer;
	private ZmodemHeader _lastHeader;
	private bool _sentAnyHeader;
	private bool _expectFilePacket;
	private bool _expectSinitPacket;
	private bool _rejectData;
	private bool _peerWantsCrc32;
	private bool _zdataSent;
	private long _fileBytes;
	private long _sendPosition;

	/// <summary>How many bytes of the peer's closing "OO" are still swallowed; negative when none are expected.</summary>
	private int _overAndOut = -1;

	public ZmodemSessionState State => _state;

	/// <summary>The file offered while <see cref="State"/> is <see cref="ZmodemSessionState.Offering"/>.</summary>
	public ZmodemFileInfo? Offer => _offer;

	/// <summary>The file moving now; null between files.</summary>
	public string? FileName => _state switch
	{
		ZmodemSessionState.Receiving => _inbound?.Name,
		ZmodemSessionState.Sending or ZmodemSessionState.Negotiating when _role == Role.Sender && _outbound.TryPeek(out ZmodemFileInfo? sending) => sending.Name,
		_ => null,
	};

	/// <summary>Bytes of the current file that moved (received and written, or sent).</summary>
	public long FileBytes => _fileBytes;

	/// <summary>The current file's stated size, when one side told one.</summary>
	public long? FileTotal => _state == ZmodemSessionState.Receiving ? _inbound?.Length : _outbound.TryPeek(out ZmodemFileInfo? outbound) ? outbound.Length : null;

	/// <summary>True while the terminal's bytes belong to the transfer rather than the view.</summary>
	public bool Intercepts => _state != ZmodemSessionState.Watching && _overAndOut != 0;

	/// <summary>The file the sender sends now, for progress and data pulls.</summary>
	public ZmodemFileInfo? CurrentOutbound => _outbound.TryPeek(out ZmodemFileInfo? next) ? next : null;

	/// <summary>Feeds channel bytes through the decoder and answers whatever the peer's frames ask for.</summary>
	public ZmodemStep Feed(ReadOnlySpan<byte> bytes)
	{
		ZmodemStep step = new();
		_decoder.Feed(bytes);
		foreach (ZmodemReceived received in _decoder.Events)
		{
			switch (received.Kind)
			{
				case ZmodemReceivedKind.Noise:
					// After the session closed, only the peer's "OO" is swallowed; anything else is terminal output again.
					if (_overAndOut > 0)
					{
						if (received.NoiseByte == (byte)'O')
						{
							_overAndOut--;
							break;
						}

						_overAndOut = 0;
					}

					if (_state is (ZmodemSessionState.Watching or ZmodemSessionState.Done))
					{
						step.Passthrough.Add(received.NoiseByte);
					}

					break;
				case ZmodemReceivedKind.Header:
					OnHeader(received.Header, step);
					break;
				case ZmodemReceivedKind.Packet:
					OnPacket(received.Packet!, step);
					break;
				case ZmodemReceivedKind.Malformed:
					OnMalformed(step);
					break;
				case ZmodemReceivedKind.Canceled:
					Finish(step, "The other side canceled the transfer.", aborted: true);
					break;
			}
		}

		return step;
	}

	/// <summary>Accepts the offered file: tells the peer to send it from the beginning.</summary>
	public ZmodemStep Accept()
	{
		ZmodemStep step = new();
		if (_state != ZmodemSessionState.Offering)
		{
			return step;
		}

		_inbound = _offer;
		_offer = null;
		_fileBytes = 0;
		_rejectData = false;
		_zdataSent = false;
		SendHeader(step, ZmodemHeader.OfPosition(ZmodemCode.ZRPOS, 0));
		_state = ZmodemSessionState.Receiving;
		step.FileStarted = true;
		return step;
	}

	/// <summary>Declines the offered file; the peer moves on to its next one.</summary>
	public ZmodemStep Skip()
	{
		ZmodemStep step = new();
		if (_state != ZmodemSessionState.Offering)
		{
			return step;
		}

		_offer = null;
		SendHeader(step, new ZmodemHeader(ZmodemCode.ZSKIP));
		_state = ZmodemSessionState.Negotiating;
		return step;
	}

	/// <summary>Aborts the session with ZMODEM's cancel sequence, whatever it was doing.</summary>
	public ZmodemStep Cancel()
	{
		ZmodemStep step = new();
		if (_state is (ZmodemSessionState.Watching or ZmodemSessionState.Done))
		{
			return step;
		}

		_encoder.CancelSequence();
		TakeEncoderOutput(step);
		Finish(step, null, aborted: true);
		return step;
	}

	/// <summary>Gives the sender files to send. An empty list closes the session instead.</summary>
	public ZmodemStep StartUpload(IReadOnlyList<ZmodemFileInfo> files)
	{
		ZmodemStep step = new();
		if (_state != ZmodemSessionState.ChoosingFiles)
		{
			return step;
		}

		if (files.Count == 0)
		{
			CloseAsSender(step);
			return step;
		}

		foreach (ZmodemFileInfo file in files)
		{
			_outbound.Enqueue(file);
		}

		SendNextFile(step);
		return step;
	}

	/// <summary>
	/// Feeds the sender with bytes read from the current file at the position it last asked for. The sender cuts them
	/// into subpackets; when the file's stated length is reached it closes the file with ZEOF.
	/// </summary>
	public ZmodemStep SupplyData(ReadOnlySpan<byte> data)
	{
		ZmodemStep step = new();
		if (_state != ZmodemSessionState.Sending || !_outbound.TryPeek(out ZmodemFileInfo? file))
		{
			return step;
		}

		long length = file.Length ?? 0;
		if (!_zdataSent)
		{
			SendHeader(step, ZmodemHeader.OfPosition(ZmodemCode.ZDATA, _sendPosition, BinaryEncoding()), BinaryEncoding());
			_zdataSent = true;
		}

		if (data.IsEmpty && _sendPosition < length)
		{
			// The file's bytes ran out before its stated length, so it can never be closed honestly.
			_encoder.CancelSequence();
			TakeEncoderOutput(step);
			Finish(step, "The file to send is shorter than it said it was.", aborted: true);
			return step;
		}
		long remaining = Math.Max(0, length - _sendPosition);
		int count = (int)Math.Min(data.Length, remaining);
		int offset = 0;
		while (offset < count)
		{
			int chunk = (int)Math.Min(SubpacketBytes, count - offset);
			bool last = _sendPosition + chunk >= length;
			_encoder.Subpacket(data.Slice(offset, chunk), last ? ZmodemCode.ZCRCE : ZmodemCode.ZCRCG, _peerWantsCrc32);
			_sendPosition += chunk;
			offset += chunk;
		}

		_fileBytes = _sendPosition;
		TakeEncoderOutput(step);

		if (_sendPosition >= length)
		{
			SendHeader(step, ZmodemHeader.OfPosition(ZmodemCode.ZEOF, length, BinaryEncoding()), BinaryEncoding());
			_state = ZmodemSessionState.Negotiating;
			step.FileFinished = true;
		}
		else
		{
			step.DataWanted = true;
			step.DataWantedBytes = (int)Math.Min(DataChunkBytes, length - _sendPosition);
			step.DataPosition = _sendPosition;
		}

		return step;
	}

	private void OnHeader(ZmodemHeader header, ZmodemStep step)
	{
		if (_state == ZmodemSessionState.Watching)
		{
			Watch(header, step);
			return;
		}

		if (_state == ZmodemSessionState.Done)
		{
			return;
		}

		if (_role == Role.Sender)
		{
			Send(header, step);
		}
		else
		{
			Receive(header, step);
		}
	}

	/// <summary>The entry frames: a ZRQINIT from <c>sz</c>, a ZRINIT from <c>rz</c>, or a ZFILE or ZSINIT from either.</summary>
	private static bool IsEntry(byte type) => type
		is ZmodemCode.ZRQINIT or ZmodemCode.ZRINIT or ZmodemCode.ZFILE or ZmodemCode.ZSINIT;

	private void Watch(ZmodemHeader header, ZmodemStep step)
	{
		if (!IsEntry(header.Type))
		{
			// A frame from a session we never joined is swallowed whole; nothing of it reaches the terminal.
			return;
		}

		if (header.Type == ZmodemCode.ZRINIT)
		{
			// The peer announced itself as the receiver, the way rz does.
			_role = Role.Sender;
			_peerWantsCrc32 = (header.Capabilities & ZmodemCode.CANFC32) != 0;
			_state = ZmodemSessionState.ChoosingFiles;
			return;
		}

		_role = Role.Receiver;
		Receive(header, step);
	}

	private void Receive(ZmodemHeader header, ZmodemStep step)
	{
		switch (header.Type)
		{
			case ZmodemCode.ZRQINIT:
				// The peer started as a sender; answer with what we can do, every time it asks again.
				SendHeader(step, ZmodemHeader.OurZrinit());
				_state = ZmodemSessionState.Negotiating;
				break;
			case ZmodemCode.ZSINIT:
				_expectSinitPacket = true;
				break;
			case ZmodemCode.ZFILE:
				if (_state != ZmodemSessionState.Offering)
				{
					_state = ZmodemSessionState.Negotiating;
				}

				// Set either way: a retransmission while the user thinks carries the packet too.
				_expectFilePacket = true;
				break;
			case ZmodemCode.ZDATA:
				if (_state != ZmodemSessionState.Receiving)
				{
					break;
				}

				if (header.Position != _fileBytes)
				{
					// Data from a position we are not at: send the peer back to where our file ends.
					_rejectData = true;
					SendHeader(step, ZmodemHeader.OfPosition(ZmodemCode.ZRPOS, _fileBytes));
					break;
				}

				_rejectData = false;
				break;
			case ZmodemCode.ZEOF:
				if (_state != ZmodemSessionState.Receiving)
				{
					break;
				}

				if (header.Position == _fileBytes)
				{
					step.FileFinished = true;
					SendHeader(step, ZmodemHeader.OurZrinit());
					_state = ZmodemSessionState.Negotiating;
					_inbound = null;
				}
				else
				{
					SendHeader(step, ZmodemHeader.OfPosition(ZmodemCode.ZRPOS, _fileBytes));
				}

				break;
			case ZmodemCode.ZFIN:
				SendHeader(step, new ZmodemHeader(ZmodemCode.ZFIN));
				Finish(step, null, aborted: false);
				// We answered the peer's ZFIN, so its closing "OO" is still ours to swallow.
				_overAndOut = 2;
				break;
			case ZmodemCode.ZNAK:
				if (_sentAnyHeader)
				{
					_encoder.Header(_lastHeader, _lastHeader.Encoding);
					TakeEncoderOutput(step);
				}

				break;
			case ZmodemCode.ZCHALLENGE:
				SendHeader(step, ZmodemHeader.OfPosition(ZmodemCode.ZACK, header.Position));
				break;
			case ZmodemCode.ZABORT or ZmodemCode.ZCAN or ZmodemCode.ZFERR:
				Finish(step, header.Type == ZmodemCode.ZFERR ? "The other side could not write the file." : "The other side canceled the transfer.", aborted: true);
				break;
		}
	}

	private void Send(ZmodemHeader header, ZmodemStep step)
	{
		switch (header.Type)
		{
			case ZmodemCode.ZRINIT:
				_peerWantsCrc32 = (header.Capabilities & ZmodemCode.CANFC32) != 0;
				if (_state == ZmodemSessionState.ChoosingFiles)
				{
					// The peer repeats its invitation while we wait for files; there is nothing to answer yet.
					break;
				}

				// The receipt for a finished file: on to the next, or close the session.
				_outbound.Dequeue();
				SendNextFile(step);
				break;
			case ZmodemCode.ZSKIP:
				// The peer refused the file being sent or just finished; either way it does not want it.
				_outbound.Dequeue();
				SendNextFile(step);
				break;
			case ZmodemCode.ZRPOS:
				// Sending, or just finished a file the peer has not acknowledged: both still have it at the head.
				if (_state is not (ZmodemSessionState.Sending or ZmodemSessionState.Negotiating)
					|| !_outbound.TryPeek(out ZmodemFileInfo? file)
					|| file.Length is not { } length)
				{
					break;
				}

				_state = ZmodemSessionState.Sending;

				if (header.Position < 0 || header.Position > length)
				{
					_encoder.CancelSequence();
					TakeEncoderOutput(step);
					Finish(step, "The other side asked to continue at a position past the end of the file.", aborted: true);
					break;
				}

				// The first go-ahead for this file, or a retransmit request mid-stream: both start at that position.
				_sendPosition = header.Position;
				_zdataSent = false;
				if (header.Position == 0)
				{
					_fileBytes = 0;
					step.FileStarted = true;
				}

				if (_sendPosition >= length)
				{
					// Nothing to send: an empty file ends the moment the peer allows it to start, with an empty
					// closing subpacket so the frame grammar still holds.
					SendHeader(step, ZmodemHeader.OfPosition(ZmodemCode.ZDATA, _sendPosition, BinaryEncoding()), BinaryEncoding());
					_encoder.Subpacket([], ZmodemCode.ZCRCE, _peerWantsCrc32);
					TakeEncoderOutput(step);
					SendHeader(step, ZmodemHeader.OfPosition(ZmodemCode.ZEOF, length, BinaryEncoding()), BinaryEncoding());
					_state = ZmodemSessionState.Negotiating;
					step.FileFinished = true;
					break;
				}

				step.DataWanted = true;
				step.DataWantedBytes = (int)Math.Min(DataChunkBytes, length - _sendPosition);
				step.DataPosition = _sendPosition;
				break;
			case ZmodemCode.ZACK:
				if (_state == ZmodemSessionState.Sending)
				{
					_fileBytes = header.Position;
				}

				break;
			case ZmodemCode.ZFIN:
				SendHeader(step, new ZmodemHeader(ZmodemCode.ZFIN));
				_encoder.Raw((byte)'O');
				_encoder.Raw((byte)'O');
				TakeEncoderOutput(step);
				Finish(step, null, aborted: false);
				break;
			case ZmodemCode.ZABORT or ZmodemCode.ZCAN or ZmodemCode.ZFERR:
				Finish(step, header.Type == ZmodemCode.ZFERR ? "The other side could not write the file." : "The other side canceled the transfer.", aborted: true);
				break;
		}
	}

	private void OnPacket(ZmodemDataPacket packet, ZmodemStep step)
	{
		if (_expectSinitPacket)
		{
			// The peer's attention string, acknowledged and never used: this side cannot interrupt a stream through a PTY.
			_expectSinitPacket = false;
			SendHeader(step, ZmodemHeader.OfPosition(ZmodemCode.ZACK, 1));
			return;
		}

		if (_expectFilePacket)
		{
			_expectFilePacket = false;
			if (_state == ZmodemSessionState.Offering)
			{
				// The retransmission of an offer already standing; the first packet said everything.
				return;
			}

			ZmodemFileInfo info = ZmodemFileInfo.Parse(packet.Data);
			if (info.Name.Length == 0)
			{
				SendHeader(step, new ZmodemHeader(ZmodemCode.ZSKIP));
				return;
			}

			_offer = info;
			_state = ZmodemSessionState.Offering;
			step.Offer = info;
			return;
		}

		if (_state == ZmodemSessionState.Receiving && !_rejectData)
		{
			step.FileData.Add(packet.Data);
			_fileBytes += packet.Data.Length;
			if (packet.End is (ZmodemCode.ZCRCQ or ZmodemCode.ZCRCW))
			{
				SendHeader(step, ZmodemHeader.OfPosition(ZmodemCode.ZACK, _fileBytes));
			}
		}
	}

	private void OnMalformed(ZmodemStep step)
	{
		if (_state == ZmodemSessionState.Receiving)
		{
			// A frame whose check failed: everything after the last good byte is suspect, so send the peer back there.
			_rejectData = true;
			SendHeader(step, ZmodemHeader.OfPosition(ZmodemCode.ZRPOS, _fileBytes));
		}
	}

	private void SendNextFile(ZmodemStep step)
	{
		if (_outbound.TryPeek(out ZmodemFileInfo? next) && next.Length is { })
		{
			_fileBytes = 0;
			_sendPosition = 0;
			_zdataSent = false;
			ZmodemHeader fileHeader = new(ZmodemCode.ZFILE, ZmodemCode.ZCBIN, ZmodemCode.ZMClob, 0, 0, BinaryEncoding());
			SendHeader(step, fileHeader, BinaryEncoding());
			_encoder.Subpacket(next.ToSubpacket(), ZmodemCode.ZCRCW, _peerWantsCrc32);
			TakeEncoderOutput(step);
			_state = ZmodemSessionState.Sending;
			step.FileStarted = true;
			return;
		}

		CloseAsSender(step);
	}

	/// <summary>Closes the sending side: ZFIN, our "OO" after the peer's answer, done.</summary>
	private void CloseAsSender(ZmodemStep step)
	{
		SendHeader(step, new ZmodemHeader(ZmodemCode.ZFIN));
		Finish(step, null, aborted: false);
	}

	private ZmodemHeaderEncoding BinaryEncoding() => _peerWantsCrc32 ? ZmodemHeaderEncoding.Binary32 : ZmodemHeaderEncoding.Binary16;

	private void SendHeader(ZmodemStep step, ZmodemHeader header) => SendHeader(step, header, header.Encoding);

	private void SendHeader(ZmodemStep step, ZmodemHeader header, ZmodemHeaderEncoding encoding)
	{
		_lastHeader = header with { Encoding = encoding };
		_sentAnyHeader = true;
		_encoder.Header(_lastHeader, encoding);
		TakeEncoderOutput(step);
	}

	private void TakeEncoderOutput(ZmodemStep step)
	{
		if (_encoder.HasOutput)
		{
			step.Send.AddRange(_encoder.TakeOutput());
		}
	}

	private void Finish(ZmodemStep step, string? reason, bool aborted)
	{
		if (_state == ZmodemSessionState.Done)
		{
			return;
		}

		_state = ZmodemSessionState.Done;
		_offer = null;
		_overAndOut = 0;
		step.Done = true;
		step.FailedReason = reason;
		step.Aborted = aborted;
	}
}
