using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Modules.Ssh.Agent;
using Mokaterm.Modules.Ssh.Keys;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Messages.Connection;

namespace Mokaterm.Modules.Ssh.Forwarding;

/// <summary>
/// Agent and X11 forwarding for one SSH session, which SSH.NET does not offer. Two things are missing there: a request
/// on the shell's channel before the shell starts, and accepting channels the server opens. The first is sent from the
/// moment the server confirms the shell's channel, which is before SSH.NET can send the shell request, because both
/// arrive on SSH.NET's message loop in order. The second listens to the session's public channel events and answers with
/// channels of its own (<see cref="SshForwardedChannel"/>). Only the target of a session forwards: jump hosts never open
/// a shell, and every channel here closes with the session.
/// </summary>
/// <remarks>
/// When both an agent and a saved key could answer, the agent on this machine wins, because that is what
/// <c>ssh -A</c> forwards and where the user manages keys and confirmations; the login's saved key is served only when
/// no agent answers, so a login with a saved key works onward without running one.
/// </remarks>
internal sealed class SshForwarding : ISshForwardingFeature, IAsyncDisposable
{
	public const string AgentChannelType = "auth-agent@openssh.com";

	public const string X11ChannelType = "x11";

	/// <summary>
	/// Our numbers for forwarded channels start here. SSH.NET counts its own up from zero and never looks at a message
	/// addressed to a number it did not hand out, so the two cannot meet.
	/// </summary>
	internal const uint FirstChannelNumber = 0xC000_0000;

	internal const int MaxAgentChannels = 16;

	internal const int MaxX11Channels = 64;

	/// <summary>How far the server may send ahead of what a relay has read, per channel.</summary>
	internal const uint WindowBytes = 256 * 1024;

	internal const uint PacketBytes = 32 * 1024;

	// Channel open failure reasons, RFC 4254 section 5.1.
	private const uint AdministrativelyProhibited = 1;

	private const uint ConnectFailed = 2;

	private const uint ResourceShortage = 4;

	private const int CookieBytes = 16;

	private static readonly TimeSpan XServerConnectTimeout = TimeSpan.FromSeconds(5);

	/// <summary>How long closing waits for relays that are still finishing; past that they end on their own.</summary>
	private static readonly TimeSpan StopWait = TimeSpan.FromSeconds(2);

	private readonly Session? _session;
	private readonly ISshMessageSender? _sender;
	private readonly SshForwardingSetup _setup;
	private readonly ILogger _logger;
	private readonly ConcurrentDictionary<uint, SshForwardedChannel> _channels = new();
	private readonly List<Task> _work = [];
	private readonly CancellationTokenSource _stopping = new();
	private readonly CancellationToken _stoppingToken;
	private readonly Lock _lock = new();
	private readonly byte[] _fakeCookie = RandomNumberGenerator.GetBytes(CookieBytes);
	private readonly byte[]? _realCookie;
	private readonly X11Display _display;
	private readonly bool _agentWanted;
	private readonly bool _x11Wanted;
	private SshForwardingLine _agent;
	private SshForwardingLine _x11;
	private KeychainAgent? _keychain;
	private bool _keychainLoaded;
	private bool _agentRequested;
	private bool _x11Requested;
	private int _nextChannel;
	private int _shellWatch;
	private int _agentOpen;
	private int _x11Open;
	private int _noAgentReported;
	private int _noXServerReported;
	private int _disposed;

