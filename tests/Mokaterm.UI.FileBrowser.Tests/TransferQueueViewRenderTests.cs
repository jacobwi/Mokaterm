using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Transfers;

namespace Mokaterm.UI.FileBrowser.Tests;

/// <summary>
/// A failed job's second line is its error alone, so a failed transfer's route and how far it got are written after the
/// error: several failures can share a file name, and only the route tells them apart.
/// </summary>
public sealed partial class TransferQueueViewRenderTests
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task AFailedTransfer_SaysWhy_Where_AndHowFar(bool compact)
	{
		string html = await RenderAsync(compact);

		Match error = ErrorLine().Match(html);
		Assert.True(error.Success, "The row has no error line.");
		Assert.Equal(
			"Permission denied, web-1:/srv/backup.tar -> backup.tar, 1 KB of 4 KB",
			WebUtility.HtmlDecode(error.Groups["text"].Value));
		Assert.Matches("class=\"moka-job-list__name\"[^>]*title=\"backup.tar\"", html);
		Assert.Contains("Retry backup.tar", Tooltips.Texts(html));
	}

	private static Task<string> RenderAsync(bool compact) =>
		StaticRender.RenderAsync<TransferQueueView>(
			services =>
			{
				services.AddMokaRed();
				services.AddSingleton(TimeProvider.System);
				services.AddSingleton<ITransferQueue>(new OneItemQueue(new FailedDownload()));
			},
			new Dictionary<string, object?> { [nameof(TransferQueueView.Compact)] = compact })
			.WaitAsync(Patience, TestContext.Current.CancellationToken);

	[GeneratedRegex("class=\"moka-job-list__error\"[^>]*>(?<text>[^<]*)<")]
	private static partial Regex ErrorLine();

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

	private sealed class FailedDownload : ITransferItem
	{
		public Guid Id { get; } = Guid.NewGuid();

		public string Name => "backup.tar";

		public TransferDirection Direction => TransferDirection.Download;

		public string Source => "web-1:/srv/backup.tar";

		public string Destination => "backup.tar";

		public string? Group => "web-1";

		public bool Elevated => false;

		public TransferState State => TransferState.Failed;

		public long? TotalBytes => 4096;

		public long TransferredBytes => 1024;

		public double BytesPerSecond => 0;

		public string? Error => "Permission denied";

		public DateTimeOffset QueuedAt => DateTimeOffset.UnixEpoch;

		public DateTimeOffset? StartedAt => DateTimeOffset.UnixEpoch;

		public DateTimeOffset? FinishedAt => DateTimeOffset.UnixEpoch;
	}
}
