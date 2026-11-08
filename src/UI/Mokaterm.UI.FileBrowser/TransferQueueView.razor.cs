using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Moka.Red.Core.Icons;
using Moka.Red.Feedback.JobList;
using Mokaterm.Abstractions.Transfers;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.Common.Icons;

namespace Mokaterm.UI.FileBrowser;

/// <summary>
/// Lists the transfer queue newest first, with progress, rate, cancel, retry and remove, through a Moka job list. Progress
/// events are coalesced so the view renders about four times a second at most.
/// </summary>
public partial class TransferQueueView : ComponentBase, IDisposable
{
	// A list with slots redraws every row each time it draws, and a folder upload queues thousands, so a long list goes a
	// page at a time. MaxVisible would not do: its button shows all the rest at once.
	private const int PagedAbove = 100;
	private const int JobsPerPage = 25;

	// Estimates above this are noise from a stalled rate and would not fit the duration format.
	private const double MaxEstimateSeconds = 100 * 3600;

	private static readonly TimeSpan RenderInterval = TimeSpan.FromMilliseconds(250);

	// One instance each: the list draws every row again when either of these changes.
	private static readonly Func<double, string> WriteBytes = bytes => DisplayFormat.Bytes((long)bytes);

	private static readonly MokaJobListText Words = new()
	{
		ListLabel = "Transfers",
		Remove = name => "Remove " + name + " from the list",
		TimeLeft = left => DisplayFormat.Duration(left) + " left",
	};

	private readonly CancellationTokenSource _lifetime = new();
	private int _page = 1;
	private int _renderScheduled;
	private long _lastRenderTimestamp;
	private bool _disposed;

	[Inject]
	private ITransferQueue Queue { get; set; } = default!;

	[Inject]
	private TimeProvider TimeProvider { get; set; } = default!;

	[Inject]
	private ILogger<TransferQueueView> Logger { get; set; } = default!;

	/// <summary>Tighter rows and a one-line empty state, for the bottom panel.</summary>
	[Parameter]
	public bool Compact { get; set; }

	/// <summary>False where the surroundings already say what this is, such as a dock panel with its own header.</summary>
	[Parameter]
	public bool ShowTitle { get; set; } = true;

	[Parameter]
	public string? Class { get; set; }

	[Parameter]
	public string? Style { get; set; }

