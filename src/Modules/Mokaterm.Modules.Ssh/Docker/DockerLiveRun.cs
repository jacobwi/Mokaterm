using System.Globalization;
using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// One build, push or compose build while it runs and after: its output as lines (<see cref="Lines"/>), how it ended,
/// and a way to stop it. It belongs to the session's <see cref="DockerLiveRuns"/>, not to a page, so switching pages or
/// closing the manager leaves it running and the page shows it again when it comes back.
/// </summary>
internal sealed class DockerLiveRun
{
	private readonly CancellationTokenSource _cancel;
	private readonly TimeProvider _time;
	private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private int _state = (int)DockerLiveState.Running;
	private int _stoppedByUser;

	/// <param name="shown">Every call as the question showed it, which is what the run shows too.</param>
	/// <param name="lifetime">Cancelled when the session goes, which stops the run.</param>
	internal DockerLiveRun(DockerLivePlan plan, IReadOnlyList<string> shown, TimeProvider time, CancellationToken lifetime)
	{
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentNullException.ThrowIfNull(shown);
		ArgumentNullException.ThrowIfNull(time);
		Plan = plan;
		Shown = shown;
		_time = time;
		_cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
		Lines = new DockerOutputLines(plan.ErrorIsProgress);
		StartedAt = time.GetUtcNow();
	}

	/// <summary>Raised when the state changes, not per line. Handlers may run on any thread.</summary>
	public event Action? Changed;

	public DockerLivePlan Plan { get; }

	public string Title => Plan.Title;

	public string Owner => Plan.Owner;

	public IReadOnlyList<string> Shown { get; }

	public DockerOutputLines Lines { get; }

	public DockerLiveState State => (DockerLiveState)Volatile.Read(ref _state);

	public bool IsRunning => State == DockerLiveState.Running;

	public DateTimeOffset StartedAt { get; }

	/// <summary>How long it ran, once it ended.</summary>
	public TimeSpan? Duration { get; private set; }

	/// <summary>The last call's exit, once one ended.</summary>
	public int? ExitCode { get; private set; }

	/// <summary>Why it did not succeed, in a sentence; null while it runs and after a success.</summary>
	public string? Problem { get; private set; }

	/// <summary>Completes when the run ended, however it ended. It never faults.</summary>
	public Task Completion => _completion.Task;

	/// <summary>What the run's tag says: <c>RUNNING</c>, <c>EXIT 0</c>, <c>STOPPED</c>.</summary>
	public string StateText => State switch
	{
		DockerLiveState.Running => "running",
		DockerLiveState.Stopped => "stopped",
		DockerLiveState.Lost => "did not finish",
		_ => ExitCode is { } code ? string.Create(CultureInfo.InvariantCulture, $"exit {code}") : "no exit status",
	};

	/// <summary>Stops the run: the call's channel closes, which ends the docker client and, with it, the build.</summary>
	public void Cancel()
	{
		if (!IsRunning)
		{
			return;
		}

		Volatile.Write(ref _stoppedByUser, 1);
		try
		{
			_cancel.Cancel();
		}
		catch (ObjectDisposedException)
		{
			// It ended between the check and the cancel.
		}
	}

	/// <summary>Runs every call in turn through <paramref name="client"/>, its output into <see cref="Lines"/>.</summary>
	internal async Task RunAsync(DockerClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		DockerLiveState ended = DockerLiveState.Succeeded;
		try
		{
			foreach (DockerCall call in Plan.Calls)
			{
				DockerOutput output = await client.RunAsync(call with { OnOutput = Lines.Write }, _cancel.Token);
				ExitCode = output.ExitCode;
				if (!output.Succeeded)
				{
					ended = DockerLiveState.Failed;
					Problem = FailureText(client, call, output);
					break;
				}
			}
		}
		catch (OperationCanceledException) when (Volatile.Read(ref _stoppedByUser) != 0)
		{
			ended = DockerLiveState.Stopped;
			Problem = "Stopped. Closing the command's channel ends the docker client, and with it the build or the push; what a push already sent stays in the registry.";
		}
		catch (OperationCanceledException)
		{
			ended = DockerLiveState.Lost;
			Problem = "The session closed while it ran.";
		}
		catch (DockerOperationException ex)
		{
			ended = DockerLiveState.Lost;
			Problem = ex.Message;
		}
		finally
		{
			Lines.Complete();
			Duration = _time.GetUtcNow() - StartedAt;
			Volatile.Write(ref _state, (int)ended);
			_cancel.Dispose();
			Changed?.Invoke();
			_completion.TrySetResult();
		}
	}

	/// <summary>Why a call that ran failed: its time ran out on the server, or the last line that reads as an error.</summary>
	private string FailureText(DockerClient client, DockerCall call, DockerOutput output)
	{
		if (DockerTimeLimit.RanOut(output.ExitCode) && DockerTimeLimit.LimitOf(call) is { } limit)
		{
			return $"It ran past the {DockerTimeLimit.Describe(limit)} the server gives it, so the server stopped it.";
		}

		if (output.ExitCode is null && output.ExitSignal is { Length: > 0 } signal)
		{
			return $"docker ended on signal {signal}.";
		}

		return Lines.LastError() ?? client.Failed(output).Message;
	}
}
