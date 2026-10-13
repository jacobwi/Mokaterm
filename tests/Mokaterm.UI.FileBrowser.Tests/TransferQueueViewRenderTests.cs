using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Transfers;

namespace Mokaterm.UI.FileBrowser.Tests;

/// <summary>
/// A transfer row's full route and error are the title of the route text, which is what gets cut off. The row itself
/// carries none, so the browser's own tooltip never shows over the row's buttons and status hints, which have Moka's.
/// </summary>
public sealed partial class TransferQueueViewRenderTests
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task TheRouteCarriesTheTitle_NotTheRow(bool compact)
	{
		string html = await RenderAsync(compact);

		Assert.All(RowTag().Matches(html), row => Assert.DoesNotContain(" title=", row.Value, StringComparison.Ordinal));
		Match route = RouteTitle().Match(html);
		Assert.True(route.Success, "The route has no title.");
		Assert.Equal(
			"web-1: web-1:/srv/backup.tar -> backup.tar\nPermission denied",
			WebUtility.HtmlDecode(route.Groups["title"].Value).ReplaceLineEndings("\n"));
		Assert.Matches("class=\"tq-name\"[^>]*title=\"backup.tar\"", html);
		Assert.Contains("Retry", Tooltips.Texts(html));
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

	[GeneratedRegex("<div[^>]*class=\"tq-row[^\"]*\"[^>]*>")]
	private static partial Regex RowTag();

	[GeneratedRegex("class=\"tq-route\"[^>]*title=\"(?<title>[^\"]*)\"")]
	private static partial Regex RouteTitle();

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
