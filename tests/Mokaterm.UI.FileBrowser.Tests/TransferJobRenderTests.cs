using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Transfers;

namespace Mokaterm.UI.FileBrowser.Tests;

/// <summary>
/// A running transfer spells itself out in a job progress block (the step in words, how far, and the facts), while
/// every other row keeps its one line with a bar and its sizes. Sizes count in 1024s, as everywhere else in the app.
/// </summary>
public sealed class TransferJobRenderTests
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ARunningTransfer_SaysItsStep_HowFar_AndTheFacts(bool compact)
	{
		string html = await RenderAsync(compact, new Item { State = TransferState.Running, TransferredBytes = 1024, BytesPerSecond = 2048 });

		Assert.Contains("moka-job-progress", html, StringComparison.Ordinal);
		Assert.Contains(">Uploading<", html, StringComparison.Ordinal);
		Assert.Contains(">25%<", html, StringComparison.Ordinal);
		Assert.Contains(">1 KB of 4 KB<", html, StringComparison.Ordinal);
		Assert.Contains(">2 KB/s<", html, StringComparison.Ordinal);
		Assert.Contains(">00:01 left<", html, StringComparison.Ordinal);
		Assert.DoesNotContain("class=\"tq-progress\"", html, StringComparison.Ordinal);
		Assert.Contains("Cancel", Tooltips.Texts(html));
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

		Assert.Contains(">Checking the copy<", html, StringComparison.Ordinal);
		Assert.Contains("moka-job-progress--waiting", html, StringComparison.Ordinal);
		Assert.Contains(">4 KB of 4 KB<", html, StringComparison.Ordinal);
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
	public async Task AFinishedTransfer_KeepsItsLine_AndSaysItWasNotChecked(bool compact)
	{
		string html = await RenderAsync(compact, new Item
		{
			State = TransferState.Completed,
			TransferredBytes = 4096,
			Verification = TransferVerification.Unavailable,
		});

		Assert.DoesNotContain("moka-job-progress", html, StringComparison.Ordinal);
		Assert.Contains("class=\"tq-progress\"", html, StringComparison.Ordinal);
		Assert.Contains(">4 KB of 4 KB<", html, StringComparison.Ordinal);
		Assert.Contains(">Unchecked<", html, StringComparison.Ordinal);
		Assert.Contains("The server cannot compute checksums, so this copy was not checked", Tooltips.Texts(html));
		Assert.Contains("Remove from the list", Tooltips.Texts(html));
	}

	private static async Task<string> RenderAsync(bool compact, Item item)
	{
		string html = await StaticRender.RenderAsync<TransferQueueView>(
			services =>
			{
				services.AddMokaRed();
				services.AddSingleton(TimeProvider.System);
				services.AddSingleton<ITransferQueue>(new OneItemQueue(item));
			},
			new Dictionary<string, object?> { [nameof(TransferQueueView.Compact)] = compact })
			.WaitAsync(Patience, TestContext.Current.CancellationToken);
		return WebUtility.HtmlDecode(html);
	}

	private sealed class OneItemQueue(ITransferItem item) : ITransferQueue
	{
		public IReadOnlyList<ITransferItem> Items => [item];

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

		public string? Group => "web-1";

		public bool Elevated => false;

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
