using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Interop;

namespace Mokaterm.UI.Common.Platform;

/// <summary>
/// When a copied secret leaves the clipboard, for a window and every tool window it opened. The clipboard belongs to the
/// machine, not to the page that wrote it, so the deadline cannot live in a page: a tool window that closes before it
/// would leave the secret behind for good. This service is window state and reaches no page itself, so tool windows
/// borrow the main window's instance, and the clear goes through whichever of those pages is still open.
/// <para>
/// Every write through <see cref="JsClipboardService"/> is numbered. Only the newest copy is ever cleared, and the pages a
/// clear was sent to hear of every newer copy, so a clear still waiting for focus in one window cannot wipe what was
/// copied later in another.
/// </para>
/// <para>
/// Where the host has an <see cref="ISystemClipboard"/> (the desktop), the clipboard's number is recorded with each
/// secret copy, and a clear that no page can carry out goes through the system instead, but only while that number is
/// unchanged: when the app shuts down before the deadline (<see cref="ClearNow"/>), when a page put the clear off until
/// it is focused again, and when no page of the window is left at all.
/// </para>
/// </summary>
internal sealed class ClipboardClearSchedule : IPendingClipboardClear, IDisposable
{
	/// <summary>How long a page has to answer before the clear moves on: a page on its way out may never answer.</summary>
	internal static readonly TimeSpan PageAnswerLimit = TimeSpan.FromSeconds(5);

	/// <summary>How long a clear through the system clipboard keeps trying while another program holds it open.</summary>
	internal static readonly TimeSpan SystemClearLimit = TimeSpan.FromSeconds(2);

	private readonly TimeProvider _time;
	private readonly ISettingsService _settings;
	private readonly ISystemClipboard? _system;
	private readonly ILogger _logger;
	private readonly Lock _lock = new();
	private readonly List<IClipboardClearer> _pages = [];
	private readonly HashSet<IClipboardClearer> _asked = [];
	private ITimer? _timer;
	private IClipboardClearer? _copiedBy;
	private IClipboardClearer? _heldBy;
	private uint? _contentNumber;
	private bool _owed;
	private long _copies;
	private bool _disposed;

	public ClipboardClearSchedule(
		TimeProvider timeProvider,
		ISettingsService settings,
		ILoggerFactory loggerFactory,
		ISystemClipboard? systemClipboard = null)
	{
		_time = timeProvider;
		_settings = settings;
		_system = systemClipboard;
		_logger = loggerFactory.CreateLogger<ClipboardClearSchedule>();
	}

	/// <summary>The secret whose clear is still owed, or null when nothing on the clipboard is ours to clear.</summary>
	public PendingClipboardClear? Pending
	{
		get
		{
			lock (_lock)
			{
				return _owed ? new PendingClipboardClear(_copies, _contentNumber, _heldBy is not null) : null;
			}
		}
	}

	/// <summary>A page opened that can clear the clipboard.</summary>
	public void Register(IClipboardClearer page)
	{
		ArgumentNullException.ThrowIfNull(page);
		lock (_lock)
		{
			if (!_disposed && !_pages.Contains(page))
			{
				_pages.Add(page);
			}
		}
	}

	/// <summary>A page is going away; a pending clear goes through another one.</summary>
	public void Unregister(IClipboardClearer page)
	{
		long? reclear = null;
		lock (_lock)
		{
			_ = _pages.Remove(page);
			_ = _asked.Remove(page);
			if (ReferenceEquals(_copiedBy, page))
			{
				_copiedBy = null;
			}

			// The page was waiting for focus to clear, and its wait goes with it.
			if (ReferenceEquals(_heldBy, page))
			{
				_heldBy = null;
				reclear = _owed && !_disposed ? _copies : null;
			}
		}

		if (reclear is { } copy)
		{
			_ = ClearAsync(copy);
		}
	}