	/// <param name="session">The session to listen on, or null when SSH.NET would not hand it over.</param>
	/// <param name="sender">How messages are written, or null when SSH.NET would not allow it.</param>
	internal SshForwarding(Session? session, ISshMessageSender? sender, SshForwardingSetup setup, ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(setup);
		ArgumentNullException.ThrowIfNull(logger);
		_session = session;
		_sender = sender;
		_setup = setup;
		_logger = logger;
		_stoppingToken = _stopping.Token;
		_display = X11Display.Default;

		string? sessionProblem = session is null || sender is null
			? SshNetInternals.SessionProblem ?? "SSH.NET did not hand over the session."
			: null;

		SshForwardingOptions options = setup.Options;
		_agent = SshForwardingLine.Off;
		if (options.ForwardAgent)
		{
			string? problem = sessionProblem
				?? SshNetInternals.UnknownChannelTypesProblem
				?? (setup.OffersSystemAgent || setup.HasSavedKey ? null : "This login uses no saved key, and on the web host the server's own agent is never forwarded.");
			_agentWanted = problem is null;
			_agent = new SshForwardingLine(SshForwardingState.Unavailable, AgentTarget, 0, problem ?? "Not asked for yet.");
		}

		_x11 = SshForwardingLine.Off;
		if (options.ForwardX11)
		{
			string? problem = !setup.OffersX11
				? "X11 forwarding needs the desktop app: on the web host the X server would have to run on the server."
				: sessionProblem;
			string target = options.Display ?? X11Display.DefaultText;
			if (problem is null)
			{
				if (X11Display.TryParse(options.Display, out X11Display display, out string? error))
				{
					_display = display;
					target = $"{display} ({display.DescribeEndPoint()})";
				}
				else
				{
					problem = $"The display {options.Display} cannot be used. {error}";
				}
			}

			_x11Wanted = problem is null;
			_x11 = new SshForwardingLine(SshForwardingState.Unavailable, target, 0, problem ?? "Not asked for yet.");
			if (_x11Wanted)
			{
				_realCookie = setup.FindRealCookie(_display);
			}
		}

		if (_session is not null && (_agentWanted || _x11Wanted))
		{
			_session.ChannelOpenReceived += OnChannelOpen;
			_session.ChannelDataReceived += OnChannelData;
			_session.ChannelWindowAdjustReceived += OnChannelWindowAdjust;
			_session.ChannelEofReceived += OnChannelEof;
			_session.ChannelCloseReceived += OnChannelClose;
			_session.ChannelRequestReceived += OnChannelRequest;
			_session.Disconnected += OnDisconnected;
			_session.ErrorOccured += OnSessionError;
		}
	}

	public event Action? Changed;

	public SshForwardingLine Agent
	{
		get
		{
			lock (_lock)
			{
				return _agent with { OpenChannels = Volatile.Read(ref _agentOpen) };
			}
		}
	}

	public SshForwardingLine X11
	{
		get
		{
			lock (_lock)
			{
				return _x11 with { OpenChannels = Volatile.Read(ref _x11Open) };
			}
		}
	}

	/// <summary>True when anything is being asked for, which is when the shell's channel has to be watched.</summary>
	public bool IsWanted => _agentWanted || _x11Wanted;

	private string AgentTarget => _setup.OffersSystemAgent
		? _setup.HasSavedKey ? "Your SSH agent, or this login's saved key when no agent answers" : "Your SSH agent"
		: "This login's saved key";

	/// <summary>Starts forwarding for a connected client. Null when the connection asks for none.</summary>
	public static SshForwarding? Start(BaseClient client, SshForwardingSetup setup, ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(client);
		ArgumentNullException.ThrowIfNull(setup);
		if (!setup.Options.ForwardAgent && !setup.Options.ForwardX11)
		{
			return null;
		}

		Session? session = SshNetInternals.GetSession(client);
		ISshMessageSender? sender = session is null ? null : SshNetInternals.CreateSender(session);
		return new SshForwarding(session, sender, setup, logger);
	}

	/// <summary>
	/// Call right before the shell's channel is opened. The first channel confirmation after this is the shell's, since
	/// nothing else opens a channel on a session that has no shell yet, and the requests go out from inside it.
	/// </summary>
	public void BeginShellOpen()
	{
		if (_session is null || !IsWanted || Volatile.Read(ref _disposed) != 0)
		{
			return;
		}

		Volatile.Write(ref _shellWatch, 1);
		_session.ChannelOpenConfirmationReceived += OnShellChannelConfirmed;
	}

