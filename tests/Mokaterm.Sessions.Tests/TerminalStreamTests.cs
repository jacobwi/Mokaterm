using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Sessions.Terminal;
using Mokaterm.Sessions.Tests.Fakes;

namespace Mokaterm.Sessions.Tests;

public sealed class TerminalStreamTests : IAsyncDisposable
{
	private static readonly TerminalSize Size80X24 = new(80, 24);

	private readonly List<TerminalStream> _streams = [];

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Attach_WhileOutputFlows_DeliversReplayThenLiveWithoutLossOrDuplication()
	{
		const int Lines = 3000;
		string expected = string.Concat(Enumerable.Range(0, Lines).Select(Line));
		TerminalStream stream = CreateStream(replayBytes: 4 * 1024 * 1024);
		FakeTerminalChannel channel = new();
		List<RecordingSink> sinks = [new RecordingSink()];
		List<IDisposable> attachments = [stream.Attach(sinks[0])];
		_ = stream.Bind(channel, Size80X24, generation: 1);

		Task producer = Task.Run(
			async () =>
			{
				for (int i = 0; i < Lines; i++)
				{
					channel.Emit(Line(i));
					if (i % 50 == 0)
					{
						await Task.Delay(1, Ct);
					}
				}

				channel.Close();
			},
			Ct);

		for (int i = 0; i < 8; i++)
		{
			await Task.Delay(15, Ct);

			// Every other sink goes async on each write, so its queue fills while output keeps coming.
			RecordingSink sink = i % 2 == 0 ? new RecordingSink() : new RecordingSink { OnWrite = async (_, _) => await Task.Yield() };
			sinks.Add(sink);
			attachments.Add(stream.Attach(sink));
		}

		await producer;
		await Eventually.TrueAsync(() => sinks.TrueForAll(sink => sink.Length >= expected.Length), "every sink has all output");
		await Eventually.StaysTrueAsync(() => sinks.TrueForAll(sink => sink.Length == expected.Length), "no sink receives a chunk twice");
		Assert.All(sinks, sink => Assert.Equal(expected, sink.Text));
		attachments.ForEach(attachment => attachment.Dispose());
	}

	[Fact]
	public async Task SlowSink_BoundsHowMuchThePumpReads()
	{
		TerminalStream stream = CreateStream(replayBytes: 64 * 1024);
		FakeTerminalChannel channel = new() { Endless = true };
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		RecordingSink sink = new() { OnWrite = async (_, cancellationToken) => await release.Task.WaitAsync(cancellationToken) };
		using IDisposable attachment = stream.Attach(sink);

		_ = stream.Bind(channel, Size80X24, generation: 1);
		long settled = await Eventually.StableAsync(() => channel.BytesRead);

		// One chunk inside the sink, a full queue, and the chunk the pump holds while it waits for space.
		long bound = (TerminalStream.SinkQueueCapacity + 2) * (long)TerminalStream.ReadBufferSize;
		Assert.InRange(settled, TerminalStream.ReadBufferSize, bound);

		release.SetResult();
		await Eventually.TrueAsync(() => channel.BytesRead > settled + (10L * TerminalStream.ReadBufferSize), "the pump resumes once the sink catches up");
		await stream.DisposeAsync();
	}

	[Fact]
	public async Task Bind_AfterTheChannelClosed_ResumesWithTheSameSinks()
	{
		TerminalStream stream = CreateStream();
		int stateChanges = 0;
		stream.StateChanged += () => Interlocked.Increment(ref stateChanges);
		RecordingSink sink = new();
		using IDisposable attachment = stream.Attach(sink);

		FakeTerminalChannel first = new();
		Task firstPump = stream.Bind(first, Size80X24, generation: 1);
		Assert.True(stream.IsOpen);
		first.Emit("one ");
		await Eventually.TrueAsync(() => sink.Text == "one ", "the first channel's output arrives");

		first.Close();
		await firstPump.WaitAsync(Ct);
		Assert.False(stream.IsOpen);
		await stream.SendTextAsync("lost", Ct);
		Assert.Equal("", first.InputText);

		FakeTerminalChannel second = new();
		_ = stream.Bind(second, Size80X24, generation: 2);
		second.Emit("two");
		await stream.SendTextAsync("ls\r", Ct);

		await Eventually.TrueAsync(() => sink.Text == "one two", "the second channel's output reaches the same sink");
		Assert.True(stream.IsOpen);
		Assert.Equal("ls\r", second.InputText);
		Assert.Equal(3, Volatile.Read(ref stateChanges));
	}

	[Fact]
	public async Task Bind_FromAnOlderAttempt_LeavesTheNewerChannelBound()
	{
		TerminalStream stream = CreateStream();
		RecordingSink sink = new();
		using IDisposable attachment = stream.Attach(sink);
		FakeTerminalChannel newer = new();
		FakeTerminalChannel older = new();
		_ = stream.Bind(newer, Size80X24, generation: 2);

		Task ignored = stream.Bind(older, Size80X24, generation: 1);
		older.Emit("stale");
		older.Close();
		newer.Emit("live");

		Assert.True(ignored.IsCompleted);
		await Eventually.TrueAsync(() => sink.Text == "live", "the newer channel's output arrives");
		await Eventually.StaysTrueAsync(() => stream.IsOpen && sink.Text == "live", "the older channel is never read");
		await stream.SendTextAsync("ok", Ct);
		Assert.Equal("ok", newer.InputText);
		Assert.Equal("", older.InputText);
	}

