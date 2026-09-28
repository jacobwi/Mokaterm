using System.Buffers;
using System.Text;

namespace Mokaterm.Modules.Telnet.Protocol;

/// <summary>
/// The option negotiation of RFC 854, run as the state machine of RFC 1143 so a request and an offer that cross on
/// the wire settle instead of bouncing forever. Options this module does not implement are refused, never ignored.
/// </summary>
internal sealed class TelnetNegotiator
{
	/// <summary>Options we offer to turn on for ourselves. Everything else is answered with WONT.</summary>
	private static readonly byte[] LocalOptions =
	[
		TelnetOption.Binary,
		TelnetOption.SuppressGoAhead,
		TelnetOption.TerminalType,
		TelnetOption.NegotiateAboutWindowSize,
	];

	/// <summary>Options we let the server turn on. Everything else is answered with DONT.</summary>
	private static readonly byte[] RemoteOptions =
	[
		TelnetOption.Binary,
		TelnetOption.SuppressGoAhead,
		TelnetOption.Echo,
	];

	/// <summary>Longest terminal type name sent. Real names are far shorter, and the answer is built on the stack.</summary>
	private const int MaxTerminalTypeLength = 64;

	private readonly Dictionary<byte, TelnetOptionState> _local = [];
	private readonly Dictionary<byte, TelnetOptionState> _remote = [];
	private readonly HashSet<int> _outstanding = [];
	private readonly string[] _terminalTypes;
	private int _terminalTypeIndex;

	/// <param name="terminalTypes">
	/// Answers to TERMINAL-TYPE SEND, most wanted first. A server that keeps asking walks the list and then gets the
	/// last name again, which is how RFC 1091 says the list ends.
	/// </param>
	public TelnetNegotiator(IEnumerable<string> terminalTypes)
	{
		ArgumentNullException.ThrowIfNull(terminalTypes);
		_terminalTypes =
		[
			.. terminalTypes
				.Where(type => !string.IsNullOrWhiteSpace(type))
				.Select(type => type.Length > MaxTerminalTypeLength ? type[..MaxTerminalTypeLength] : type)
				.Distinct(StringComparer.OrdinalIgnoreCase),
		];

		if (_terminalTypes.Length == 0)
		{
			_terminalTypes = ["UNKNOWN"];
		}
	}

	/// <summary>True once every option this side asked about at the start has been answered.</summary>
	public bool IsSettled => _outstanding.Count == 0;

	/// <summary>The name the next TERMINAL-TYPE answer carries.</summary>
	public string TerminalType => _terminalTypes[Math.Min(_terminalTypeIndex, _terminalTypes.Length - 1)];

	public bool IsLocalEnabled(byte option) => StateOf(_local, option) == TelnetOptionState.Yes;

	public bool IsRemoteEnabled(byte option) => StateOf(_remote, option) == TelnetOptionState.Yes;

	/// <summary>
	/// Opens the negotiation: 8 bit data both ways, character at a time (both sides suppress go ahead), the server
	/// echoing, and our terminal type and window size.
	/// </summary>
	public void WriteInitialRequests(IBufferWriter<byte> output)
	{
		RequestRemote(TelnetOption.Binary, output);
		RequestRemote(TelnetOption.Echo, output);
		RequestRemote(TelnetOption.SuppressGoAhead, output);
		RequestLocal(TelnetOption.Binary, output);
		RequestLocal(TelnetOption.SuppressGoAhead, output);
		RequestLocal(TelnetOption.TerminalType, output);
		RequestLocal(TelnetOption.NegotiateAboutWindowSize, output);
	}

	/// <summary>Answers one message, writing whatever the protocol owes the server to <paramref name="output"/>.</summary>
	public void Handle(TelnetMessage message, IBufferWriter<byte> output)
	{
		ArgumentNullException.ThrowIfNull(output);
		switch (message.Command)
		{
			case TelnetCommand.Will:
				_outstanding.Remove(Key(remote: true, message.Option));
				HandleWill(message.Option, output);
				break;

			case TelnetCommand.Wont:
				_outstanding.Remove(Key(remote: true, message.Option));
				HandleWont(message.Option, output);
				break;

			case TelnetCommand.Do:
				_outstanding.Remove(Key(remote: false, message.Option));
				HandleDo(message.Option, output);
				break;

			case TelnetCommand.Dont:
				_outstanding.Remove(Key(remote: false, message.Option));
				HandleDont(message.Option, output);
				break;

			case TelnetCommand.Sb:
				HandleSubnegotiation(message, output);
				break;

			default:
				// NOP, GA, DM and the interrupt commands need no answer from a client with a terminal attached.
				break;
		}
	}

	/// <summary>Asks the server to turn an option on. Does nothing when it is already on or already asked for.</summary>
	public void RequestRemote(byte option, IBufferWriter<byte> output)
	{
		if (StateOf(_remote, option) != TelnetOptionState.No)
		{
			return;
		}

		_remote[option] = TelnetOptionState.WantYes;
		_outstanding.Add(Key(remote: true, option));
		TelnetWire.WriteNegotiation(output, TelnetCommand.Do, option);
	}