	private string RootClass => $"tq{(Compact ? " tq--compact" : "")} {Class}".TrimEnd();

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		if (disposing)
		{
			Queue.Changed -= OnQueueChanged;
			_lifetime.Cancel();
			_lifetime.Dispose();
		}
	}

	protected override void OnInitialized() => Queue.Changed += OnQueueChanged;

	protected override void OnAfterRender(bool firstRender) =>
		Interlocked.Exchange(ref _lastRenderTimestamp, TimeProvider.GetTimestamp());

	private void OnQueueChanged()
	{
		if (Interlocked.Exchange(ref _renderScheduled, 1) == 0)
		{
			_ = RenderSoonAsync();
		}
	}

	private async Task RenderSoonAsync()
	{
		try
		{
			TimeSpan wait = RenderInterval - TimeProvider.GetElapsedTime(Interlocked.Read(ref _lastRenderTimestamp));
			if (wait > TimeSpan.Zero)
			{
				await Task.Delay(wait, TimeProvider, _lifetime.Token);
			}

			// Cleared before rendering, so a change that arrives during the render schedules the next one.
			Interlocked.Exchange(ref _renderScheduled, 0);
			await InvokeAsync(StateHasChanged);
		}
		catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
		{
			// The view was disposed while a render was pending.
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "The transfer list failed to update");
		}
	}

	private QueueSnapshot TakeSnapshot()
	{
		List<ITransferItem> items = [.. Queue.Items];
		int running = 0;
		int queued = 0;
		int failed = 0;
		int completed = 0;
		int canceled = 0;
		foreach (ITransferItem item in items)
		{
			switch (item.State)
			{
				case TransferState.Running:
					running++;
					break;
				case TransferState.Queued:
					queued++;
					break;
				case TransferState.Failed:
					failed++;
					break;
				case TransferState.Completed:
					completed++;
					break;
				default:
					canceled++;
					break;
			}
		}

		List<ITransferItem> shown = ShownPage(items);
		return new QueueSnapshot(items, [.. shown.Select(ToJob)], shown.ToDictionary(JobId), running, queued, failed, completed, canceled);
	}

	private List<ITransferItem> ShownPage(List<ITransferItem> items)
	{
		if (items.Count <= PagedAbove)
		{
			_page = 1;
			return items;
		}

		// A queue runs from its oldest, which is listed last, so what needs a look would otherwise be many pages in. That
		// includes a canceled transfer, whose Retry is how it picks up again.
		List<ITransferItem> ordered = [.. items.Where(NeedsALook), .. items.Where(item => !NeedsALook(item))];
		_page = Math.Clamp(_page, 1, ((ordered.Count - 1) / JobsPerPage) + 1);
		int first = (_page - 1) * JobsPerPage;
		return ordered.GetRange(first, Math.Min(JobsPerPage, ordered.Count - first));
	}

	private static bool NeedsALook(ITransferItem item) => item.State is TransferState.Running or TransferState.Failed or TransferState.Canceled;

	private void Cancel(MokaJob job) => Run(() => Queue.Cancel(Guid.Parse(job.Id)));

	private void Retry(MokaJob job) => Run(() => Queue.Retry(Guid.Parse(job.Id)));

	private void Remove(MokaJob job) => Run(() => Queue.Remove(Guid.Parse(job.Id)));

	private void ClearFinished() => Run(Queue.ClearFinished);

	private void Run(Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex, "A transfer queue action failed");
		}
	}

	private static string CountLabel(int count, string label) =>
		string.Create(CultureInfo.CurrentCulture, $"{count:N0} {label}");

	private static MokaIconDefinition DirectionIcon(ITransferItem item) => item.Direction switch
	{
		TransferDirection.Upload => MokatermIcons.FileUpload,
		TransferDirection.Download => MokatermIcons.FileDownload,
		_ => MokatermIcons.Transfers,
	};

	private static string JobId(ITransferItem item) => item.Id.ToString("N");

	private static MokaJob ToJob(ITransferItem item)
	{
		bool checking = item.Verification == TransferVerification.Checking;
		return new MokaJob(JobId(item), item.Name, StateOf(item))
		{
			Icon = DirectionIcon(item),
			Detail = checking ? Route(item) + Words.Separator + "checking the copy" : Route(item),

			// The server's checksum gives no number to show, so with no figure for what is done the bar slides, as it does
			// before the size is known.
			Bytes = checking && item.TotalBytes is not null ? null : item.TransferredBytes,
			TotalBytes = item.TotalBytes,
			BytesPerSecond = checking || item.BytesPerSecond <= 0 ? null : item.BytesPerSecond,
			TimeRemaining = checking ? null : Remaining(item),
			Error = item.State == TransferState.Failed ? FailureText(item) : null,
		};
	}

	private static MokaJobState StateOf(ITransferItem item) => item.State switch
	{
		TransferState.Queued => MokaJobState.Queued,
		TransferState.Running => MokaJobState.Running,
		TransferState.Completed => MokaJobState.Done,
		TransferState.Failed => MokaJobState.Failed,
		_ => MokaJobState.Canceled,
	};

	private static string Route(ITransferItem item)
	{
		string route = item.Source + " -> " + item.Destination;
		return string.IsNullOrEmpty(item.Group) || route.Contains(item.Group, StringComparison.Ordinal) ? route : item.Group + ": " + route;
	}

	// A failed job's second line is its error alone, so the rest of what a row says goes after it: several failures can
	// share a file name, and only the route tells them apart.
	private static string FailureText(ITransferItem item) => string.Join(
		Words.Separator,
		string.IsNullOrEmpty(item.Error) ? Words.NoReason : item.Error,
		Route(item),
		BytesText(item));

	private static string ResumedText(long offset) => "Continued from " + DisplayFormat.Bytes(offset) + " instead of starting over";

	private static string BytesText(ITransferItem item) => item.TotalBytes is { } total
		? DisplayFormat.Bytes(item.TransferredBytes) + " of " + DisplayFormat.Bytes(total)
		: DisplayFormat.Bytes(item.TransferredBytes);

	private static TimeSpan? Remaining(ITransferItem item)
	{
		if (item.BytesPerSecond <= 0 || item.TotalBytes is not { } total || total <= item.TransferredBytes)
		{
			return null;
		}

		double seconds = (total - item.TransferredBytes) / item.BytesPerSecond;
		return seconds <= MaxEstimateSeconds ? TimeSpan.FromSeconds(seconds) : null;
	}

	private sealed record QueueSnapshot(
		List<ITransferItem> Items,
		IReadOnlyList<MokaJob> Jobs,
		Dictionary<string, ITransferItem> Shown,
		int Running,
		int Queued,
		int Failed,
		int Completed,
		int Canceled)
	{
		public int Finished => Failed + Completed + Canceled;
	}
}
