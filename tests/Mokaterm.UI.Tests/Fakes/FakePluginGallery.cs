using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.UI.Tests.Fakes;

/// <summary>
/// A gallery that answers with a fixed list, a fixed failure, or never, for rendering the Browse tab in each of its
/// states. It counts reads, so a test can say that nothing was asked of the gallery's host.
/// </summary>
internal sealed class FakePluginGallery : IPluginGallery
{
	public event EventHandler? Changed;

	public Uri Index { get; init; } = new("https://jacobwi.github.io/mokaterm-plugins/index.json");

	public PluginGalleryIndex? Current { get; set; }

	/// <summary>What a read answers with.</summary>
	public PluginGalleryIndex? List { get; init; }

	/// <summary>What a read throws instead, when set.</summary>
	public Exception? Failure { get; init; }

	/// <summary>A read waits until it is cancelled, which is the loading state.</summary>
	public bool NeverAnswers { get; init; }

	public int Reads { get; private set; }

	public async Task<PluginGalleryIndex> ReadIndexAsync(bool refresh, CancellationToken cancellationToken)
	{
		Reads++;
		if (NeverAnswers)
		{
			await Task.Delay(Timeout.Infinite, cancellationToken);
		}

		if (Failure is not null)
		{
			throw Failure;
		}

		Current = List ?? throw new InvalidOperationException("The fake gallery has no list to answer with.");
		Changed?.Invoke(this, EventArgs.Empty);
		return Current;
	}

	public Task<IPluginPackage> DownloadAsync(PluginGalleryEntry plugin, PluginGalleryVersion version, CancellationToken cancellationToken) =>
		throw new NotSupportedException("A static render presses no button.");
}
