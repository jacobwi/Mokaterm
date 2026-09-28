using System.Text;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Serial.Protocol;
using Mokaterm.Modules.Serial.Sessions;

namespace Mokaterm.Modules.Serial;

/// <summary>Opens serial sessions: the port, its line settings and the terminal on it all run here.</summary>
internal sealed class SerialProtocolProvider : IProtocolProvider
{
	public const string ProtocolId = "serial";

	/// <summary>
	/// A serial line has nothing a port number could mean. The descriptor has to name one, so it names 0, and
	/// <see cref="ProtocolDescriptor.UsesPort"/> is what keeps the number out of the editor and out of every label.
	/// </summary>
	public const int NoPort = 0;

	private readonly ISettingsService _settings;
	private readonly TimeProvider _timeProvider;
	private readonly ILoggerFactory _loggerFactory;
	private readonly ILogger _logger;

	public SerialProtocolProvider(ISettingsService settings, TimeProvider timeProvider, ILoggerFactory loggerFactory)
	{
		_settings = settings;
		_timeProvider = timeProvider;
		_loggerFactory = loggerFactory;
		_logger = loggerFactory.CreateLogger<SerialProtocolProvider>();
	}

	public ProtocolDescriptor Descriptor { get; } = new()
	{
		Id = ProtocolId,
		DisplayName = "Serial",
		Description = "A console over a serial port. The machine's address is the port name, such as COM3 or /dev/ttyUSB0.",
		DefaultPort = NoPort,
		UsesPort = false,
		Capabilities = ProtocolCapabilities.Terminal,

		// There is no login: a serial port is opened, not logged in to. A device that asks for one asks inside the
		// stream, like a telnet console does, and the answer is typed there.
		AuthenticationMethods = [AuthenticationMethod.Anonymous],
		RequiresUsername = false,
		Order = 60,
	};

	public async Task<IProtocolSession> ConnectAsync(ProtocolConnectContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		SerialSettings settings = _settings.Get<SerialSettings>().Clamped();
		SerialConnectionOptions options = SerialConnectionOptions.From(context.Connection.Options);
		string portName = options.ResolvePortName(context.Host.Address);
		if (SerialPortNames.Validate(portName) is { } badName)
		{
			throw new ProtocolConnectException(ConnectFailure.HostUnreachable, badName);
		}

		if (options.Line.Validate() is { } badLine)
		{
			throw new ProtocolConnectException(
				ConnectFailure.HostUnreachable,
				$"{portName} cannot be opened at {options.Line.Frame}: {badLine}");
		}

		if (!TerminalEncodings.TryGet(options.EncodingName, out Encoding? encoding))
		{
			_logger.LogWarning("Unknown serial character set {Encoding}; using UTF-8", options.EncodingName);
			encoding = Encoding.UTF8;
		}

		context.Status?.Report($"Opening {portName} at {options.Line.Describe()}");
		SerialPortLink link = await OpenAsync(
			new SerialOpenRequest
			{
				PortName = portName,
				Line = options.Line,
				Dtr = options.Dtr,
				Rts = options.Rts,
				ReadTimeout = settings.ReadTimeout,
				WriteTimeout = settings.WriteTimeout,
			},
			cancellationToken);

		SerialTerminalChannel? channel = null;
		try
		{
			channel = new SerialTerminalChannel(
				link,
				new SerialChannelOptions
				{
					Encoding = encoding,
					LineEnding = options.LineEnding,
					LocalEcho = options.LocalEcho,
					DeviceCheck = settings.DeviceCheck,
					Break = settings.Break,
					TimeProvider = _timeProvider,
				},
				_loggerFactory.CreateLogger<SerialTerminalChannel>());
			channel.Start();
			return new SerialSession(context.SessionId, portName, channel, _loggerFactory.CreateLogger<SerialSession>());
		}
		catch
		{
			// The channel owns the port from the moment it is built, so only one of the two is closed here.
			if (channel is not null)
			{
				await channel.DisposeAsync();
			}
			else
			{
				await link.DisposeAsync();
			}

			throw;
		}
	}

	/// <summary>
	/// Opens the port off the caller's thread: a driver can sit in an open for seconds, and on the web host that
	/// thread is the circuit. The open itself takes no cancellation token, so a cancelled attempt closes whatever
	/// the open produced instead of pretending it never happened.
	/// </summary>
	private static async Task<SerialPortLink> OpenAsync(SerialOpenRequest request, CancellationToken cancellationToken)
	{
		Task<SerialPortLink> opening = Task.Run(() => SerialPortLink.Open(request), CancellationToken.None);
		try
		{
			return await opening.WaitAsync(cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			_ = CloseWhenOpenedAsync(opening);
			throw;
		}
		catch (Exception ex) when (ex is not ProtocolConnectException)
		{
			throw SerialConnectErrors.Describe(ex, request.PortName, request.Line);
		}
	}

	private static async Task CloseWhenOpenedAsync(Task<SerialPortLink> opening)
	{
		try
		{
			SerialPortLink link = await opening;
			await link.DisposeAsync();
		}
		catch (Exception)
		{
			// The open failed on its own, so there is nothing left to close.
		}
	}
}
