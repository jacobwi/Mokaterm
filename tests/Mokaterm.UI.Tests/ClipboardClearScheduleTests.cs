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

	[Fact]
	public void ASecretCopy_IsPending_WithTheClipboardsNumberFromRightAfterTheCopy()
	{
		FakeSystemClipboard system = new();
		using ClipboardClearSchedule schedule = Schedule(system);
		FakePage main = Open(schedule, new FakePage());

		Assert.Null(schedule.Pending);
		schedule.SecretCopied(main);

		Assert.Equal(new PendingClipboardClear(1, 41, WaitsForFocus: false), schedule.Pending);
	}

	[Fact]
	public void AtExit_TheSecret_IsClearedThroughTheSystem_WhenNothingWroteSince()
	{
		FakeSystemClipboard system = new();
		using ClipboardClearSchedule schedule = Schedule(system);
		FakePage main = Open(schedule, new FakePage());

		schedule.SecretCopied(main);

		Assert.Equal(SystemClipboardClear.Cleared, schedule.ClearNow(TimeSpan.FromSeconds(1)));
		Assert.Equal(1, system.Empties);
		Assert.Null(schedule.Pending);
		Assert.Null(schedule.ClearNow(TimeSpan.FromSeconds(1)));
	}

	[Fact]
	public void AtExit_AnotherProgramsCopy_IsLeftOnTheClipboard()
	{
		FakeSystemClipboard system = new();
		using ClipboardClearSchedule schedule = Schedule(system);
		FakePage main = Open(schedule, new FakePage());

		schedule.SecretCopied(main);
		system.WrittenElsewhere();

		Assert.Equal(SystemClipboardClear.Changed, schedule.ClearNow(TimeSpan.FromSeconds(1)));
		Assert.Equal(0, system.Empties);
		Assert.Null(schedule.Pending);
	}

	[Fact]
	public void AtExit_APlainCopyOfOurs_LeavesNothingToClear()
	{
		FakeSystemClipboard system = new();
		using ClipboardClearSchedule schedule = Schedule(system);
		FakePage main = Open(schedule, new FakePage());

		schedule.SecretCopied(main);
		schedule.TextCopied();

		Assert.Null(schedule.Pending);
		Assert.Null(schedule.ClearNow(TimeSpan.FromSeconds(1)));
		Assert.Equal(0, system.Empties);
	}

	[Fact]
	public async Task AtExit_AClearAPageAlreadyMade_LeavesNothingToClear()
	{
		FakeSystemClipboard system = new();
		using ClipboardClearSchedule schedule = Schedule(system);
		FakePage main = Open(schedule, new FakePage());

		schedule.SecretCopied(main);
		_time.Advance(ClearTime);
		await Eventually.TrueAsync(() => main.Clears.SequenceEqual([1L]), "the main window clears");

		Assert.Null(schedule.Pending);
		Assert.Null(schedule.ClearNow(TimeSpan.FromSeconds(1)));
		Assert.Equal(0, system.Empties);
	}

	[Fact]
	public void AtExit_WithClearSecondsAtZero_TheSecretStays()
	{
		_settings.Set(new SecuritySettings { ClipboardClearSeconds = 0 });
		FakeSystemClipboard system = new();
		using ClipboardClearSchedule schedule = Schedule(system);
		FakePage main = Open(schedule, new FakePage());

		schedule.SecretCopied(main);

		Assert.Null(schedule.Pending);
		Assert.Null(schedule.ClearNow(TimeSpan.FromSeconds(1)));
		Assert.Equal(0, system.Empties);
	}

	// Without a number from right after the copy, nothing could say the clipboard still holds our secret.
	[Fact]
	public void AtExit_AClipboardWhoseNumberCouldNotBeRead_IsLeftAlone()
	{
		FakeSystemClipboard system = new();
		system.Unreadable();
		using ClipboardClearSchedule schedule = Schedule(system);
		FakePage main = Open(schedule, new FakePage());

		schedule.SecretCopied(main);

		Assert.Equal(new PendingClipboardClear(1, null, WaitsForFocus: false), schedule.Pending);
		Assert.Null(schedule.ClearNow(TimeSpan.FromSeconds(1)));
		Assert.Equal(0, system.Empties);
	}

	[Fact]
	public void AtExit_WithoutASystemClipboard_NothingCanBeCleared()
	{
		using ClipboardClearSchedule schedule = Schedule();
		FakePage main = Open(schedule, new FakePage());

		schedule.SecretCopied(main);

		Assert.Equal(new PendingClipboardClear(1, null, WaitsForFocus: false), schedule.Pending);
		Assert.Null(schedule.ClearNow(TimeSpan.FromSeconds(1)));
	}

	[Fact]
	public void AtExit_ASecondSecret_IsTheOneCompared()
	{
		FakeSystemClipboard system = new();
		using ClipboardClearSchedule schedule = Schedule(system);
		FakePage main = Open(schedule, new FakePage());

		schedule.SecretCopied(main);
		system.WrittenElsewhere();
		schedule.SecretCopied(main);

		Assert.Equal(new PendingClipboardClear(2, 42, WaitsForFocus: false), schedule.Pending);
		Assert.Equal(SystemClipboardClear.Cleared, schedule.ClearNow(TimeSpan.FromSeconds(1)));
	}

	// The user copied a password to paste it into another window, which is exactly when the page may not write.
	[Fact]
	public async Task APageWaitingForFocus_LeavesTheClearToTheSystem_AndDropsItsWait()
	{
		FakeSystemClipboard system = new();
		using ClipboardClearSchedule schedule = Schedule(system);
		FakePage main = Open(schedule, new FakePage { WaitsForFocus = true });

		schedule.SecretCopied(main);
		_time.Advance(ClearTime);

		await Eventually.TrueAsync(() => system.Empties == 1, "the system clipboard is cleared at the deadline");
		await Eventually.TrueAsync(() => main.Forgets.SequenceEqual([2L]), "the page drops the clear it was holding");
		Assert.Equal([1L], main.Clears);
		Assert.Null(schedule.Pending);
	}

	[Fact]
	public async Task APageWaitingForFocus_WithoutASystemClipboard_KeepsTheClearDue()
	{
		using ClipboardClearSchedule schedule = Schedule();
		FakePage main = Open(schedule, new FakePage());
		FakePage tool = Open(schedule, new FakePage { WaitsForFocus = true });

		schedule.SecretCopied(tool);
		_time.Advance(ClearTime);

		await Eventually.TrueAsync(
			() => schedule.Pending == new PendingClipboardClear(1, null, WaitsForFocus: true),
			"the clear waits in the tool window");
		Assert.Empty(main.Clears);
	}

	// Its wait for focus goes with the page, so the clear is handed to a page that stays.
	[Fact]
	public async Task APageWaitingForFocus_ThatCloses_HandsTheClearOn()
	{
		using ClipboardClearSchedule schedule = Schedule();
		FakePage main = Open(schedule, new FakePage());
		FakePage tool = Open(schedule, new FakePage { WaitsForFocus = true });

		schedule.SecretCopied(tool);
		_time.Advance(ClearTime);
		await Eventually.TrueAsync(() => schedule.Pending is { WaitsForFocus: true }, "the clear waits in the tool window");

		schedule.Unregister(tool);

		await Eventually.TrueAsync(() => main.Clears.SequenceEqual([1L]), "the main window clears");
		Assert.Null(schedule.Pending);
	}

	[Fact]
	public async Task WithNoPageLeft_TheSystemClipboardClears()
	{
		FakeSystemClipboard system = new();
		using ClipboardClearSchedule schedule = Schedule(system);
		FakePage tool = Open(schedule, new FakePage());

		schedule.SecretCopied(tool);
		schedule.Unregister(tool);
		_time.Advance(ClearTime);

		await Eventually.TrueAsync(() => system.Empties == 1, "the system clipboard is cleared");
		Assert.Null(schedule.Pending);
	}

	[Fact]
	public async Task WithNoPageLeft_AnotherProgramsCopy_IsLeftOnTheClipboard()
	{
		FakeSystemClipboard system = new();
		using ClipboardClearSchedule schedule = Schedule(system);
		FakePage tool = Open(schedule, new FakePage());

		schedule.SecretCopied(tool);
		schedule.Unregister(tool);
		system.WrittenElsewhere();
		_time.Advance(ClearTime);

		await Eventually.TrueAsync(() => schedule.Pending is null, "the clear is over");
		Assert.Equal(0, system.Empties);
	}

	// Another program holding the clipboard open the whole time is no reason to give up: the window's close tries again.
	[Fact]
	public async Task ABusySystemClipboard_KeepsTheClearDue_ForTheWindowsClose()
	{
		FakeSystemClipboard system = new();
		system.Answer(SystemClipboardClear.Busy);
		using ClipboardClearSchedule schedule = Schedule(system);
		FakePage tool = Open(schedule, new FakePage());

		schedule.SecretCopied(tool);
		schedule.Unregister(tool);
		_time.Advance(ClearTime);
		await Eventually.StaysTrueAsync(() => schedule.Pending is not null, "the clear stays due");

		system.Answer(null);

		Assert.Equal(SystemClipboardClear.Cleared, schedule.ClearNow(TimeSpan.FromSeconds(1)));
		Assert.Equal(1, system.Empties);
	}

	private static FakePage Open(ClipboardClearSchedule schedule, FakePage page)
	{
		schedule.Register(page);
		return page;
	}

	private ClipboardClearSchedule Schedule(ISystemClipboard? system = null) => new(_time, _settings, NullLoggerFactory.Instance, system);

	/// <summary>
	/// The operating system's clipboard as a number that moves on every write, the way Windows' sequence number does. A
	/// clear compares, empties and moves it, or answers what the test forces.
	/// </summary>
	private sealed class FakeSystemClipboard : ISystemClipboard
	{
		private readonly Lock _lock = new();
		private uint _number = 41;
		private int _empties;
		private SystemClipboardClear? _forced;
		private bool _readable = true;

		public int Empties
		{
			get
			{
				lock (_lock)
				{
					return _empties;
				}
			}
		}

		public uint? ContentNumber
		{
			get
			{
				lock (_lock)
				{
					return _readable ? _number : null;
				}
			}
		}

		/// <summary>Another program wrote the clipboard.</summary>
		public void WrittenElsewhere()
		{
			lock (_lock)
			{
				_number++;
			}
		}

		public void Unreadable()
		{
			lock (_lock)
			{
				_readable = false;
			}
		}

		/// <summary>Every clear answers <paramref name="answer"/> until it is set back to null.</summary>
		public void Answer(SystemClipboardClear? answer)
		{
			lock (_lock)
			{
				_forced = answer;
			}
		}

		public SystemClipboardClear EmptyIfUnchanged(uint contentNumber, TimeSpan timeout)
		{
			lock (_lock)
			{
				if (_forced is { } forced)
				{
					return forced;
				}

				if (contentNumber != _number)
				{
					return SystemClipboardClear.Changed;
				}

				_empties++;
				_number++;
				return SystemClipboardClear.Cleared;
			}
		}
	}

	/// <summary>A page that records what the schedule asked of it, and can fail or never answer.</summary>
	private sealed class FakePage : IClipboardClearer
	{
		private readonly Lock _lock = new();
		private readonly List<long> _clears = [];
		private readonly List<long> _forgets = [];
		private int _gaveUp;

		public Exception? Failure { get; init; }

		public bool Hangs { get; init; }

		/// <summary>The page is in the background, so its script holds the clear until it is focused again.</summary>
		public bool WaitsForFocus { get; init; }

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

		public async ValueTask<bool> ClearAsync(long copy, CancellationToken cancellationToken)
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

			return !WaitsForFocus;
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

			// clearClipboard answers whether it cleared at once, which a focused page does.
			return ValueTask.FromResult(typeof(TValue) == typeof(bool) ? (TValue)(object)true : default(TValue)!);
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
