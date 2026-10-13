using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Plugins;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.Settings.Pages.Plugins;

/// <summary>
/// The Plugins page's Browse tab: the gallery's signed list with a search and categories, and a card per plugin. It
/// reads the list when it first opens, which is the first time anything is asked of the gallery's host, and keeps what
/// the gallery service kept for the window after that. Downloading and installing belong to the page.
/// </summary>
public sealed partial class PluginGalleryView : ComponentBase, IDisposable
{
	private readonly CancellationTokenSource _lifetime = new();
	private PluginGalleryIndex? _index;
	private string? _error;
	private bool _loading;
	private string _query = "";
	private string? _category;
	private bool _disposed;

	/// <summary>The installed plugins, so a card can say Installed or Update.</summary>
	[Parameter]
	public IReadOnlyList<PluginRecord> Installed { get; set; } = [];

	/// <summary>True while a download or a change runs on the page, so two never overlap.</summary>
	[Parameter]
	public bool Busy { get; set; }

	/// <summary>The id of the plugin being downloaded, or null.</summary>
	[Parameter]
	public string? Downloading { get; set; }

	/// <summary>Install or Update on a card: the page downloads the newest offered version and opens the install dialog.</summary>
	[Parameter]
	public EventCallback<PluginGalleryEntry> OnInstall { get; set; }

	[Inject]
	private IPluginGallery Gallery { get; set; } = default!;

	[Inject]
	private IUrlOpener Links { get; set; } = default!;

	[Inject]
	private ILogger<PluginGalleryView> Logger { get; set; } = default!;

	private string Host => Gallery.Index.Host;

	private string Notice => $"Opening this tab asks {Host} for the plugin list. Nothing about you or this computer is sent.";

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_lifetime.Cancel();
		_lifetime.Dispose();
	}

	protected override async Task OnInitializedAsync()
	{
		// A list this window already read shows at once; only the first opening asks the host.
		_index = Gallery.Current;
		if (_index is null)
		{
			await LoadAsync(refresh: false);
		}
	}

	private Task RefreshAsync() => LoadAsync(refresh: true);

	private async Task LoadAsync(bool refresh)
	{
		if (_loading || _disposed)
		{
			return;
		}

		_loading = true;
		_error = null;
		try
		{
			_index = await Gallery.ReadIndexAsync(refresh, _lifetime.Token);
		}
		catch (OperationCanceledException) when (_disposed)
		{
			// The tab went away while the list was being read.
		}
		catch (PluginGalleryException ex)
		{
			Logger.LogWarning("Reading the plugin gallery failed: {Failure}", LogSafe.Describe(ex));
			_error = ex.Message;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError(ex, "Reading the plugin gallery failed");
			_error = "Mokaterm could not read it. Try again in a moment.";
		}
		finally
		{
			_loading = false;
		}
	}

	private void OnQueryChanged(string query) => _query = query;

	private void ShowCategory(string? category) => _category = category == _category ? null : category;

	private PluginRecord? InstalledOf(PluginGalleryEntry plugin) =>
		Installed.FirstOrDefault(record => string.Equals(record.Id, plugin.Id, StringComparison.Ordinal));

	private IReadOnlyList<PluginGalleryEntry> Shown(PluginGalleryIndex index)
	{
		string[] words = _query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		return
		[
			.. index.Plugins
				.Where(plugin => _category is null || plugin.Categories.Contains(_category, StringComparer.Ordinal))
				.Where(plugin => words.All(word => Matches(plugin, word)))
				.OrderBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase)
				.ThenBy(plugin => plugin.Id, StringComparer.Ordinal),
		];
	}

	private static bool Matches(PluginGalleryEntry plugin, string word) =>
		plugin.Name.Contains(word, StringComparison.CurrentCultureIgnoreCase)
		|| plugin.Id.Contains(word, StringComparison.OrdinalIgnoreCase)
		|| plugin.Publisher.Contains(word, StringComparison.CurrentCultureIgnoreCase)
		|| plugin.Description.Contains(word, StringComparison.CurrentCultureIgnoreCase)
		|| plugin.Categories.Any(category => PluginDisplay.Category(category).Contains(word, StringComparison.CurrentCultureIgnoreCase));

	private async Task OpenSourceAsync(PluginGalleryEntry plugin)
	{
		if (!_disposed)
		{
			await Links.OpenAsync(plugin.Source.AbsoluteUri, _lifetime.Token);
		}
	}
}