	/// <summary>
	/// A secret was written through <paramref name="page"/>. It is cleared after <c>SecuritySettings.ClipboardClearSeconds</c>,
	/// or never when that is 0, and either way a clear still pending for an earlier copy is dropped.
	/// </summary>
	public void SecretCopied(IClipboardClearer page)
	{
		ArgumentNullException.ThrowIfNull(page);
		int seconds = _settings.Get<SecuritySettings>().ClipboardClearSeconds;

		// Read now, once the page has taken the write: from here on any change means the secret was replaced.
		uint? contentNumber = seconds > 0 ? _system?.ContentNumber : null;
		long copy;
		IClipboardClearer[] asked;
		lock (_lock)
		{
			if (_disposed)
			{
				return;
			}

			(copy, asked) = NextCopy();
			if (seconds > 0)
			{
				_copiedBy = page;
				_owed = true;
				_contentNumber = contentNumber;
				_timer = _time.CreateTimer(OnDeadline, copy, TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan);
			}
		}

		Forget(asked, copy);
	}

	/// <summary>Something that is not a secret replaced the clipboard, so a pending clear would only wipe it.</summary>
	public void TextCopied()
	{
		long copy;
		IClipboardClearer[] asked;
		lock (_lock)
		{
			if (_disposed)
			{
				return;
			}

			(copy, asked) = NextCopy();
		}

		Forget(asked, copy);
	}

	public SystemClipboardClear? ClearNow(TimeSpan timeout)
	{
		long copy;
		uint contentNumber;
		lock (_lock)
		{
			if (_system is null || !_owed || _contentNumber is not { } recorded)
			{
				return null;
			}

			copy = _copies;
			contentNumber = recorded;
		}

		SystemClipboardClear result = ClearThroughSystem(copy, contentNumber, timeout);
		_logger.LogInformation("A copied secret's clear was still due and went through the system clipboard: {Result}.", result);
		return result;
	}

	public void Dispose()
	{
		lock (_lock)
		{
			_disposed = true;
			EndClear();
			_pages.Clear();
			_asked.Clear();
		}
	}

	// A timer callback cannot be awaited; ClearAsync catches everything, so no faulted task is left behind.
	private void OnDeadline(object? state) => _ = ClearAsync((long)state!);

	private async Task ClearAsync(long copy)
	{
		List<IClipboardClearer> passedOver = [];
		while (true)
		{
			IClipboardClearer? page;
			lock (_lock)
			{
				// Checked before every page, because a copy made while one page was being asked has a clear of its own.
				if (_disposed || copy != _copies || !_owed)
				{
					return;
				}

				page = NextPage(passedOver);
				if (page is not null)
				{
					_ = _asked.Add(page);
				}
			}

			if (page is null)
			{
				await ClearWithoutPageAsync(copy);
				return;
			}

			PageAnswer answer = await TryClearAsync(page, copy);
			if (answer == PageAnswer.Cleared)
			{
				lock (_lock)
				{
					if (copy == _copies)
					{
						EndClear();
					}
				}

				return;
			}

			if (answer == PageAnswer.WaitsForFocus && Hold(page, copy))
			{
				await ClearWithoutPageAsync(copy);
				return;
			}

			passedOver.Add(page);
		}
	}

	// The page keeps the clear until it is focused, which may be never: the user copied a password to paste it elsewhere.
	// The system clipboard has no such limit, so where there is one it clears now and the page drops its wait.
	private bool Hold(IClipboardClearer page, long copy)
	{
		lock (_lock)
		{
			// A page that went away while it answered took its wait with it; the next one is asked instead.
			if (copy != _copies || !_pages.Contains(page))
			{
				return false;
			}

			_heldBy = page;
			return true;
		}
	}

	/// <summary>
	/// The clear for <paramref name="copy"/> when no page can carry it out now: every page is gone or failed, or the one
	/// that answered waits for focus. Without a system clipboard that leaves the secret where it is.
	/// </summary>
	private async Task ClearWithoutPageAsync(long copy)
	{
		uint? contentNumber;
		IClipboardClearer? holder;
		lock (_lock)
		{
			if (_disposed || copy != _copies || !_owed)
			{
				return;
			}

			contentNumber = _contentNumber;
			holder = _heldBy;
			if (holder is null && (_system is null || contentNumber is null))
			{
				EndClear();
			}
		}

		if (_system is null || contentNumber is not { } number)
		{
			if (holder is null)
			{
				_logger.LogWarning("A copied secret stays on the clipboard: no page of this window is left to clear it.");
			}

			return;
		}

		// Off the caller's thread: emptying the clipboard can wait on the program that last wrote it.
		SystemClipboardClear result = await Task.Run(() => ClearThroughSystem(copy, number, SystemClearLimit));
		if (result is SystemClipboardClear.Busy or SystemClipboardClear.Failed)
		{
			// The page's wait, if there is one, is now the only clear left before the window closes.
			_logger.LogWarning("The system clipboard could not be cleared ({Result}); the clear stays due until the window closes.", result);
			return;
		}

		// The secret is gone either way, and a wait left in the page would only wipe whatever the clipboard holds when the
		// page is focused again. Forgetting the clears before the next number is forgetting this one.
		if (holder is not null)
		{
			await ForgetAsync(holder, copy + 1);
		}
	}

