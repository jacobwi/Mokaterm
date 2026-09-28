using System.Buffers;
using System.Globalization;
using System.Text;
using Mokaterm.Modules.Telnet.Protocol;

namespace Mokaterm.DevHost.Demo.Telnet;

/// <summary>
/// One connection to the demo telnet server: the server half of the negotiation, a login that accepts the seeded
/// account and a handful of commands that show what was negotiated.
/// </summary>
internal sealed class DemoTelnetConnection
{
	private const int BufferSize = 4096;

	private const byte Backspace = 0x08;

	private const byte Delete = 0x7f;

	private const byte Interrupt = 0x03;

	/// <summary>Options the client may turn on for itself.</summary>
	private static readonly byte[] AcceptedFromClient =
	[
		TelnetOption.Binary,
		TelnetOption.SuppressGoAhead,
		TelnetOption.TerminalType,
		TelnetOption.NegotiateAboutWindowSize,
	];

	/// <summary>Options this server turns on for itself.</summary>
	private static readonly byte[] AcceptedForServer =
	[
		TelnetOption.Binary,
		TelnetOption.Echo,
		TelnetOption.SuppressGoAhead,
	];

	private readonly Stream _stream;
	private readonly string _username;
	private readonly string _password;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly TelnetInputParser _parser = new();
	private readonly ArrayBufferWriter<byte> _out = new(BufferSize);
	private readonly StringBuilder _line = new();
	private readonly HashSet<byte> _clientEnabled = [];
	private readonly HashSet<byte> _serverEnabled = [];
	private readonly HashSet<byte> _pendingClient = [];
	private readonly HashSet<byte> _pendingServer = [];
	private Stage _stage = Stage.Username;
	private string _terminalType = "unknown";
	private int _columns;
	private int _rows;
	private string _typedUsername = "";
	private bool _afterCarriageReturn;
	private bool _running = true;

	public DemoTelnetConnection(Stream stream, string username, string password, TimeProvider timeProvider, ILogger logger)
	{
		_stream = stream;
		_username = username;
		_password = password;
		_timeProvider = timeProvider;
		_logger = logger;
	}

	private enum Stage
	{
		Username,
		Password,
		Shell,
	}

	private bool ServerEchoes => _serverEnabled.Contains(TelnetOption.Echo);

	public async Task RunAsync(CancellationToken cancellationToken)
	{
		Offer(TelnetCommand.Will, TelnetOption.Echo);
		Offer(TelnetCommand.Will, TelnetOption.SuppressGoAhead);
		Offer(TelnetCommand.Do, TelnetOption.SuppressGoAhead);
		Offer(TelnetCommand.Do, TelnetOption.TerminalType);
		Offer(TelnetCommand.Do, TelnetOption.NegotiateAboutWindowSize);
		Send("\r\nMokaterm demo console\r\nThis is a plain telnet connection: everything on it is readable.\r\n");

		// The workbench makes a new password for every run, so the only way to type it by hand is to read it here.
		Send($"Log in as {_username} with the password {_password}\r\n\r\n");
		Send("Username: ");
		await FlushAsync(cancellationToken);

		byte[] buffer = new byte[BufferSize];
		byte[] data = new byte[BufferSize];
		List<TelnetMessage> messages = [];
		while (_running)
		{
			int read = await _stream.ReadAsync(buffer, cancellationToken);
			if (read == 0)
			{
				return;
			}

			messages.Clear();
			int length = _parser.Feed(buffer.AsSpan(0, read), data, messages);
			foreach (TelnetMessage message in messages)
			{
				Handle(message);
			}

			for (int i = 0; i < length && _running; i++)
			{
				HandleInput(data[i]);
			}

			await FlushAsync(cancellationToken);
		}
	}

	private static string Describe(byte[] payload) =>
		payload.Length <= 1 ? "unknown" : Encoding.ASCII.GetString(payload, 1, payload.Length - 1);

	private void Offer(byte command, byte option)
	{
		if (command == TelnetCommand.Will)
		{
			_serverEnabled.Add(option);
			_pendingServer.Add(option);
		}
		else
		{
			_clientEnabled.Add(option);
			_pendingClient.Add(option);
		}

		TelnetWire.WriteNegotiation(_out, command, option);
	}

	private void Handle(TelnetMessage message)
	{
		switch (message.Command)
		{
			case TelnetCommand.Will:
				_pendingClient.Remove(message.Option);
				if (Array.IndexOf(AcceptedFromClient, message.Option) < 0)
				{
					TelnetWire.WriteNegotiation(_out, TelnetCommand.Dont, message.Option);
				}
				else if (_clientEnabled.Add(message.Option))
				{
					TelnetWire.WriteNegotiation(_out, TelnetCommand.Do, message.Option);
					AskTerminalType(message.Option);
				}
				else
				{
					AskTerminalType(message.Option);
				}

				break;

			case TelnetCommand.Wont:
				if (_clientEnabled.Remove(message.Option) && !_pendingClient.Remove(message.Option))
				{
					TelnetWire.WriteNegotiation(_out, TelnetCommand.Dont, message.Option);
				}

				break;

			case TelnetCommand.Do:
				_pendingServer.Remove(message.Option);
				if (Array.IndexOf(AcceptedForServer, message.Option) < 0)
				{
					TelnetWire.WriteNegotiation(_out, TelnetCommand.Wont, message.Option);
				}
				else if (_serverEnabled.Add(message.Option))
				{
					TelnetWire.WriteNegotiation(_out, TelnetCommand.Will, message.Option);
				}

				break;

			case TelnetCommand.Dont:
				if (_serverEnabled.Remove(message.Option) && !_pendingServer.Remove(message.Option))
				{
					TelnetWire.WriteNegotiation(_out, TelnetCommand.Wont, message.Option);
				}

				break;

			case TelnetCommand.Sb:
				HandleSubnegotiation(message);
				break;

			default:
				break;
		}
	}