	/// <summary>Call once the shell opened, or failed to. Tells the user once about anything that could not be set up.</summary>
	public void EndShellOpen(bool opened)
	{
		if (_session is not null)
		{
			_session.ChannelOpenConfirmationReceived -= OnShellChannelConfirmed;
		}

		bool missed = Interlocked.Exchange(ref _shellWatch, 0) == 1;
		if (!opened)
		{
			return;
		}

		if (missed)
		{
			lock (_lock)
			{
				_agent = Unavailable(_agent, _agentWanted, "The request could not be sent with the shell.");
				_x11 = Unavailable(_x11, _x11Wanted, "The request could not be sent with the shell.");
			}
		}

		List<string> problems = [];
		lock (_lock)
		{
			if (_agent is { State: SshForwardingState.Unavailable, Problem: { } agentProblem })
			{
				problems.Add($"Agent forwarding: {agentProblem}");
			}

			if (_x11 is { State: SshForwardingState.Unavailable, Problem: { } x11Problem })
			{
				problems.Add($"X11 forwarding: {x11Problem}");
			}
		}

		if (problems.Count > 0)
		{
			_logger.LogInformation("SSH session {SessionId} runs without some of the forwarding it asked for", _setup.SessionId);
			_setup.Interaction?.Notify(NoticeSeverity.Warning, string.Join(Environment.NewLine, problems), "Forwarding is not available");
		}

		RaiseChanged();
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		if (_session is not null)
		{
			_session.ChannelOpenConfirmationReceived -= OnShellChannelConfirmed;
			_session.ChannelOpenReceived -= OnChannelOpen;
			_session.ChannelDataReceived -= OnChannelData;
			_session.ChannelWindowAdjustReceived -= OnChannelWindowAdjust;
			_session.ChannelEofReceived -= OnChannelEof;
			_session.ChannelCloseReceived -= OnChannelClose;
			_session.ChannelRequestReceived -= OnChannelRequest;
			_session.Disconnected -= OnDisconnected;
			_session.ErrorOccured -= OnSessionError;
		}

		foreach (SshForwardedChannel channel in _channels.Values)
		{
			channel.Close();
		}

		await _stopping.CancelAsync();
		Task[] work;
		lock (_lock)
		{
			work = [.. _work];
		}

		try
		{
			await Task.WhenAll(work).WaitAsync(StopWait);
		}
		catch (Exception)
		{
			// Each relay logs its own end; one that is slow to stop finishes on its own.
		}

		lock (_lock)
		{
			_keychainLoaded = true;
			_keychain?.Dispose();
			_keychain = null;
		}

		_channels.Clear();
		CryptographicOperations.ZeroMemory(_fakeCookie);
		if (_realCookie is not null)
		{
			CryptographicOperations.ZeroMemory(_realCookie);
		}

		_stopping.Dispose();
	}

	private static SshForwardingLine Unavailable(SshForwardingLine line, bool wanted, string problem) =>
		wanted && line.State == SshForwardingState.Unavailable ? line with { Problem = problem } : line;

