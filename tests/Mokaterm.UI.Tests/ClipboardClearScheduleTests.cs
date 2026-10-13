using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The clear after a secret copy, which a window shares with its tool windows: it goes through a page that is still
/// open, only the newest copy is ever cleared, and a plain copy anywhere in the window calls it off.
/// </summary>
public sealed class ClipboardClearScheduleTests
{
	private static readonly TimeSpan ClearTime = TimeSpan.FromSeconds(30);

	private readonly FakeTimeProvider _time = new();
	private readonly FakeSettingsService _settings = new();

	public ClipboardClearScheduleTests() => _settings.Set(new SecuritySettings { ClipboardClearSeconds = (int)ClearTime.TotalSeconds });

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task TheCopyingPage_ClearsAtTheDeadline_WhileItIsOpen()
	{
		using ClipboardClearSchedule schedule = Schedule();
		FakePage main = Open(schedule, new FakePage());
		FakePage tool = Open(schedule, new FakePage());
		FakePage newest = Open(schedule, new FakePage());

		schedule.SecretCopied(tool);
		_time.Advance(ClearTime - TimeSpan.FromSeconds(1));
		await Eventually.StaysTrueAsync(() => tool.Clears.Length == 0, "nothing is cleared before the deadline");

		_time.Advance(TimeSpan.FromSeconds(1));
		await Eventually.TrueAsync(() => tool.Clears.SequenceEqual([1L]), "the page that copied clears");
		Assert.Empty(main.Clears);
		Assert.Empty(newest.Clears);
	}

	[Fact]
	public async Task ACopyingPageThatClosed_LeavesTheClearToAPageStillOpen()
	{
		using ClipboardClearSchedule schedule = Schedule();
		FakePage main = Open(schedule, new FakePage());
		FakePage tool = Open(schedule, new FakePage());

		schedule.SecretCopied(tool);
		schedule.Unregister(tool);
		_time.Advance(ClearTime);

		await Eventually.TrueAsync(() => main.Clears.SequenceEqual([1L]), "the main window clears");
		Assert.Empty(tool.Clears);
	}

	[Fact]
	public async Task WithTheCopyingPageGone_TheNewestPageClears()
	{
		using ClipboardClearSchedule schedule = Schedule();
		FakePage main = Open(schedule, new FakePage());
		FakePage copier = Open(schedule, new FakePage());
		FakePage other = Open(schedule, new FakePage());

		schedule.SecretCopied(copier);
		schedule.Unregister(copier);
		_time.Advance(ClearTime);

		await Eventually.TrueAsync(() => other.Clears.SequenceEqual([1L]), "the page opened last clears");
		Assert.Empty(main.Clears);
	}

	[Fact]
	public async Task APlainCopy_CallsTheClearOff_WhicheverPageMadeIt()
	{
		using ClipboardClearSchedule schedule = Schedule();
		FakePage main = Open(schedule, new FakePage());
		FakePage tool = Open(schedule, new FakePage());

		schedule.SecretCopied(tool);
		_time.Advance(ClearTime / 2);
		schedule.TextCopied();
		_time.Advance(ClearTime * 2);

		await Eventually.StaysTrueAsync(() => main.Clears.Length == 0 && tool.Clears.Length == 0, "nothing is cleared");
	}

	[Fact]
	public async Task ASecondSecret_ReplacesTheFirstDeadline()
	{
		using ClipboardClearSchedule schedule = Schedule();
		FakePage main = Open(schedule, new FakePage());

		schedule.SecretCopied(main);
		_time.Advance(TimeSpan.FromSeconds(20));
		schedule.SecretCopied(main);
		_time.Advance(TimeSpan.FromSeconds(10));
		await Eventually.StaysTrueAsync(() => main.Clears.Length == 0, "the first deadline is gone");

		_time.Advance(TimeSpan.FromSeconds(20));
		await Eventually.TrueAsync(() => main.Clears.SequenceEqual([2L]), "the second secret is cleared once, at its own deadline");
		_time.Advance(ClearTime * 2);
		await Eventually.StaysTrueAsync(() => main.Clears.Length == 1, "nothing is cleared twice");
	}

	// A page that cleared may still be waiting for focus, and its script has to hear that the clipboard holds something
	// newer before that focus wipes it.
	[Fact]
	public async Task ANewerCopy_IsAnnouncedToThePagesAClearWentTo()
	{
		using ClipboardClearSchedule schedule = Schedule();
		FakePage main = Open(schedule, new FakePage());
		FakePage tool = Open(schedule, new FakePage());

		schedule.SecretCopied(main);
		_time.Advance(ClearTime);
		await Eventually.TrueAsync(() => main.Clears.SequenceEqual([1L]), "the main window clears");

		schedule.TextCopied();

		await Eventually.TrueAsync(() => main.Forgets.SequenceEqual([2L]), "the main window forgets the older clear");
		Assert.Empty(tool.Forgets);
	}

