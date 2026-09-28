using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Sessions.Tests.Fakes;

internal sealed class FakeProtocolProvider : IProtocolProvider
{
	private readonly Lock _lock = new();
	private readonly List<ProtocolConnectContext> _contexts = [];
	private readonly List<CancellationToken> _tokens = [];
	private readonly List<FakeProtocolSession> _sessions = [];

	public FakeProtocolProvider(ProtocolDescriptor descriptor)
	{
		Descriptor = descriptor;
		OnConnect = (_, _) => Task.FromResult<IProtocolSession>(CreateSession());
	}

	public ProtocolDescriptor Descriptor { get; }

	/// <summary>What a connect does. By default it returns a new session, with a terminal channel when the protocol has one.</summary>
	public Func<ProtocolConnectContext, CancellationToken, Task<IProtocolSession>> OnConnect { get; set; }

	public int ConnectCount
	{
		get
		{
			lock (_lock)
			{
				return _contexts.Count;
			}
		}
	}

	public IReadOnlyList<ProtocolConnectContext> Contexts
	{
		get
		{
			lock (_lock)
			{
				return [.. _contexts];
			}
		}
	}

	public IReadOnlyList<CancellationToken> Tokens
	{
		get
		{
			lock (_lock)
			{
				return [.. _tokens];
			}
		}
	}

	/// <summary>Sessions made by <see cref="CreateSession"/>, oldest first.</summary>
	public IReadOnlyList<FakeProtocolSession> Sessions
	{
		get
		{
			lock (_lock)
			{
				return [.. _sessions];
			}
		}
	}

	public FakeProtocolSession CreateSession()
	{
		FakeProtocolSession session = new(Descriptor.Has(ProtocolCapabilities.Terminal) ? new FakeTerminalChannel() : null);
		lock (_lock)
		{
			_sessions.Add(session);
		}

		return session;
	}

	public Task<IProtocolSession> ConnectAsync(ProtocolConnectContext context, CancellationToken cancellationToken)
	{
		lock (_lock)
		{
			_contexts.Add(context);
			_tokens.Add(cancellationToken);
		}

		return OnConnect(context, cancellationToken);
	}
}