	// Runs on SSH.NET's message loop, where an exception would end the connection.
	private void OnShellChannelConfirmed(object? sender, MessageEventArgs<ChannelOpenConfirmationMessage> e)
	{
		if (Interlocked.Exchange(ref _shellWatch, 0) == 0 || _sender is not { } writer)
		{
			return;
		}

		try
		{
			uint shell = e.Message.RemoteChannelNumber;
			bool agent = _agentWanted && writer.TrySend(new ChannelRequestMessage(shell, new AgentForwardingRequest()));
			bool x11 = _x11Wanted && writer.TrySend(new ChannelRequestMessage(shell, new X11ForwardingRequest(_fakeCookie, _display.Screen)));
			Volatile.Write(ref _agentRequested, agent);
			Volatile.Write(ref _x11Requested, x11);
			lock (_lock)
			{
				_agent = Requested(_agent, _agentWanted, agent);
				_x11 = Requested(_x11, _x11Wanted, x11);
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning("SSH session {SessionId} could not ask for forwarding: {Error}", _setup.SessionId, LogSafe.Describe(ex));
		}

		static SshForwardingLine Requested(SshForwardingLine line, bool wanted, bool sent) =>
			!wanted ? line
			: sent ? line with { State = SshForwardingState.Requested, Problem = null }
			: line with { Problem = "The connection closed before the request could be sent." };
	}

	private void OnChannelOpen(object? sender, MessageEventArgs<ChannelOpenMessage> e)
	{
		try
		{
			ChannelOpenMessage open = e.Message;
			string type = Encoding.ASCII.GetString(open.ChannelType);
			bool agent = string.Equals(type, AgentChannelType, StringComparison.Ordinal);
			if (!agent && !string.Equals(type, X11ChannelType, StringComparison.Ordinal))
			{
				// Not ours: SSH.NET's remote tunnels answer forwarded-tcpip themselves.
				return;
			}

			uint remote = open.LocalChannelNumber;
			uint window = open.InitialWindowSize;
			uint packet = open.MaximumPacketSize;
			bool requested = agent ? Volatile.Read(ref _agentRequested) : Volatile.Read(ref _x11Requested);
			if (!requested || Volatile.Read(ref _disposed) != 0)
			{
				Track(() => Refuse(remote, AdministrativelyProhibited, agent ? "agent forwarding is off" : "X11 forwarding is off"));
				return;
			}

			Track(agent ? () => AcceptAgentAsync(remote, window, packet) : () => AcceptX11Async(remote, window, packet));
		}
		catch (Exception ex)
		{
			_logger.LogWarning("SSH session {SessionId} could not take a forwarded channel: {Error}", _setup.SessionId, LogSafe.Describe(ex));
		}
	}

	private void OnChannelData(object? sender, MessageEventArgs<ChannelDataMessage> e)
	{
		try
		{
			ChannelDataMessage message = e.Message;
			if (Find(message.LocalChannelNumber) is { } channel && message.Data is { } data)
			{
				channel.OnData(new ReadOnlySpan<byte>(data, message.Offset, message.Size));
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning("SSH session {SessionId} could not take forwarded data: {Error}", _setup.SessionId, LogSafe.Describe(ex));
		}
	}

	private void OnChannelWindowAdjust(object? sender, MessageEventArgs<ChannelWindowAdjustMessage> e) =>
		Find(e.Message.LocalChannelNumber)?.OnWindowAdjust(e.Message.BytesToAdd);

	private void OnChannelEof(object? sender, MessageEventArgs<ChannelEofMessage> e) =>
		Find(e.Message.LocalChannelNumber)?.OnEof();

	private void OnChannelClose(object? sender, MessageEventArgs<ChannelCloseMessage> e) =>
		Find(e.Message.LocalChannelNumber)?.OnClose();

	private void OnChannelRequest(object? sender, MessageEventArgs<ChannelRequestMessage> e)
	{
		ChannelRequestMessage message = e.Message;
		if (Find(message.LocalChannelNumber) is { } channel)
		{
			// The reply flag is the first byte after the request name.
			channel.OnRequest(message.RequestData is { Length: > 0 } data && data[0] != 0);
		}
	}

	private void OnDisconnected(object? sender, EventArgs e) => LoseChannels();

	private void OnSessionError(object? sender, ExceptionEventArgs e) => LoseChannels();

	private void LoseChannels()
	{
		foreach (SshForwardedChannel channel in _channels.Values)
		{
			channel.OnConnectionLost();
		}
	}

	private SshForwardedChannel? Find(uint number) =>
		number >= FirstChannelNumber && _channels.TryGetValue(number, out SshForwardedChannel? channel) ? channel : null;

	private void Track(Func<Task> work)
	{
		Task task = Task.Run(work, CancellationToken.None);
		lock (_lock)
		{
			_ = _work.RemoveAll(running => running.IsCompleted);
			_work.Add(task);
		}
	}

	private Task Refuse(uint remote, uint reason, string description)
	{
		_ = _sender?.TrySend(new ChannelOpenFailureMessage(remote, description, reason));
		return Task.CompletedTask;
	}

	/// <summary>Registers a channel before accepting it, so data that follows the confirmation finds it.</summary>
	private SshForwardedChannel? OpenChannel(uint remote, uint window, uint packet)
	{
		if (_sender is not { } writer)
		{
			return null;
		}

		uint local = FirstChannelNumber | ((uint)Interlocked.Increment(ref _nextChannel) & 0x3FFF_FFFF);
		SshForwardedChannel channel = new(writer, local, remote, window, packet, WindowBytes, PacketBytes);
		_channels[local] = channel;
		if (Volatile.Read(ref _disposed) == 0 && channel.Confirm())
		{
			return channel;
		}

		_ = _channels.TryRemove(local, out _);
		channel.OnConnectionLost();
		return null;
	}

	private void CloseChannel(SshForwardedChannel channel)
	{
		channel.Close();
		_ = _channels.TryRemove(channel.LocalNumber, out _);
	}

	private async Task AcceptX11Async(uint remote, uint window, uint packet)
	{
		if (!TryReserve(ref _x11Open, MaxX11Channels))
		{
			await Refuse(remote, ResourceShortage, "too many X11 connections");
			return;
		}

		Socket? server = null;
		SshForwardedChannel? channel = null;
		try
		{
			server = await ConnectXServerAsync();
			if (server is null)
			{
				await Refuse(remote, ConnectFailed, "X11 connect failed");
				ReportNoXServer();
				return;
			}

			channel = OpenChannel(remote, window, packet);
			if (channel is null)
			{
				return;
			}

			RaiseChanged();
			string? refusal = await X11Relay.RunAsync(channel, server, _fakeCookie, _realCookie, _stoppingToken);
			if (refusal is not null && !_stoppingToken.IsCancellationRequested)
			{
				_logger.LogInformation("SSH session {SessionId} turned an X11 connection away: {Reason}", _setup.SessionId, refusal);
			}
		}
		catch (Exception ex)
		{
			_logger.LogDebug("SSH session {SessionId} ended an X11 connection: {Error}", _setup.SessionId, LogSafe.Describe(ex));
		}
		finally
		{
			if (channel is not null)
			{
				CloseChannel(channel);
			}

			server?.Dispose();
			_ = Interlocked.Decrement(ref _x11Open);
			RaiseChanged();
		}
	}

	private async Task<Socket?> ConnectXServerAsync()
	{
		EndPoint endPoint = _display.EndPoint;
		Socket socket = endPoint is IPEndPoint ip
			? new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
			: new Socket(SocketType.Stream, ProtocolType.Tcp);

		// X11 is a stream of small requests answered one by one; OpenSSH turns Nagle off for it too.
		socket.NoDelay = true;
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken);
		timeout.CancelAfter(XServerConnectTimeout);
		try
		{
			await socket.ConnectAsync(endPoint, timeout.Token);
			return socket;
		}
		catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException)
		{
			socket.Dispose();
			return null;
		}
	}