	[Fact]
	public async Task WriteLocalAsync_UsesCrLf_AndReachesSinksAndTheReplay()
	{
		TerminalStream stream = CreateStream();
		RecordingSink attached = new();
		using IDisposable first = stream.Attach(attached);

		await stream.WriteLocalAsync("closed\nbye\r\n", Ct);
		await Eventually.TrueAsync(() => attached.Text == "closed\r\nbye\r\n", "the notice reaches the attached sink");

		RecordingSink later = new();
		using IDisposable second = stream.Attach(later);
		await Eventually.TrueAsync(() => later.Text == "closed\r\nbye\r\n", "a sink attached later replays the notice");
	}

	[Fact]
	public async Task ResizeAsync_ForwardsOnlyRealChanges_AndCatchesUpAfterRebind()
	{
		TerminalStream stream = CreateStream();
		FakeTerminalChannel first = new();
		Task firstPump = stream.Bind(first, Size80X24, generation: 1);

		await stream.ResizeAsync(Size80X24, Ct);
		await stream.ResizeAsync(new TerminalSize(100, 30), Ct);
		await stream.ResizeAsync(new TerminalSize(100, 30), Ct);
		await stream.ResizeAsync(new TerminalSize(0, 0), Ct);
		Assert.Equal(new TerminalSize(100, 30), Assert.Single(first.Resizes));

		first.Close();
		await firstPump.WaitAsync(Ct);
		await stream.ResizeAsync(new TerminalSize(120, 40), Ct);
		Assert.Equal(new TerminalSize(120, 40), stream.Size);
		_ = Assert.Single(first.Resizes);

		// The new channel was opened at the old size; the size reported while closed follows it.
		FakeTerminalChannel second = new();
		_ = stream.Bind(second, new TerminalSize(100, 30), generation: 2);
		await Eventually.TrueAsync(() => second.Resizes.Count == 1, "the pending size reaches the new channel");
		await stream.ResizeAsync(new TerminalSize(120, 40), Ct);
		Assert.Equal(new TerminalSize(120, 40), Assert.Single(second.Resizes));
	}

	[Fact]
	public async Task FaultySink_IsDetachedAndLogged_WhileOtherSinksContinue()
	{
		ListLogger<TerminalStreamTests> logger = new();
		TerminalStream stream = CreateStream(logger: logger);
		RecordingSink faulty = new() { OnWrite = (_, _) => throw new InvalidOperationException("The view is gone.") };
		RecordingSink healthy = new();
		using IDisposable faultyAttachment = stream.Attach(faulty);
		using IDisposable healthyAttachment = stream.Attach(healthy);
		FakeTerminalChannel channel = new();
		_ = stream.Bind(channel, Size80X24, generation: 1);

		channel.Emit("a");
		await Eventually.TrueAsync(() => healthy.Text == "a", "the healthy sink gets the first chunk");
		await Eventually.TrueAsync(() => logger.Entries.Any(entry => entry.Level == LogLevel.Warning), "the failure is logged");

		channel.Emit("b");
		channel.Emit("c");
		await Eventually.TrueAsync(() => healthy.Text == "abc", "the healthy sink keeps receiving");
		Assert.Equal(1, faulty.WriteCount);
	}

	[Fact]
	public async Task DisposingTheAttachment_StopsDelivery()
	{
		TerminalStream stream = CreateStream();
		FakeTerminalChannel channel = new();
		RecordingSink detached = new();
		RecordingSink marker = new();
		IDisposable attachment = stream.Attach(detached);
		using IDisposable markerAttachment = stream.Attach(marker);
		_ = stream.Bind(channel, Size80X24, generation: 1);

		channel.Emit("a");
		await Eventually.TrueAsync(() => detached.Text == "a" && marker.Text == "a", "both sinks get the first chunk");
		attachment.Dispose();
		channel.Emit("b");

		await Eventually.TrueAsync(() => marker.Text == "ab", "the remaining sink gets the next chunk");
		await Eventually.StaysTrueAsync(() => detached.Text == "a", "the detached sink gets nothing more");
	}

	public async ValueTask DisposeAsync()
	{
		foreach (TerminalStream stream in _streams)
		{
			await stream.DisposeAsync();
		}
	}

	private static string Line(int number) => string.Create(CultureInfo.InvariantCulture, $"line {number:D5}\n");

	private TerminalStream CreateStream(int replayBytes = 1024 * 1024, ILogger? logger = null)
	{
		TerminalStream stream = new(replayBytes, logger ?? NullLogger.Instance);
		_streams.Add(stream);
		return stream;
	}
}
