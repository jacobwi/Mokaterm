namespace Mokaterm.Modules.Telnet.Protocol;

/// <summary>
/// Splits a telnet stream into terminal data and protocol messages. It keeps its state between calls, so a
/// command, an escaped IAC or a subnegotiation may straddle any number of reads.
/// </summary>
internal sealed class TelnetInputParser
{
	/// <summary>
	/// Longest subnegotiation payload kept. Nothing this module answers comes near it, and a server that never
	/// sends SE must not be able to grow the buffer without end.
	/// </summary>
	internal const int MaxSubnegotiationLength = 4096;

	private readonly List<byte> _subnegotiation = [];
	private State _state = State.Data;
	private byte _command;
	private byte _subnegotiationOption;
	private bool _subnegotiationOverflowed;

	private enum State
	{
		Data,
		Command,
		Negotiation,
		SubnegotiationOption,
		Subnegotiation,
		SubnegotiationCommand,
	}

	/// <summary>
	/// Consumes <paramref name="input"/>, writing terminal bytes to <paramref name="data"/> and appending every
	/// message it finds to <paramref name="messages"/>. Returns how many bytes of <paramref name="data"/> were
	/// written; it is never more than the length of the input, which is why the caller may pass one buffer of that
	/// size.
	/// </summary>
	public int Feed(ReadOnlySpan<byte> input, Span<byte> data, List<TelnetMessage> messages)
	{
		ArgumentNullException.ThrowIfNull(messages);
		if (data.Length < input.Length)
		{
			throw new ArgumentException("The data buffer must be at least as long as the input.", nameof(data));
		}

		int written = 0;
		foreach (byte value in input)
		{
			switch (_state)
			{
				case State.Data:
					if (value == TelnetCommand.Iac)
					{
						_state = State.Command;
					}
					else
					{
						data[written++] = value;
					}

					break;

				case State.Command:
					if (value == TelnetCommand.Iac)
					{
						// IAC IAC is one data byte with the value 255.
						data[written++] = TelnetCommand.Iac;
						_state = State.Data;
					}
					else if (TelnetCommand.IsNegotiation(value))
					{
						_command = value;
						_state = State.Negotiation;
					}
					else if (value == TelnetCommand.Sb)
					{
						_state = State.SubnegotiationOption;
					}
					else
					{
						messages.Add(TelnetMessage.Bare(value));
						_state = State.Data;
					}

					break;

				case State.Negotiation:
					messages.Add(TelnetMessage.Negotiation(_command, value));
					_state = State.Data;
					break;

				case State.SubnegotiationOption:
					_subnegotiationOption = value;
					_subnegotiation.Clear();
					_subnegotiationOverflowed = false;
					_state = State.Subnegotiation;
					break;

				case State.Subnegotiation:
					if (value == TelnetCommand.Iac)
					{
						_state = State.SubnegotiationCommand;
					}
					else
					{
						AppendSubnegotiation(value);
					}

					break;

				case State.SubnegotiationCommand:
					if (value == TelnetCommand.Iac)
					{
						AppendSubnegotiation(TelnetCommand.Iac);
						_state = State.Subnegotiation;
					}
					else if (value == TelnetCommand.Se)
					{
						if (!_subnegotiationOverflowed)
						{
							messages.Add(TelnetMessage.Subnegotiation(_subnegotiationOption, [.. _subnegotiation]));
						}

						_subnegotiation.Clear();
						_state = State.Data;
					}
					else
					{
						// Any other command inside a subnegotiation ends it. The payload is dropped rather than waiting
						// for an SE that a confused server may never send.
						_subnegotiation.Clear();
						if (TelnetCommand.IsNegotiation(value))
						{
							_command = value;
							_state = State.Negotiation;
						}
						else if (value == TelnetCommand.Sb)
						{
							_state = State.SubnegotiationOption;
						}
						else
						{
							messages.Add(TelnetMessage.Bare(value));
							_state = State.Data;
						}
					}

					break;

				default:
					throw new InvalidOperationException($"Unknown parser state {_state}.");
			}
		}

		return written;
	}

	private void AppendSubnegotiation(byte value)
	{
		if (_subnegotiation.Count >= MaxSubnegotiationLength)
		{
			_subnegotiationOverflowed = true;
			return;
		}

		_subnegotiation.Add(value);
	}
}
