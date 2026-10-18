using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.ServerAdmin;
using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Monitoring;

namespace Mokaterm.Modules.Ssh.Tests.Admin;

/// <summary>
/// One sampler per session, shared by the System view's overview and the status bar: it reads only while someone
/// watches, as often as the fastest watcher asks, never two reads at once, and a session that cannot give figures stops
/// asking for good.
/// </summary>
public sealed class HostMonitorTests
{
	private static readonly TimeSpan Slow = TimeSpan.FromSeconds(5);

	private static readonly TimeSpan Fast = TimeSpan.FromSeconds(2);

	private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(250);

	private readonly FakeTimeProvider _time = new();

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Nothing_IsRead_UntilSomeoneWatches()
	{
		Reader reader = new(_time, MonitorFixtures.UbuntuFirst);
		await using HostMonitor monitor = Monitor(reader);

		for (int i = 0; i < 8; i++)
		{
			_time.Advance(Slow);
		}

		await Eventually.StaysTrueAsync(() => reader.Count == 0, "no read without a watcher");
		Assert.Same(HostMonitorState.Waiting, monitor.State);
		Assert.Null(monitor.Interval);
	}

	[Fact]
	public async Task TheFirstLease_ReadsAtOnce_TheSecondReadBringsCpu_AndTheLastLeaseStopsTheReads()
	{
		Reader reader = new(_time, MonitorFixtures.UbuntuFirst, MonitorFixtures.UbuntuSecond);
		await using HostMonitor monitor = Monitor(reader);

		IDisposable lease = monitor.Watch(Slow);
		await Eventually.TrueAsync(() => reader.Count == 1, "the first read, at once");
		HostFigures first = await FiguresAsync(monitor);
		Assert.Null(first.CpuPercent);
		Assert.NotNull(first.MemoryPercent);
		Assert.Equal("overview.sample", Assert.Single(reader.Scripts));

		await Eventually.AdvanceUntilAsync(_time, Step, () => monitor.State.Figures?.CpuPercent is not null, "CPU from the second read");
		Assert.Equal(25, monitor.State.Figures!.CpuPercent!.Value, 6);

		lease.Dispose();
		int reads = reader.Count;
		Assert.Equal(0, monitor.Watchers);
		for (int i = 0; i < 6; i++)
		{
			_time.Advance(Slow);
			await Task.Delay(10, Ct);
		}

		await Eventually.StaysTrueAsync(() => reader.Count == reads, "no read once the last lease went");
	}

	[Fact]
	public async Task TheFastestLease_SetsThePace_AndEveryWatcherReadsTheSameSamples()
	{
		Reader reader = new(_time, MonitorFixtures.UbuntuFirst, MonitorFixtures.UbuntuSecond);
		await using HostMonitor monitor = Monitor(reader);
		using IDisposable statusBar = monitor.Watch(Slow);
		await Eventually.TrueAsync(() => reader.Count == 1, "the first read");

		IDisposable overview = monitor.Watch(Fast);
		Assert.Equal(Fast, monitor.Interval);
		Assert.Equal(2, monitor.Watchers);

		// The overview's lease came a moment after a read, so it waits out its own interval rather than reading again.
		await Eventually.AdvanceUntilAsync(_time, Step, () => reader.Count >= 4, "three more reads at the overview's pace");
		IReadOnlyList<DateTimeOffset> times = reader.Times;
		for (int i = 1; i < times.Count; i++)
		{
			TimeSpan gap = times[i] - times[i - 1];
			Assert.InRange(gap, Fast, Slow - Step);
		}

		overview.Dispose();
		Assert.Equal(Slow, monitor.Interval);
		int before = reader.Count;
		await Eventually.AdvanceUntilAsync(_time, Step, () => reader.Count >= before + 2, "two reads at the status bar's pace");
		IReadOnlyList<DateTimeOffset> later = reader.Times;
		Assert.True(later[^1] - later[^2] >= Slow, "the slower lease's own pace once the fast one went");
		Assert.Equal(1, reader.MostAtOnce);
	}