	private SystemClipboardClear ClearThroughSystem(long copy, uint contentNumber, TimeSpan timeout)
	{
		SystemClipboardClear result;
		try
		{
			result = _system!.EmptyIfUnchanged(contentNumber, timeout);
		}
		catch (Exception ex)
		{
			// The contract says it never throws; a host that breaks it must not take the caller down with it.
			_logger.LogWarning("Clearing the system clipboard failed ({Failure}).", LogSafe.Describe(ex));
			result = SystemClipboardClear.Failed;
		}

		// Either the secret is gone or something else replaced it; a busy or refusing clipboard keeps the clear due.
		if (result is SystemClipboardClear.Cleared or SystemClipboardClear.Changed)
		{
			lock (_lock)
			{
				if (copy == _copies)
				{
					EndClear();
				}
			}
		}

		return result;
	}

	private async Task<PageAnswer> TryClearAsync(IClipboardClearer page, long copy)
	{
		using CancellationTokenSource limit = new(PageAnswerLimit, _time);
		try
		{
			return await page.ClearAsync(copy, limit.Token) ? PageAnswer.Cleared : PageAnswer.WaitsForFocus;
		}
		catch (Exception ex) when (JsModule.IsTeardown(ex))
		{
			// The page closed under the call or did not answer in time; another page can still clear.
			return PageAnswer.Failed;
		}
		catch (Exception ex)
		{
			_logger.LogWarning("A page could not clear the clipboard ({Failure}); trying another.", LogSafe.Describe(ex));
			return PageAnswer.Failed;
		}
	}

	private void Forget(IClipboardClearer[] pages, long copy)
	{
		foreach (IClipboardClearer page in pages)
		{
			_ = ForgetAsync(page, copy);
		}
	}

	private async Task ForgetAsync(IClipboardClearer page, long copy)
	{
		using CancellationTokenSource limit = new(PageAnswerLimit, _time);
		try
		{
			await page.ForgetClearsBeforeAsync(copy, limit.Token);
		}
		catch (Exception ex) when (JsModule.IsTeardown(ex))
		{
			// A page that is gone has no clear left waiting in it.
		}
		catch (Exception ex)
		{
			_logger.LogWarning("A page could not drop an older clipboard clear ({Failure}).", LogSafe.Describe(ex));
		}
	}

	// The page that copied first, because it is where the user copied and so the likeliest to be focused again, which a
	// page in the background waits for. Then the page opened last; the main window opened first and is the last resort.
	private IClipboardClearer? NextPage(List<IClipboardClearer> passedOver)
	{
		if (_copiedBy is { } copier && !passedOver.Contains(copier) && _pages.Contains(copier))
		{
			return copier;
		}

		for (int i = _pages.Count - 1; i >= 0; i--)
		{
			if (!passedOver.Contains(_pages[i]))
			{
				return _pages[i];
			}
		}

		return null;
	}

	private (long Copy, IClipboardClearer[] Asked) NextCopy()
	{
		EndClear();
		_copies++;
		IClipboardClearer[] asked = [.. _asked];
		_asked.Clear();
		return (_copies, asked);
	}

	// Nothing more is owed for the current copy.
	private void EndClear()
	{
		_timer?.Dispose();
		_timer = null;
		_copiedBy = null;
		_heldBy = null;
		_contentNumber = null;
		_owed = false;
	}

	private enum PageAnswer
	{
		Cleared,
		WaitsForFocus,
		Failed,
	}
}