	private async Task AcceptAgentAsync(uint remote, uint window, uint packet)
	{
		if (!TryReserve(ref _agentOpen, MaxAgentChannels))
		{
			await Refuse(remote, ResourceShortage, "too many agent connections");
			return;
		}

		IDisposable? owned = null;
		SshForwardedChannel? channel = null;
		try
		{
			(IForwardedAgent? agent, owned) = OpenAgent();
			if (agent is null)
			{
				await Refuse(remote, ConnectFailed, "no agent is available");
				ReportNoAgent();
				return;
			}

			channel = OpenChannel(remote, window, packet);
			if (channel is null)
			{
				return;
			}

			RaiseChanged();
			using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken);
			Task relay = AgentRelay.RunAsync(channel, agent, stop.Token);
			if (await Task.WhenAny(relay, channel.Closed) != relay)
			{
				// The server closed the channel while the agent was still thinking; its answer has nowhere to go.
				await stop.CancelAsync();
			}

			await relay;
		}
		catch (Exception ex)
		{
			_logger.LogDebug("SSH session {SessionId} ended an agent connection: {Error}", _setup.SessionId, LogSafe.Describe(ex));
		}
		finally
		{
			if (channel is not null)
			{
				CloseChannel(channel);
			}

			owned?.Dispose();
			_ = Interlocked.Decrement(ref _agentOpen);
			RaiseChanged();
		}
	}

	/// <summary>The agent on this machine when one answers, otherwise the login's saved key, otherwise nothing.</summary>
	private (IForwardedAgent? Agent, IDisposable? Owned) OpenAgent()
	{
		if (_setup.OffersSystemAgent)
		{
			IReadOnlyList<SshAgentAddress> addresses = SshAgentEndpoints.Discover(_setup.AgentEndpoint);
			if (addresses.Count > 0)
			{
				try
				{
					SystemAgentConnection connection = new(SshAgentClient.Connect(addresses, _setup.AgentTimeout));
					return (connection, connection);
				}
				catch (SshAgentException ex)
				{
					_logger.LogDebug("SSH session {SessionId} found no agent for a forwarded channel: {Error}", _setup.SessionId, LogSafe.Describe(ex));
				}
			}
		}

		return (GetKeychainAgent(), null);
	}

	private KeychainAgent? GetKeychainAgent()
	{
		lock (_lock)
		{
			if (_keychainLoaded)
			{
				return _keychain;
			}

			_keychainLoaded = true;
			if (_setup.Credentials is not { Method: Abstractions.Connections.AuthenticationMethod.PublicKey, PrivateKey: { } privateKey } credentials)
			{
				return null;
			}

			// The key decrypted once for the session, as the login did, and wiped when the session closes.
			if (SshPrivateKeys.TryLoad(privateKey.Span, credentials.Passphrase?.RevealString(), out PrivateKeyFile? key, out _) == PrivateKeyStatus.Valid && key is not null)
			{
				_keychain = new KeychainAgent(key);
			}
			else
			{
				key?.Dispose();
				_logger.LogInformation("SSH session {SessionId} could not load its saved key for agent forwarding", _setup.SessionId);
			}

			return _keychain;
		}
	}

	private static bool TryReserve(ref int open, int max)
	{
		while (true)
		{
			int current = Volatile.Read(ref open);
			if (current >= max)
			{
				return false;
			}

			if (Interlocked.CompareExchange(ref open, current + 1, current) == current)
			{
				return true;
			}
		}
	}

	private void ReportNoXServer()
	{
		string where = $"{_display} ({_display.DescribeEndPoint()})";
		lock (_lock)
		{
			_x11 = _x11 with { Problem = $"No X server answered on {where}." };
		}

		if (Interlocked.Exchange(ref _noXServerReported, 1) == 0)
		{
			_setup.Interaction?.Notify(
				NoticeSeverity.Warning,
				$"A program on {_setup.Account} wanted to show a window, but no X server answered on {where}. Start one here, such as VcXsrv, Xming or X410, then run the program again.",
				"X11 forwarding");
		}

		RaiseChanged();
	}

	private void ReportNoAgent()
	{
		string problem = _setup.OffersSystemAgent
			? "No SSH agent answered, and this login has no saved key that could be offered instead."
			: "This login's saved key could not be loaded.";
		lock (_lock)
		{
			_agent = _agent with { Problem = problem };
		}

		if (Interlocked.Exchange(ref _noAgentReported, 1) == 0)
		{
			_setup.Interaction?.Notify(
				NoticeSeverity.Warning,
				$"A program on {_setup.Account} asked for your SSH agent. {problem}",
				"Agent forwarding");
		}

		RaiseChanged();
	}

	private void RaiseChanged()
	{
		try
		{
			Changed?.Invoke();
		}
		catch (Exception ex)
		{
			_logger.LogWarning("A view of SSH session {SessionId}'s forwarding failed: {Error}", _setup.SessionId, LogSafe.Describe(ex));
		}
	}
}
