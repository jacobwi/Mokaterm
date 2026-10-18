using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;

namespace Mokaterm.Maui.Services;

/// <summary>
/// Hands a click on a system notification to the page that holds the sessions, which selects the session's tab. The
/// platform side brings the window forward first; this only reaches the page, which the window has one of. A click that
/// comes before the page is up, or after it has gone, has nothing to select.
/// </summary>
internal sealed class NotificationClicks
{
	private readonly Lock _lock = new();
	private readonly ILogger<NotificationClicks> _logger;
	private Func<NotificationClick, Task>? _page;

	public NotificationClicks(ILogger<NotificationClicks> logger) => _logger = logger;

	/// <summary>Called by the page's root component; disposing the result lets the page go again.</summary>
	/// <param name="page">Selects the click's session, on the page's own dispatcher.</param>
	public IDisposable Attach(Func<NotificationClick, Task> page)
	{
		ArgumentNullException.ThrowIfNull(page);
		lock (_lock)
		{
			_page = page;
		}

		return new Attachment(this, page);
	}

	/// <summary>Any thread. Never throws: a click the page cannot take is dropped with a log line.</summary>
	public void Deliver(NotificationClick click)
	{
		ArgumentNullException.ThrowIfNull(click);
		Func<NotificationClick, Task>? page;
		lock (_lock)
		{
			page = _page;
		}

		if (page is not null)
		{
			_ = DeliverAsync(page, click);
		}
	}

	private async Task DeliverAsync(Func<NotificationClick, Task> page, NotificationClick click)
	{
		try
		{
			await page(click);
		}
		catch (Exception ex)
		{
			_logger.LogWarning("A notification click could not select its session ({Failure}).", LogSafe.Describe(ex));
		}
	}

	private void Detach(Func<NotificationClick, Task> page)
	{
		lock (_lock)
		{
			if (ReferenceEquals(_page, page))
			{
				_page = null;
			}
		}
	}

	private sealed class Attachment(NotificationClicks owner, Func<NotificationClick, Task> page) : IDisposable
	{
		public void Dispose() => owner.Detach(page);
	}
}
