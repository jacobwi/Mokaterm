using System.Text;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Telnet.Protocol;
using Mokaterm.Modules.Telnet.Sessions;

namespace Mokaterm.Modules.Telnet;

/// <summary>Opens telnet sessions: the socket, the option negotiation and the terminal all run here.</summary>
internal sealed class TelnetProtocolProvider : IProtocolProvider
{
	public const string ProtocolId = "telnet";

	public const int DefaultPort = 23;

	/// <summary>Names offered to a server that does not know the first one. RFC 1091 walks the list with SEND.</summary>
	private static readonly string[] FallbackTerminalTypes = ["xterm", "vt100", "ansi"];

	private readonly ISettingsService _settings;
	private readonly TimeProvider _timeProvider;
	private readonly ILoggerFactory _loggerFactory;
	private readonly ILogger _logger;

	public TelnetProtocolProvider(ISettingsService settings, TimeProvider timeProvider, ILoggerFactory loggerFactory)
	{
		_settings = settings;
		_timeProvider = timeProvider;
		_loggerFactory = loggerFactory;
		_logger = loggerFactory.CreateLogger<TelnetProtocolProvider>();
	}

	public ProtocolDescriptor Descriptor { get; } = new()
	{
		Id = ProtocolId,
		DisplayName = "Telnet",
		Description = "A terminal over plain TCP. Telnet has no encryption and no login of its own: the prompt is part of the stream.",
		DefaultPort = DefaultPort,
		Capabilities = ProtocolCapabilities.Terminal,

		// Anonymous first, and the default: nothing is sent before the terminal opens. A saved password is only
		// used by connections that turn the automatic login on.
		AuthenticationMethods = [AuthenticationMethod.Anonymous, AuthenticationMethod.Password],
		RequiresUsername = false,
		Order = 40,
	};

	public async Task<IProtocolSession> ConnectAsync(ProtocolConnectContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		TelnetSettings settings = _settings.Get<TelnetSettings>().Clamped();
		TelnetConnectionOptions options = TelnetConnectionOptions.From(context.Connection.Options);
		if (!TerminalEncodings.TryGet(options.EncodingName, out Encoding? encoding))
		{
			_logger.LogWarning("Unknown telnet character set {Encoding}; using UTF-8", options.EncodingName);
			encoding = Encoding.UTF8;
		}

		TelnetAutoLogin? autoLogin = options.AutoLogin ? await CreateAutoLoginAsync(context, cancellationToken) : null;
		TelnetConnector connector = new(context.Host.Address, context.Port, settings.ConnectTimeout);
		TelnetTransport? transport = null;
		TelnetTerminalChannel? channel = null;
		try
		{
			transport = await connector.ConnectAsync(context.Status, cancellationToken);
			channel = new TelnetTerminalChannel(
				transport.Stream,
				new TelnetChannelOptions
				{
					TerminalTypes = TerminalTypes(options),
					Encoding = encoding,
					LineEnding = options.LineEnding,
					Echo = options.Echo,
					Size = context.TerminalSize,
					KeepAlive = settings.KeepAlive,
					AutoLoginTimeout = settings.AutoLoginTimeout,
					AutoLogin = autoLogin,
					TimeProvider = _timeProvider,
				},
				_loggerFactory.CreateLogger<TelnetTerminalChannel>());

			// The channel owns the login from here: it wipes the password once it has been typed.
			autoLogin = null;

			context.Status?.Report("Negotiating options");
			await channel.StartAsync(cancellationToken);
			if (!await channel.WaitForNegotiationAsync(settings.NegotiationTimeout, cancellationToken))
			{
				// Serial consoles and other small devices answer nothing at all, which is allowed.
				_logger.LogDebug("Telnet session {SessionId} got no answer to its opening negotiation.", context.SessionId);
			}

			TelnetSession session = new(context.SessionId, transport, channel, _loggerFactory.CreateLogger<TelnetSession>());
			transport = null;
			channel = null;
			return session;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex) when (ex is not ProtocolConnectException)
		{
			throw await TelnetConnectErrors.DescribeAsync(ex, context.Host.Address, context.Port, timedOut: false, cancellationToken);
		}
		finally
		{
			autoLogin?.Dispose();
			if (channel is not null)
			{
				await channel.DisposeAsync();
			}

			if (transport is not null)
			{
				await transport.DisposeAsync();
			}
		}
	}

	/// <summary>
	/// The connection's own terminal type, then the one the terminal settings already send over SSH, then names
	/// every server knows.
	/// </summary>
	private List<string> TerminalTypes(TelnetConnectionOptions options)
	{
		List<string> types = [];
		if (TelnetConnectionOptions.IsValidTerminalType(options.TerminalType))
		{
			types.Add(options.TerminalType);
		}

		types.Add(_settings.Get<TerminalSettings>().TerminalType);
		types.AddRange(FallbackTerminalTypes);
		return types;
	}

	/// <summary>
	/// Reads the saved login for a connection that types it at the prompt. Nothing is sent before the server asks,
	/// and the password is kept in pinned memory until it is typed.
	/// </summary>
	private static async Task<TelnetAutoLogin?> CreateAutoLoginAsync(ProtocolConnectContext context, CancellationToken cancellationToken)
	{
		if (context.Credentials.Method is AuthenticationMethod.PublicKey or AuthenticationMethod.Agent)
		{
			throw new ProtocolConnectException(
				ConnectFailure.AuthenticationFailed,
				"Telnet types a user name and a password at the server's own prompt; key based logins are not part of the protocol.");
		}

		using LoginCredentials credentials = await context.Credentials.GetAsync(cancellationToken)
			?? throw new ProtocolConnectException(ConnectFailure.Cancelled, "The login was cancelled.");

		// An anonymous login has no saved name of its own, and the placeholder one would be typed at the prompt.
		string? username = context.Credentials.Method == AuthenticationMethod.Anonymous
			? NullIfBlank(context.Connection.Username)
			: NullIfBlank(credentials.Username);

		SecretBuffer? password = credentials.Password?.Copy();
		return username is null && password is null ? null : new TelnetAutoLogin(username, password);
	}

	private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