	/// <summary>Offers to turn an option on for ourselves.</summary>
	public void RequestLocal(byte option, IBufferWriter<byte> output)
	{
		if (StateOf(_local, option) != TelnetOptionState.No)
		{
			return;
		}

		_local[option] = TelnetOptionState.WantYes;
		_outstanding.Add(Key(remote: false, option));
		TelnetWire.WriteNegotiation(output, TelnetCommand.Will, option);
	}

	private static int Key(bool remote, byte option) => remote ? option : option + 256;

	private static TelnetOptionState StateOf(Dictionary<byte, TelnetOptionState> states, byte option) =>
		states.TryGetValue(option, out TelnetOptionState state) ? state : TelnetOptionState.No;

	private void HandleWill(byte option, IBufferWriter<byte> output)
	{
		switch (StateOf(_remote, option))
		{
			case TelnetOptionState.No:
				if (Array.IndexOf(RemoteOptions, option) >= 0)
				{
					_remote[option] = TelnetOptionState.Yes;
					TelnetWire.WriteNegotiation(output, TelnetCommand.Do, option);
				}
				else
				{
					TelnetWire.WriteNegotiation(output, TelnetCommand.Dont, option);
				}

				break;

			case TelnetOptionState.WantNo:
				_remote[option] = TelnetOptionState.No;
				break;

			case TelnetOptionState.WantNoOpposite:
			case TelnetOptionState.WantYes:
				_remote[option] = TelnetOptionState.Yes;
				break;

			case TelnetOptionState.WantYesOpposite:
				_remote[option] = TelnetOptionState.WantNo;
				TelnetWire.WriteNegotiation(output, TelnetCommand.Dont, option);
				break;

			case TelnetOptionState.Yes:
			default:
				break;
		}
	}

	private void HandleWont(byte option, IBufferWriter<byte> output)
	{
		switch (StateOf(_remote, option))
		{
			case TelnetOptionState.Yes:
				_remote[option] = TelnetOptionState.No;
				TelnetWire.WriteNegotiation(output, TelnetCommand.Dont, option);
				break;

			case TelnetOptionState.WantNoOpposite:
				_remote[option] = TelnetOptionState.WantYes;
				TelnetWire.WriteNegotiation(output, TelnetCommand.Do, option);
				break;

			case TelnetOptionState.WantNo:
			case TelnetOptionState.WantYes:
			case TelnetOptionState.WantYesOpposite:
				_remote[option] = TelnetOptionState.No;
				break;

			case TelnetOptionState.No:
			default:
				break;
		}
	}

	private void HandleDo(byte option, IBufferWriter<byte> output)
	{
		switch (StateOf(_local, option))
		{
			case TelnetOptionState.No:
				if (Array.IndexOf(LocalOptions, option) >= 0)
				{
					_local[option] = TelnetOptionState.Yes;
					TelnetWire.WriteNegotiation(output, TelnetCommand.Will, option);
				}
				else
				{
					TelnetWire.WriteNegotiation(output, TelnetCommand.Wont, option);
				}

				break;

			case TelnetOptionState.WantNo:
				_local[option] = TelnetOptionState.No;
				break;

			case TelnetOptionState.WantNoOpposite:
			case TelnetOptionState.WantYes:
				_local[option] = TelnetOptionState.Yes;
				break;

			case TelnetOptionState.WantYesOpposite:
				_local[option] = TelnetOptionState.WantNo;
				TelnetWire.WriteNegotiation(output, TelnetCommand.Wont, option);
				break;

			case TelnetOptionState.Yes:
			default:
				break;
		}
	}

	private void HandleDont(byte option, IBufferWriter<byte> output)
	{
		switch (StateOf(_local, option))
		{
			case TelnetOptionState.Yes:
				_local[option] = TelnetOptionState.No;
				TelnetWire.WriteNegotiation(output, TelnetCommand.Wont, option);
				break;

			case TelnetOptionState.WantNoOpposite:
				_local[option] = TelnetOptionState.WantYes;
				TelnetWire.WriteNegotiation(output, TelnetCommand.Will, option);
				break;

			case TelnetOptionState.WantNo:
			case TelnetOptionState.WantYes:
			case TelnetOptionState.WantYesOpposite:
				_local[option] = TelnetOptionState.No;
				break;

			case TelnetOptionState.No:
			default:
				break;
		}
	}

	private void HandleSubnegotiation(TelnetMessage message, IBufferWriter<byte> output)
	{
		// The only question this module answers is "what terminal are you", and only once that option is on.
		if (message.Option != TelnetOption.TerminalType
			|| !IsLocalEnabled(TelnetOption.TerminalType)
			|| message.Payload.Length == 0
			|| message.Payload[0] != TelnetOption.Send)
		{
			return;
		}

		string name = TerminalType;
		Span<byte> payload = stackalloc byte[1 + Encoding.ASCII.GetByteCount(name)];
		payload[0] = TelnetOption.Is;
		Encoding.ASCII.GetBytes(name, payload[1..]);
		TelnetWire.WriteSubnegotiation(output, TelnetOption.TerminalType, payload);

		// Asking again means the name was not understood, so the next answer moves down the list.
		if (_terminalTypeIndex < _terminalTypes.Length - 1)
		{
			_terminalTypeIndex++;
		}
	}
}
