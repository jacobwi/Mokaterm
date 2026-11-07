using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Moka.Red.Core.Enums;
using Moka.Red.Core.Icons;
using Mokaterm.Abstractions.Transfers;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.Common.Icons;

namespace Mokaterm.UI.FileBrowser;

/// <summary>
/// Lists the transfer queue newest first, with progress, rate, cancel, retry and remove. Progress events are coalesced
/// so the view renders about four times a second at most.
/// </summary>
public partial class TransferQueueView : ComponentBase, IDisposable
{
	// Virtualize renders no rows until its script has measured the list, so short lists render plainly.
	private const int VirtualizeThreshold = 100;

	// First guesses for virtualization, which Virtualize corrects from the rows it draws: three lines of text with
	// padding, or one line as tall as the theme's small button.
	private const float RowHeight = 72;
	private const float CompactRowHeight = 32;

	// Estimates above this are noise from a stalled rate and would not fit the duration format.
	private const double MaxEstimateSeconds = 100 * 3600;

	private static readonly TimeSpan RenderInterval = TimeSpan.FromMilliseconds(250);

	private readonly CancellationTokenSource _lifetime = new();
	private int _renderScheduled;
	private long _lastRenderTimestamp;
	private bool _disposed;

	[Inject]
	private ITransferQueue Queue { get; set; } = default!;

	[Inject]
	private TimeProvider TimeProvider { get; set; } = default!;

	[Inject]
	private ILogger<TransferQueueView> Logger { get; set; } = default!;

	/// <summary>Single-line rows for the bottom panel.</summary>
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

		return new QueueSnapshot(items, running, queued, failed, completed, canceled);
	}

	private void Cancel(Guid id) => Run(() => Queue.Cancel(id));

	private void Retry(Guid id) => Run(() => Queue.Retry(id));

	private void Remove(Guid id) => Run(() => Queue.Remove(id));

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

	private static string Route(ITransferItem item) => item.Source + " -> " + item.Destination;

	private static string Tooltip(ITransferItem item)
	{
		string route = string.IsNullOrEmpty(item.Group) ? Route(item) : item.Group + ": " + Route(item);
		return item.State == TransferState.Failed && !string.IsNullOrEmpty(item.Error) ? route + "\n" + item.Error : route;
	}

	private static string ResumedText(long offset) => "Continued from " + DisplayFormat.Bytes(offset) + " instead of starting over";

	private static string CompactRowClass(ITransferItem item) =>
		item.State == TransferState.Running ? "tq-row tq-row--compact tq-row--running" : "tq-row tq-row--compact";

	// Whole percentages: the progress bar writes the value into its style with the current culture's decimal separator.
	private static double? ProgressValue(ITransferItem item) => item.State switch
	{
		TransferState.Completed => 100,
		_ when item.TotalBytes is { } total && total > 0 => Math.Round(Math.Clamp(100d * item.TransferredBytes / total, 0, 100)),
		_ => 0,
	};

	private static string BytesText(ITransferItem item) => item.TotalBytes is { } total
		? DisplayFormat.Bytes(item.TransferredBytes) + " of " + DisplayFormat.Bytes(total)
		: DisplayFormat.Bytes(item.TransferredBytes);

	private static bool IsChecking(ITransferItem item) => item.Verification == TransferVerification.Checking;

	// Said again by a screen reader each time it changes, so it names the step and never the numbers.
	private static string JobText(ITransferItem item) => IsChecking(item)
		? "Checking the copy"
		: item.Direction switch
		{
			TransferDirection.Upload => "Uploading",
			TransferDirection.Download => "Downloading",
			_ => "Copying",
		};

	// The server's checksum gives no number to show, so the bar slides as it does before the size is known.
	private static double? JobPercent(ITransferItem item) =>
		!IsChecking(item) && item.TotalBytes is { } total && total > 0
			? Math.Clamp(100d * item.TransferredBytes / total, 0, 100)
			: null;

	// Written here rather than through the job's Bytes and BytesPerSecond, which count in thousands where every other
	// size in the app counts in 1024s.
	private static List<string> JobFacts(ITransferItem item)
	{
		List<string> facts = [];
		if (item.TotalBytes is not null || item.TransferredBytes > 0)
		{
			facts.Add(BytesText(item));
		}

		if (IsChecking(item) || item.BytesPerSecond <= 0)
		{
			return facts;
		}

		facts.Add(DisplayFormat.Rate(item.BytesPerSecond));
		if (item.TotalBytes is { } total && total > item.TransferredBytes)
		{
			double seconds = (total - item.TransferredBytes) / item.BytesPerSecond;
			if (seconds <= MaxEstimateSeconds)
			{
				facts.Add(DisplayFormat.Duration(TimeSpan.FromSeconds(seconds)) + " left");
			}
		}

		return facts;
	}

	private static MokaColor StateColor(ITransferItem item) => item.State switch
	{
		TransferState.Running => MokaColor.Primary,
		TransferState.Completed => MokaColor.Success,
		TransferState.Failed => MokaColor.Error,
		TransferState.Queued => MokaColor.Info,
		_ => MokaColor.Surface,
	};

	private static string StateLabel(ITransferItem item) => item.State switch
	{
		TransferState.Queued => "Queued",
		TransferState.Running when item.Verification == TransferVerification.Checking => "Verifying",
		TransferState.Running => "Running",
		TransferState.Completed => "Done",
		TransferState.Failed => "Failed",
		_ => "Canceled",
	};

	private sealed record QueueSnapshot(List<ITransferItem> Items, int Running, int Queued, int Failed, int Completed, int Canceled)
	{
		public int Finished => Failed + Completed + Canceled;
	}
}
