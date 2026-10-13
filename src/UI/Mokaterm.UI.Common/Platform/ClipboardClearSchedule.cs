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
/// </summary>
internal sealed class ClipboardClearSchedule : IDisposable
{
	/// <summary>How long a page has to answer before the clear moves on: a page on its way out may never answer.</summary>
	internal static readonly TimeSpan PageAnswerLimit = TimeSpan.FromSeconds(5);

	private readonly TimeProvider _time;
	private readonly ISettingsService _settings;
	private readonly ILogger _logger;
	private readonly Lock _lock = new();
	private readonly List<IClipboardClearer> _pages = [];
	private readonly HashSet<IClipboardClearer> _asked = [];
	private ITimer? _timer;
	private IClipboardClearer? _copiedBy;
	private long _copies;
	private bool _disposed;

	public ClipboardClearSchedule(TimeProvider timeProvider, ISettingsService settings, ILoggerFactory loggerFactory)
	{
		_time = timeProvider;
		_settings = settings;
		_logger = loggerFactory.CreateLogger<ClipboardClearSchedule>();
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
		lock (_lock)
		{
			_ = _pages.Remove(page);
			_ = _asked.Remove(page);
			if (ReferenceEquals(_copiedBy, page))
			{
				_copiedBy = null;
			}
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
				if (_disposed || copy != _copies)
				{
					return;
				}

				page = NextPage(passedOver);
				if (page is null)
				{
					EndClear();
				}
				else
				{
					_ = _asked.Add(page);
				}
			}

			if (page is null)
			{
				_logger.LogWarning("A copied secret stays on the clipboard: no page of this window is left to clear it.");
				return;
			}

			if (await TryClearAsync(page, copy))
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

			passedOver.Add(page);
		}
	}

	private async Task<bool> TryClearAsync(IClipboardClearer page, long copy)
	{
		using CancellationTokenSource limit = new(PageAnswerLimit, _time);
		try
		{
			await page.ClearAsync(copy, limit.Token);
			return true;
		}
		catch (Exception ex) when (JsModule.IsTeardown(ex))
		{
			// The page closed under the call or did not answer in time; another page can still clear.
			return false;
		}
		catch (Exception ex)
		{
			_logger.LogWarning("A page could not clear the clipboard ({Failure}); trying another.", LogSafe.Describe(ex));
			return false;
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

	private void EndClear()
	{
		_timer?.Dispose();
		_timer = null;
		_copiedBy = null;
	}
}
