using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Transfers;

namespace Mokaterm.UI.FileBrowser.Tests;

/// <summary>
/// Every transfer is two lines of a Moka job list: the name with its badges and buttons, then the route with the numbers.
/// Sizes count in 1024s, as everywhere else in the app.
/// </summary>
public sealed class TransferJobRenderTests
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ARunningTransfer_SaysHowFar_InBinaryUnits_AndCanBeCanceled(bool compact)
	{
		string html = await RenderAsync(compact, new Item { State = TransferState.Running, TransferredBytes = 1024, BytesPerSecond = 2048 });

		Assert.Contains("moka-job-list__item--running", html, StringComparison.Ordinal);
		Assert.Contains("aria-valuenow=\"25\"", html, StringComparison.Ordinal);
		Assert.Contains("backup.tar -> web-1:/srv/backup.tar, 1 KB of 4 KB, 2 KB/s, 00:01 left", html, StringComparison.Ordinal);
		Assert.Contains("Cancel backup.tar", Tooltips.Texts(html));
		Assert.DoesNotContain("Retry backup.tar", Tooltips.Texts(html));
		Assert.DoesNotContain("Remove backup.tar from the list", Tooltips.Texts(html));
	}

	[Fact]
	public async Task CheckingTheCopy_HasNoNumber_SoTheBarSlides()
	{
		string html = await RenderAsync(compact: true, new Item
		{
			State = TransferState.Running,
			TransferredBytes = 4096,
			BytesPerSecond = 2048,
			Verification = TransferVerification.Checking,
		});

		Assert.Contains("moka-job-list__track--waiting", html, StringComparison.Ordinal);
		Assert.Contains("checking the copy, 4 KB", html, StringComparison.Ordinal);
		Assert.DoesNotContain("KB/s", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AResumedTransfer_SaysWhereItPickedUp()
	{
		string html = await RenderAsync(compact: true, new Item { State = TransferState.Running, TransferredBytes = 2048, ResumedFrom = 1024 });

		Assert.Contains(">Resumed<", html, StringComparison.Ordinal);
		Assert.Contains("Continued from 1 KB instead of starting over", Tooltips.Texts(html));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task AFinishedTransfer_SaysItsSize_AndThatItWasNotChecked(bool compact)
	{
		string html = await RenderAsync(compact, new Item
		{
			State = TransferState.Completed,
			TransferredBytes = 4096,
			Verification = TransferVerification.Unavailable,
		});

		Assert.DoesNotContain("moka-job-list__track", html, StringComparison.Ordinal);
		Assert.Contains("4 KB, Done", html, StringComparison.Ordinal);
		Assert.Contains(">Unchecked<", html, StringComparison.Ordinal);
		Assert.Contains("The server cannot compute checksums, so this copy was not checked", Tooltips.Texts(html));
		Assert.Contains("Remove backup.tar from the list", Tooltips.Texts(html));
	}

	[Fact]
	public async Task AVerifiedRootTransfer_WearsBothBadges()
	{
		string html = await RenderAsync(compact: true, new Item
		{
			State = TransferState.Completed,
			TransferredBytes = 4096,
			Verification = TransferVerification.Verified,
			Elevated = true,
		});

		Assert.Contains(">Root<", html, StringComparison.Ordinal);
		Assert.Contains(">Verified<", html, StringComparison.Ordinal);
		Assert.Contains("The server's checksum matches the copy", Tooltips.Texts(html));
	}

	[Fact]
	public async Task ACanceledTransfer_SaysHowFarItGot_AndCanBeRetried()
	{
		string html = await RenderAsync(compact: true, new Item { State = TransferState.Canceled, TransferredBytes = 1024 });

		Assert.Contains("1 KB of 4 KB, Canceled", html, StringComparison.Ordinal);
		Assert.Contains("Retry backup.tar", Tooltips.Texts(html));
		Assert.Contains("Remove backup.tar from the list", Tooltips.Texts(html));
	}

	[Fact]
	public async Task TheGroup_ShowsOnlyWhenTheRouteDoesNotAlreadySayIt()
	{
		string same = await RenderAsync(compact: true, new Item { State = TransferState.Queued });
		string other = await RenderAsync(compact: true, new Item { State = TransferState.Queued, Group = "Build server" });

		Assert.DoesNotContain("web-1: ", same, StringComparison.Ordinal);
		Assert.Contains("Build server: backup.tar -> web-1:/srv/backup.tar", other, StringComparison.Ordinal);
	}

	// A folder upload queues thousands, and a list with slots draws every row each time, so what it is given stays bounded.
	[Fact]
	public async Task ALongQueue_GoesAPageAtATime_WhileTheCountsCoverAllOfIt()
	{
		string html = await RenderAsync(compact: true, [.. Enumerable.Range(0, 120).Select(_ => new Item { State = TransferState.Queued })]);

		Assert.Equal(25, Regex.Count(html, "class=\"moka-job-list__item "));
		Assert.Contains("moka-pagination", html, StringComparison.Ordinal);
		Assert.Contains("120 queued", html, StringComparison.Ordinal);
	}

	// Oldest runs first and the list is newest first, so a running transfer would sit on the last page.
	[Fact]
	public async Task ALongQueue_StartsWithWhatIsRunning_Failed_OrCanceled()
	{
		List<Item> items = [.. Enumerable.Range(0, 120).Select(_ => new Item { State = TransferState.Queued })];
		items[^1] = new Item { State = TransferState.Running };
		items[^2] = new Item { State = TransferState.Failed };
		items[^3] = new Item { State = TransferState.Canceled };

		string html = await RenderAsync(compact: true, items);

		Assert.Contains("moka-job-list__item--running", html, StringComparison.Ordinal);
		Assert.Contains("moka-job-list__item--failed", html, StringComparison.Ordinal);
		Assert.Contains("moka-job-list__item--canceled", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AShortQueue_ShowsEveryRow_WithoutPages()
	{
		string html = await RenderAsync(compact: true, [.. Enumerable.Range(0, 100).Select(_ => new Item { State = TransferState.Queued })]);

		Assert.Equal(100, Regex.Count(html, "class=\"moka-job-list__item "));
		Assert.DoesNotContain("moka-pagination", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(bool compact, Item item) => RenderAsync(compact, [item]);

	private static async Task<string> RenderAsync(bool compact, IReadOnlyList<Item> items)
	{
		string html = await StaticRender.RenderAsync<TransferQueueView>(
			services =>
			{
				services.AddMokaRed();
				services.AddSingleton(TimeProvider.System);
				services.AddSingleton<ITransferQueue>(new ListQueue(items));
			},
			new Dictionary<string, object?> { [nameof(TransferQueueView.Compact)] = compact })
			.WaitAsync(Patience, TestContext.Current.CancellationToken);
		return WebUtility.HtmlDecode(html);
	}

	private sealed class ListQueue(IReadOnlyList<ITransferItem> items) : ITransferQueue
	{
		public IReadOnlyList<ITransferItem> Items => items;

		public int ActiveCount => 0;

		public event Action? Changed
		{
			add { }
			remove { }
		}

		public ITransferItem Enqueue(TransferRequest request) => throw new NotSupportedException();

		public void Cancel(Guid id) => throw new NotSupportedException();

		public void Retry(Guid id) => throw new NotSupportedException();

		public void Remove(Guid id) => throw new NotSupportedException();

		public void ClearFinished() => throw new NotSupportedException();
	}

	private sealed class Item : ITransferItem
	{
		public Guid Id { get; } = Guid.NewGuid();

		public string Name => "backup.tar";

		public TransferDirection Direction => TransferDirection.Upload;

		public string Source => "backup.tar";

		public string Destination => "web-1:/srv/backup.tar";

		public string? Group { get; init; } = "web-1";

		public bool Elevated { get; init; }

		public required TransferState State { get; init; }

		public long? TotalBytes => 4096;

		public long TransferredBytes { get; init; }

		public double BytesPerSecond { get; init; }

		public string? Error => null;

		public DateTimeOffset QueuedAt => DateTimeOffset.UnixEpoch;

		public DateTimeOffset? StartedAt => DateTimeOffset.UnixEpoch;

		public DateTimeOffset? FinishedAt => null;

		public TransferVerification Verification { get; init; }

		public long? ResumedFrom { get; init; }
	}
}