	[Fact]
	public async Task AClearForAnOlderCopy_NeverRunsOnceANewerCopyIsMade()
	{
		using ClipboardClearSchedule schedule = Schedule();
		FakePage main = Open(schedule, new FakePage());
		FakePage tool = Open(schedule, new FakePage { Hangs = true });

		schedule.SecretCopied(tool);
		_time.Advance(ClearTime);
		await Eventually.TrueAsync(() => tool.Clears.SequenceEqual([1L]), "the tool window is asked to clear");

		// The plain copy lands while the tool window still has not answered.
		schedule.TextCopied();
		_time.Advance(ClipboardClearSchedule.PageAnswerLimit);
		await Eventually.TrueAsync(() => tool.GaveUp == 1, "the tool window runs out of time");

		await Eventually.StaysTrueAsync(() => main.Clears.Length == 0, "the main window is never asked to clear the plain copy");
		Assert.Equal([2L], tool.Forgets);
	}

	[Fact]
	public async Task WithNoPageLeft_NothingIsCleared_AndALaterPageIsNotAsked()
	{
		using ClipboardClearSchedule schedule = Schedule();
		FakePage tool = Open(schedule, new FakePage());

		schedule.SecretCopied(tool);
		schedule.Unregister(tool);
		_time.Advance(ClearTime);
		FakePage later = Open(schedule, new FakePage());
		_time.Advance(ClearTime * 2);

		await Eventually.StaysTrueAsync(() => tool.Clears.Length == 0 && later.Clears.Length == 0, "nothing is cleared");
	}

	[Fact]
	public async Task ClearSecondsAtZero_LeavesTheSecret()
	{
		_settings.Set(new SecuritySettings { ClipboardClearSeconds = 0 });
		using ClipboardClearSchedule schedule = Schedule();
		FakePage main = Open(schedule, new FakePage());

		schedule.SecretCopied(main);
		_time.Advance(TimeSpan.FromSeconds(SecuritySettings.MaxClipboardClearSeconds + 1));

		await Eventually.StaysTrueAsync(() => main.Clears.Length == 0, "nothing is cleared");
	}

	[Theory]
	[InlineData(typeof(JSDisconnectedException))]
	[InlineData(typeof(ObjectDisposedException))]
	[InlineData(typeof(JSException))]
	public async Task APageThatThrows_IsPassedOverForTheNextOne(Type failure)
	{
		using ClipboardClearSchedule schedule = Schedule();
		FakePage main = Open(schedule, new FakePage());
		FakePage tool = Open(schedule, new FakePage { Failure = (Exception)Activator.CreateInstance(failure, "The page is closing.")! });

		schedule.SecretCopied(tool);
		_time.Advance(ClearTime);

		await Eventually.TrueAsync(() => main.Clears.SequenceEqual([1L]), "the main window clears");
		Assert.Equal([1L], tool.Clears);
	}

	[Fact]
	public async Task APageThatNeverAnswers_IsPassedOverAfterTheLimit()
	{
		using ClipboardClearSchedule schedule = Schedule();
		FakePage main = Open(schedule, new FakePage());
		FakePage tool = Open(schedule, new FakePage { Hangs = true });

		schedule.SecretCopied(tool);
		_time.Advance(ClearTime);
		await Eventually.TrueAsync(() => tool.Clears.Length == 1, "the tool window is asked first");
		Assert.Empty(main.Clears);

		_time.Advance(ClipboardClearSchedule.PageAnswerLimit);

		await Eventually.TrueAsync(() => main.Clears.SequenceEqual([1L]), "the main window clears");
	}

	[Fact]
	public async Task ClosingTheToolWindowsClipboard_LeavesTheClearToTheMainWindows()
	{
		using ClipboardClearSchedule schedule = Schedule();
		ScriptedPage mainPage = new();
		ScriptedPage toolPage = new();
		await using JsClipboardService main = new(mainPage, schedule);
		JsClipboardService tool = new(toolPage, schedule);

		await Eventually.CompletesAsync(tool.WriteSecretAsync("correct horse", Ct).AsTask());
		await tool.DisposeAsync();
		_time.Advance(ClearTime);

		await Eventually.TrueAsync(() => mainPage.Module.Calls.SequenceEqual([("clearClipboard", 1L)]), "the main window clears");
		Assert.Equal(["correct horse"], toolPage.Written);
		Assert.Empty(toolPage.Module.Calls);
	}

