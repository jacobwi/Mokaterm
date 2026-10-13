using Mokaterm.Modules.Ssh.Forwarding;
using Renci.SshNet;

namespace Mokaterm.Modules.Ssh.KeepAlive;

/// <summary>
/// Keepalive for one SSH.NET client, as sessions and companion connections set it up: the client's
/// <see cref="GlobalRequestLine"/>, which the tunnel manager goes through too, and the <see cref="KeepAliveMonitor"/>
/// that judges the connection. Either may be missing, which only means a dead connection is noticed as late as before.
/// </summary>
internal sealed class ConnectionKeepAlive : IConnectionWatch
{
	private static readonly Task<KeepAliveTimeoutException> Never = new TaskCompletionSource<KeepAliveTimeoutException>().Task;

	private readonly KeepAliveMonitor? _monitor;

	private ConnectionKeepAlive(GlobalRequestLine? requests, KeepAliveMonitor? monitor, string? problem)
	{
		Requests = requests;
		_monitor = monitor;
		Problem = problem;
	}

	/// <summary>The client's request line, or null when SSH.NET's session could not be reached.</summary>
	public GlobalRequestLine? Requests { get; }

	/// <summary>How the tunnel manager sends forward requests: through the line whenever there is one.</summary>
	public IForwardRequests Forwards => Requests is { } requests ? requests : DirectForwardRequests.Instance;

	/// <summary>Why keepalives that want a reply do not run although they are on, or null.</summary>
	public string? Problem { get; }

	public Task<KeepAliveTimeoutException> Lost => _monitor?.Lost ?? Never;

	/// <param name="policy">Null or off keeps the line, for forward requests, and judges nothing.</param>
	/// <param name="settleLimit">How long a forward start waits for earlier requests to be answered.</param>
	/// <param name="hold">What may hold up this connection's message loop, if anything does.</param>
	/// <param name="notAnswering">
	/// Hears, once, that the server answered none of its keepalives, so the connection is kept and never judged.
	/// </param>
	public static ConnectionKeepAlive Open(
		BaseClient client,
		KeepAlivePolicy? policy,
		TimeSpan settleLimit,
		TimeProvider time,
		IReceiveHold? hold = null,
		Action? notAnswering = null)
	{
		ArgumentNullException.ThrowIfNull(client);
		GlobalRequestLine? requests = GlobalRequestLine.Open(client, settleLimit, time, hold, out string? problem);
		if (policy is not { IsOn: true })
		{
			return new ConnectionKeepAlive(requests, null, null);
		}

		problem ??= SshNetInternals.KeepAliveProblem;
		KeepAliveMonitor? monitor = requests is not null && problem is null ? new KeepAliveMonitor(requests, policy, time, hold) : null;
		if (monitor is not null && requests is not null && notAnswering is not null)
		{
			_ = ReportAsync(requests.StoppedAsking, notAnswering);
		}

		return new ConnectionKeepAlive(requests, monitor, problem);
	}

	/// <summary>Stops the loop and closes the line. Forward stops still run afterwards, uncounted.</summary>
	public async ValueTask DisposeAsync()
	{
		if (_monitor is not null)
		{
			await _monitor.DisposeAsync();
		}

		Requests?.Dispose();
	}

	private static async Task ReportAsync(Task stoppedAsking, Action notAnswering)
	{
		await stoppedAsking;
		try
		{
			notAnswering();
		}
		catch (Exception)
		{
			// Only a log line: nothing waits for it, and nothing it throws may reach the pool.
		}
	}
}