	private void AskTerminalType(byte option)
	{
		if (option == TelnetOption.TerminalType)
		{
			TelnetWire.WriteSubnegotiation(_out, TelnetOption.TerminalType, [TelnetOption.Send]);
		}
	}

	private void HandleSubnegotiation(TelnetMessage message)
	{
		if (message.Option == TelnetOption.TerminalType && message.Payload.Length > 1 && message.Payload[0] == TelnetOption.Is)
		{
			_terminalType = Describe(message.Payload);
			_logger.LogDebug("The demo telnet client says its terminal is {TerminalType}.", _terminalType);
		}
		else if (message.Option == TelnetOption.NegotiateAboutWindowSize && message.Payload.Length >= 4)
		{
			_columns = (message.Payload[0] << 8) | message.Payload[1];
			_rows = (message.Payload[2] << 8) | message.Payload[3];
		}
	}

	private void HandleInput(byte value)
	{
		switch (value)
		{
			case (byte)'\r':
				_afterCarriageReturn = true;
				Submit();
				break;

			case (byte)'\n':
				// CR LF is one Enter; a client set to send LF alone gets the same answer.
				if (!_afterCarriageReturn)
				{
					Submit();
				}

				_afterCarriageReturn = false;
				break;

			case Backspace:
			case Delete:
				if (_line.Length > 0)
				{
					_line.Length--;
					if (ServerEchoes && _stage != Stage.Password)
					{
						Send("\b \b");
					}
				}

				break;

			case Interrupt:
				_line.Clear();
				Send("^C\r\n");
				Prompt();
				break;

			case 0:
				break;

			default:
				_afterCarriageReturn = false;
				if (value >= 0x20)
				{
					_line.Append((char)value);
					if (ServerEchoes && _stage != Stage.Password)
					{
						Send(((char)value).ToString());
					}
				}

				break;
		}
	}

	private void Submit()
	{
		string line = _line.ToString();
		_line.Clear();
		Send("\r\n");
		switch (_stage)
		{
			case Stage.Username:
				_typedUsername = line.Trim();
				_stage = Stage.Password;
				Send("Password: ");
				break;

			case Stage.Password:
				if (_typedUsername == _username && line == _password)
				{
					_stage = Stage.Shell;
					Send($"\r\nWelcome, {_typedUsername}. Type 'help' for what this console can do.\r\n");
					Prompt();
				}
				else
				{
					_stage = Stage.Username;
					Send("Login incorrect.\r\n\r\nUsername: ");
				}

				break;

			case Stage.Shell:
			default:
				Run(line.Trim());
				break;
		}
	}

	private void Prompt() => Send(_stage switch
	{
		Stage.Username => "Username: ",
		Stage.Password => "Password: ",
		_ => $"{_typedUsername}@demo> ",
	});

	private void Run(string line)
	{
		string command = line.Split(' ', 2)[0].ToLowerInvariant();
		string argument = line.Length > command.Length ? line[(command.Length + 1)..] : "";
		switch (command)
		{
			case "":
				break;

			case "help":
				Send("Commands: help, term, size, time, echo <text>, colors, clear, exit\r\n");
				break;

			case "term":
				Send($"Terminal type: {_terminalType}\r\n");
				break;

			case "size":
				Send(_columns > 0
					? $"Window: {_columns} by {_rows} (from NAWS, updated on every resize)\r\n"
					: "The client did not turn NAWS on, so the window size is unknown.\r\n");
				break;

			case "time":
				Send($"{_timeProvider.GetLocalNow().ToString("F", CultureInfo.InvariantCulture)}\r\n");
				break;

			case "echo":
				Send(argument + "\r\n");
				break;

			case "colors":
				SendColors();
				break;

			case "clear":
				Send("[2J[H");
				break;

			case "exit":
			case "quit":
			case "logout":
				Send("Goodbye.\r\n");
				_running = false;
				return;

			default:
				Send($"{command}: not found\r\n");
				break;
		}

		Prompt();
	}

	private void SendColors()
	{
		StringBuilder colors = new();
		for (int i = 0; i < 8; i++)
		{
			colors.Append(CultureInfo.InvariantCulture, $"[4{i}m  [0m");
		}

		colors.Append("\r\n");
		Send(colors.ToString());
	}

	private void Send(string text)
	{
		if (text.Length > 0)
		{
			TelnetWire.WriteEscaped(_out, Encoding.UTF8.GetBytes(text));
		}
	}

	private async Task FlushAsync(CancellationToken cancellationToken)
	{
		if (_out.WrittenCount == 0)
		{
			return;
		}

		await _stream.WriteAsync(_out.WrittenMemory, cancellationToken);
		await _stream.FlushAsync(cancellationToken);
		_out.ResetWrittenCount();
	}
}
