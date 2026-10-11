using System.Text;
using Mokaterm.Abstractions.Commands;

namespace Mokaterm.Sessions.Tests.Fakes;

/// <summary>Runs commands the way a test says, records every request, and counts how many run at once.</summary>
internal sealed class FakeCommandFeature : IRemoteCommandFeature
{
	private readonly Lock _lock = new();
	private readonly List<RemoteCommandRequest> _requests = [];
	private int _running;
	private int _mostAtOnce;

	public Func<RemoteCommandRequest, CancellationToken, Task<RemoteCommandOutcome>> OnRun { get; set; } =
		(_, _) => Task.FromResult(Outcome(0, "ok\n"));

	public IReadOnlyList<RemoteCommandRequest> Requests
	{
		get
		{
			lock (_lock)
			{
				return [.. _requests];
			}
		}
	}

	public int Running => Volatile.Read(ref _running);

	public int MostAtOnce => Volatile.Read(ref _mostAtOnce);

	public static RemoteCommandOutcome Outcome(int exitCode, string output, string error = "") => new()
	{
		ExitCode = exitCode,
		Output = Captured(output),
		Error = Captured(error),
		Duration = TimeSpan.FromMilliseconds(250),
	};

	public static CapturedOutput Captured(string text)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(text);
		return new CapturedOutput(bytes, bytes.Length);
	}

	public async Task<RemoteCommandOutcome> RunAsync(RemoteCommandRequest request, CancellationToken cancellationToken)
	{
		lock (_lock)
		{
			_requests.Add(request);
			_running++;
			_mostAtOnce = Math.Max(_mostAtOnce, _running);
		}

		try
		{
			return await OnRun(request, cancellationToken);
		}
		finally
		{
			lock (_lock)
			{
				_running--;
			}
		}
	}
}