	[Fact]
	public async Task ALeaseThatComesBack_WaitsForTheReadItsPredecessorStarted()
	{
		Reader reader = new(_time, MonitorFixtures.UbuntuFirst) { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
		await using HostMonitor monitor = Monitor(reader);

		IDisposable first = monitor.Watch(Slow);
		await Eventually.TrueAsync(() => reader.InFlight == 1, "a read under way");
		first.Dispose();
		using IDisposable second = monitor.Watch(Slow);

		// The cancelled read ends before the new loop reads, so the two never overlap.
		await Eventually.TrueAsync(() => reader.Cancelled == 1, "the first read cancelled");
		await Eventually.TrueAsync(() => reader.Count == 2, "the new lease's read");
		reader.Gate.SetResult();
		await Eventually.TrueAsync(() => monitor.State.Figures is not null, "figures from the new read");
		Assert.Equal(1, reader.MostAtOnce);
	}

	[Fact]
	public async Task AFailedRead_KeepsTheFiguresMarkedStale_SeveralHideThem_AndOneThatWorksBringsThemBack()
	{
		Reader reader = new(_time, MonitorFixtures.UbuntuFirst, null, null, null, MonitorFixtures.UbuntuSecond);
		await using HostMonitor monitor = Monitor(reader);
		using IDisposable lease = monitor.Watch(Slow);
		await Eventually.TrueAsync(() => monitor.State.Figures is not null, "the first figures");
		HostFigures figures = monitor.State.Figures!;

		await Eventually.AdvanceUntilAsync(_time, Step, () => monitor.State.FailedReads == 1, "one failed read");
		HostMonitorState stale = monitor.State;
		Assert.True(stale.IsStale);
		Assert.True(stale.IsShown);
		Assert.Same(figures, stale.Figures);
		Assert.Equal("The command did not finish within 10 seconds, so it was stopped.", stale.Problem);

		await Eventually.AdvanceUntilAsync(_time, Step, () => monitor.State.FailedReads == HostMonitorState.HideAfterFailures, "several failed reads");
		Assert.False(monitor.State.IsShown);
		Assert.Null(monitor.State.Unavailable);

		await Eventually.AdvanceUntilAsync(_time, Step, () => monitor.State.FailedReads == 0, "a read that works");
		Assert.True(monitor.State.IsShown);
		Assert.Null(monitor.State.Problem);
		Assert.Equal(25, monitor.State.Figures!.CpuPercent!.Value, 6);
	}

	[Fact]
	public async Task ALoginThatRunsNoScripts_TurnsTheFiguresOff_AndIsNotAskedAgain()
	{
		Reader reader = new(_time) { Throw = new AdminException("The server did not run the script.") { NeverStarted = true } };
		await using HostMonitor monitor = Monitor(reader);
		IDisposable lease = monitor.Watch(Slow);

		await Eventually.TrueAsync(() => monitor.State.Unavailable is not null, "the figures turned off");
		Assert.Equal(HostMonitor.CannotRunReason, monitor.State.Unavailable);
		Assert.False(monitor.State.IsShown);

		lease.Dispose();
		using IDisposable again = monitor.Watch(Fast);
		for (int i = 0; i < 6; i++)
		{
			_time.Advance(Slow);
			await Task.Delay(10, Ct);
		}

		await Eventually.StaysTrueAsync(() => reader.Count == 1, "one read, then never again");
	}

	[Fact]
	public async Task AMachineWithoutProc_TurnsTheFiguresOff()
	{
		Reader reader = new(_time, MonitorFixtures.FreeBsd);
		await using HostMonitor monitor = Monitor(reader);
		using IDisposable lease = monitor.Watch(Slow);

		await Eventually.TrueAsync(() => monitor.State.Unavailable is not null, "the figures turned off");
		Assert.Equal(HostMonitor.NoProcReason, monitor.State.Unavailable);
		for (int i = 0; i < 6; i++)
		{
			_time.Advance(Slow);
			await Task.Delay(10, Ct);
		}

		await Eventually.StaysTrueAsync(() => reader.Count == 1, "not asked again");
	}

	[Fact]
	public async Task ASampleReadElsewhere_StartsTheFigures_AndTheNextReadWaitsAnInterval()
	{
		Reader reader = new(_time, MonitorFixtures.UbuntuSecond);
		await using HostMonitor monitor = Monitor(reader);
		int changes = 0;
		monitor.Changed += () => Interlocked.Increment(ref changes);

		monitor.Record(MonitorCommands.Parse(MonitorFixtures.UbuntuFirst));
		Assert.Equal(1, Volatile.Read(ref changes));
		Assert.NotNull(monitor.State.Figures);
		using IDisposable lease = monitor.Watch(Fast);
		await Eventually.StaysTrueAsync(() => reader.Count == 0, "no read right after the recorded one");

		await Eventually.AdvanceUntilAsync(_time, Step, () => monitor.State.Figures?.CpuPercent is not null, "the next read, an interval later");
		Assert.Equal(1, reader.Count);
		Assert.Equal(2, monitor.History.Memory.Count);
	}

	[Fact]
	public void Figures_AreTheNewestSample_InBytes()
	{
		MonitorHistory history = new();
		history.Add(MonitorCommands.Parse(MonitorFixtures.UbuntuFirst));
		DateTimeOffset readAt = new(2026, 10, 18, 9, 30, 0, TimeSpan.Zero);

		HostFigures figures = HostMonitor.FiguresOf(history, readAt);

		Assert.Equal(readAt, figures.ReadAt);
		Assert.Null(figures.CpuPercent);
		Assert.Equal((8037888L - 5122000) * 1024, figures.MemoryUsedBytes);
		Assert.Equal(8037888L * 1024, figures.MemoryTotalBytes);
		Assert.Equal(200000L * 1024, figures.SwapUsedBytes);
		Assert.Equal(36.28, figures.MemoryPercent!.Value, 2);
		Assert.Equal(0.42, figures.Load1);
		Assert.Equal(0.27, figures.Load15);
		Assert.Equal(TimeSpan.FromSeconds(350735), figures.Uptime);
		Assert.NotNull(figures.RootDisk);
		Assert.Equal("/", figures.RootDisk.MountPoint);
		Assert.Equal(72, figures.RootDisk.UsedPercent);
		Assert.Equal(41234567L * 1024, figures.RootDisk.UsedBytes);
		Assert.Equal(60795672L * 1024, figures.RootDisk.TotalBytes);
	}

	[Fact]
	public async Task Dispose_CancelsTheReadUnderWay_AndLaterLeasesReadNothing()
	{
		Reader reader = new(_time, MonitorFixtures.UbuntuFirst) { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
		HostMonitor monitor = Monitor(reader);
		_ = monitor.Watch(Slow);
		await Eventually.TrueAsync(() => reader.InFlight == 1, "a read under way");

		await Eventually.CompletesAsync(monitor.DisposeAsync().AsTask());

		Assert.Equal(1, reader.Cancelled);
		using IDisposable late = monitor.Watch(Slow);
		_time.Advance(Slow);
		await Eventually.StaysTrueAsync(() => reader.Count == 1, "nothing after dispose");
	}

	// The workbench and any session with commands only: the sample goes through the command feature as the login, and a
	// login whose shell never prints the marker turns the figures off.
	[Fact]
	public async Task ForCommands_ReadsAsTheLogin_AndAMissingMarkerTurnsTheFiguresOff()
	{
		ScriptedCommands commands = new("MOKATERM-OUT\0" + MonitorFixtures.UbuntuFirst.ReplaceLineEndings("\n"));
		await using HostMonitor monitor = HostMonitor.ForCommands(commands, _time, "system");
		using IDisposable lease = monitor.Watch(Slow);

		await Eventually.TrueAsync(() => monitor.State.Figures is not null, "figures over the command feature");
		Assert.Equal("system", monitor.ToolKey);
		Assert.Contains("# mokaterm system view: overview.sample", commands.LastCommand, StringComparison.Ordinal);
		Assert.DoesNotContain("sudo", commands.LastCommand, StringComparison.Ordinal);

		commands.Output = "This service allows sftp connections only.\n";
		await Eventually.AdvanceUntilAsync(_time, Step, () => monitor.State.Unavailable is not null, "the figures turned off");
		Assert.Equal(HostMonitor.CannotRunReason, monitor.State.Unavailable);
	}

	private HostMonitor Monitor(Reader reader) => new(reader.ReadAsync, _time, NullLogger.Instance, toolKey: "system");

	private static async Task<HostFigures> FiguresAsync(HostMonitor monitor)
	{
		await Eventually.TrueAsync(() => monitor.State.Figures is not null, "figures");
		return monitor.State.Figures!;
	}

	/// <summary>
	/// Answers each sample with the next canned output (the last one again once they run out); a null is a read that
	/// timed out. Counts reads, how many ran at once, and the fake time each one started.
	/// </summary>
	private sealed class Reader(FakeTimeProvider time, params string?[] outputs)
	{
		private readonly Lock _lock = new();
		private readonly List<DateTimeOffset> _times = [];
		private readonly List<string> _scripts = [];
		private int _inFlight;
		private int _mostAtOnce;
		private int _cancelled;

		public TaskCompletionSource? Gate { get; init; }

		public Exception? Throw { get; init; }

		public int Count
		{
			get
			{
				lock (_lock)
				{
					return _times.Count;
				}
			}
		}

		public IReadOnlyList<DateTimeOffset> Times
		{
			get
			{
				lock (_lock)
				{
					return [.. _times];
				}
			}
		}

		public IReadOnlyList<string> Scripts
		{
			get
			{
				lock (_lock)
				{
					return [.. _scripts];
				}
			}
		}

		public int InFlight => Volatile.Read(ref _inFlight);

		public int MostAtOnce => Volatile.Read(ref _mostAtOnce);

		public int Cancelled => Volatile.Read(ref _cancelled);

		public async Task<AdminOutcome> ReadAsync(AdminScript script, CancellationToken cancellationToken)
		{
			Assert.False(script.AsRoot);
			string? output;
			lock (_lock)
			{
				_times.Add(time.GetUtcNow());
				_scripts.Add(script.Name);
				output = outputs.Length == 0 ? null : outputs[Math.Min(_times.Count - 1, outputs.Length - 1)];
				_inFlight++;
				_mostAtOnce = Math.Max(_mostAtOnce, _inFlight);
			}

			try
			{
				if (Gate is { } gate)
				{
					await gate.Task.WaitAsync(cancellationToken);
				}

				if (Throw is { } failure)
				{
					throw failure;
				}

				return output is null
					? throw new AdminException(AdminTexts.TimedOut(script.Timeout))
					: new AdminOutcome { ExitCode = 0, Output = output.ReplaceLineEndings("\n") };
			}
			catch (OperationCanceledException)
			{
				Interlocked.Increment(ref _cancelled);
				throw;
			}
			finally
			{
				lock (_lock)
				{
					_inFlight--;
				}
			}
		}
	}

	/// <summary>A command feature that prints what it is told, the way a server's shell would.</summary>
	private sealed class ScriptedCommands(string output) : IRemoteCommandFeature
	{
		private readonly Lock _lock = new();
		private string _output = output;
		private string _lastCommand = "";

		public string Output
		{
			get
			{
				lock (_lock)
				{
					return _output;
				}
			}

			set
			{
				lock (_lock)
				{
					_output = value;
				}
			}
		}

		public string LastCommand
		{
			get
			{
				lock (_lock)
				{
					return _lastCommand;
				}
			}
		}

		public Task<RemoteCommandOutcome> RunAsync(RemoteCommandRequest request, CancellationToken cancellationToken)
		{
			byte[] bytes;
			lock (_lock)
			{
				_lastCommand = request.Command;
				bytes = Encoding.UTF8.GetBytes(_output);
			}

			return Task.FromResult(new RemoteCommandOutcome
			{
				ExitCode = 0,
				Output = new CapturedOutput(bytes, bytes.Length),
				Error = CapturedOutput.Empty,
			});
		}
	}
}
