using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Monitoring;
using Mokaterm.UI.Common.ToolPages;

namespace Mokaterm.Modules.Ssh.Tests.Admin;

/// <summary>
/// The overview's polling end to end below the page: the page poller ticks the sampler every interval while the view
/// is shown, each tick runs one sample script through the session's feature, and a failed tick does not stop the next.
/// </summary>
public sealed class LiveSamplingTests
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

	private readonly FakeTimeProvider _time = new();

	[Fact]
	public async Task EveryTick_RunsOneSample_AndTheSecondGivesRates()
	{
		SamplingShell shell = new([MonitorFixtures.UbuntuFirst, MonitorFixtures.UbuntuSecond]);
		await using SystemAdminFeature feature = new(shell, _time, NullLogger.Instance);
		MonitorSampler sampler = new(feature.ReadAsync);
		using PagePoller poller = new(_time, tick => tick(), () => true);

		poller.Start(MonitorCommands.Interval, () => sampler.SampleAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
		await Eventually.AdvanceUntilAsync(_time, MonitorCommands.Interval, () => sampler.History.Rates is not null, "two samples");

		Assert.Equal(["overview.sample", "overview.sample"], shell.Scripts);
		Assert.Equal(25, sampler.History.Cpu.Latest!.Value, 6);
		Assert.Null(sampler.Error);
	}

	[Fact]
	public async Task AHiddenView_ReadsNothing()
	{
		SamplingShell shell = new([MonitorFixtures.UbuntuFirst]);
		await using SystemAdminFeature feature = new(shell, _time, NullLogger.Instance);
		MonitorSampler sampler = new(feature.ReadAsync);
		bool visible = false;
		using PagePoller poller = new(_time, tick => tick(), () => Volatile.Read(ref visible));

		poller.Start(MonitorCommands.Interval, () => sampler.SampleAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
		for (int i = 0; i < 5; i++)
		{
			_time.Advance(MonitorCommands.Interval);
			await Task.Delay(20, TestContext.Current.CancellationToken);
		}

		Assert.Empty(shell.Scripts);

		Volatile.Write(ref visible, true);
		await Eventually.AdvanceUntilAsync(_time, MonitorCommands.Interval, () => sampler.History.Latest is not null, "a sample once shown");
		Assert.Single(shell.Scripts);
	}

	[Fact]
	public async Task AFailedSample_KeepsTheFigures_AndTheNextTickTriesAgain()
	{
		SamplingShell shell = new([MonitorFixtures.UbuntuFirst, null, MonitorFixtures.UbuntuSecond]);
		await using SystemAdminFeature feature = new(shell, _time, NullLogger.Instance);
		MonitorSampler sampler = new(feature.ReadAsync);

		Assert.True(await sampler.SampleAsync(TestContext.Current.CancellationToken).WaitAsync(Wait, TestContext.Current.CancellationToken));
		Assert.False(await sampler.SampleAsync(TestContext.Current.CancellationToken).WaitAsync(Wait, TestContext.Current.CancellationToken));
		Assert.Equal("The command did not finish within 10 seconds, so it was stopped.", sampler.Error);
		Assert.NotNull(sampler.History.Latest);

		Assert.True(await sampler.SampleAsync(TestContext.Current.CancellationToken).WaitAsync(Wait, TestContext.Current.CancellationToken));
		Assert.Null(sampler.Error);
		Assert.NotNull(sampler.History.Rates);
	}

	/// <summary>Answers each sample with the next canned output; a null in the list is a sample that timed out.</summary>
	private sealed class SamplingShell(IReadOnlyList<string?> outputs) : IAdminShell
	{
		private int _next;

		public List<string> Scripts { get; } = [];

		public string? ElevationBlockedReason => "No root here.";

		public bool HasRoot => false;

		public string Account => "alice@web1";

		public bool SupportsInput => false;

		public Task EnableRootAsync(CancellationToken cancellationToken) => throw new AdminException("No root here.");

		public void DisableRoot()
		{
		}

		public Task<AdminOutcome> RunAsync(AdminScript script, bool elevated, AdminInput? input, CancellationToken cancellationToken)
		{
			lock (Scripts)
			{
				Scripts.Add(script.Name);
			}

			string? output = outputs[Math.Min(_next++, outputs.Count - 1)];
			return output is null
				? throw new AdminException(AdminTexts.TimedOut(script.Timeout))
				: Task.FromResult(new AdminOutcome { ExitCode = 0, Output = output.ReplaceLineEndings("\n") });
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