	[Fact]
	public async Task APlainCopyInTheMainWindow_CallsOffTheToolWindowsClear()
	{
		using ClipboardClearSchedule schedule = Schedule();
		ScriptedPage mainPage = new();
		ScriptedPage toolPage = new();
		await using JsClipboardService main = new(mainPage, schedule);
		await using JsClipboardService tool = new(toolPage, schedule);

		await Eventually.CompletesAsync(tool.WriteSecretAsync("correct horse", Ct).AsTask());
		await Eventually.CompletesAsync(main.WriteTextAsync("db01.example.com", Ct).AsTask());
		_time.Advance(ClearTime * 2);

		await Eventually.StaysTrueAsync(
			() => mainPage.Module.Calls.Length == 0 && toolPage.Module.Calls.Length == 0,
			"neither window clears");
	}

	// A refused write leaves the secret where it was, so the clear for it has to stay.
	[Fact]
	public async Task APlainCopyThePageRefused_KeepsTheClear()
	{
		using ClipboardClearSchedule schedule = Schedule();
		ScriptedPage mainPage = new() { RefusesWrites = true };
		ScriptedPage toolPage = new();
		await using JsClipboardService main = new(mainPage, schedule);
		await using JsClipboardService tool = new(toolPage, schedule);

		await Eventually.CompletesAsync(tool.WriteSecretAsync("correct horse", Ct).AsTask());
		await Assert.ThrowsAsync<JSException>(() => Eventually.CompletesAsync(main.WriteTextAsync("db01.example.com", Ct).AsTask()));
		_time.Advance(ClearTime);

		await Eventually.TrueAsync(() => toolPage.Module.Calls.SequenceEqual([("clearClipboard", 1L)]), "the tool window clears");
	}

	private static FakePage Open(ClipboardClearSchedule schedule, FakePage page)
	{
		schedule.Register(page);
		return page;
	}

	private ClipboardClearSchedule Schedule() => new(_time, _settings, NullLoggerFactory.Instance);

	/// <summary>A page that records what the schedule asked of it, and can fail or never answer.</summary>
	private sealed class FakePage : IClipboardClearer
	{
		private readonly Lock _lock = new();
		private readonly List<long> _clears = [];
		private readonly List<long> _forgets = [];
		private int _gaveUp;

		public Exception? Failure { get; init; }

		public bool Hangs { get; init; }

		public long[] Clears
		{
			get
			{
				lock (_lock)
				{
					return [.. _clears];
				}
			}
		}

		public long[] Forgets
		{
			get
			{
				lock (_lock)
				{
					return [.. _forgets];
				}
			}
		}

		public int GaveUp => Volatile.Read(ref _gaveUp);

		public async ValueTask ClearAsync(long copy, CancellationToken cancellationToken)
		{
			lock (_lock)
			{
				_clears.Add(copy);
			}

			if (Failure is not null)
			{
				throw Failure;
			}

			if (Hangs)
			{
				try
				{
					await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
				}
				finally
				{
					_ = Interlocked.Increment(ref _gaveUp);
				}
			}
		}

		public ValueTask ForgetClearsBeforeAsync(long copy, CancellationToken cancellationToken)
		{
			lock (_lock)
			{
				_forgets.Add(copy);
			}

			return ValueTask.CompletedTask;
		}
	}

	/// <summary>A page whose clipboard writes are recorded, as is every call into its clipboard script.</summary>
	private sealed class ScriptedPage : IJSRuntime
	{
		private readonly Lock _lock = new();
		private readonly List<string> _written = [];

		public ScriptedModule Module { get; } = new();

		public bool RefusesWrites { get; init; }

		public string[] Written
		{
			get
			{
				lock (_lock)
				{
					return [.. _written];
				}
			}
		}

		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
			InvokeAsync<TValue>(identifier, CancellationToken.None, args);

		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
		{
			switch (identifier)
			{
				case "import":
					return ValueTask.FromResult((TValue)(object)Module);
				case "navigator.clipboard.writeText" when RefusesWrites:
					return ValueTask.FromException<TValue>(new JSException("Document is not focused."));
				case "navigator.clipboard.writeText":
					lock (_lock)
					{
						_written.Add((string)args![0]!);
					}

					return ValueTask.FromResult(default(TValue)!);
				default:
					throw new InvalidOperationException("Unexpected call " + identifier);
			}
		}
	}

	private sealed class ScriptedModule : IJSObjectReference
	{
		private readonly Lock _lock = new();
		private readonly List<(string Function, long Copy)> _calls = [];

		public (string Function, long Copy)[] Calls
		{
			get
			{
				lock (_lock)
				{
					return [.. _calls];
				}
			}
		}

		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
			InvokeAsync<TValue>(identifier, CancellationToken.None, args);

		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
		{
			lock (_lock)
			{
				_calls.Add((identifier, (long)args![0]!));
			}

			return ValueTask.FromResult(default(TValue)!);
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
