using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Plugins;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.Settings.Pages.Plugins;

/// <summary>
/// Installed plugins and what the next start does with them: installing from a file, turning on and off, trusting
/// changed files and removing. Every change waits for a restart, so the page says what is pending rather than pretending
/// it already happened. On a host that cannot install (the web host, where code would run on the server) it only lists.
/// </summary>
public sealed partial class PluginsPage : ComponentBase, IDisposable
{
	// The manager holds a package in memory while the dialog shows it. No plugin we know of comes close; the cap is there
	// so a wrong pick (a disk image renamed by mistake) is refused rather than read.
	private const long MaxPackageBytes = 64L * 1024 * 1024;

	private readonly CancellationTokenSource _lifetime = new();
	private IPluginPackage? _package;
	private bool _installOpen;
	private bool _picking;
	private bool _changing;
	private bool _disposed;

	[Inject]
	private IPluginManager Plugins { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private IServiceProvider Services { get; set; } = default!;

	[Inject]
	private ILogger<PluginsPage> Logger { get; set; } = default!;

	private ILocalFileAccess? Files => Services.GetService<ILocalFileAccess>();

	// Only the desktop can start itself again; elsewhere the callout just says to restart.
	private IAppRestarter? Restarter => Services.GetService<IAppRestarter>();

	[Inject]
	private IEnumerable<PluginInfo> Loaded { get; set; } = default!;

	private bool _restarting;

	private bool CanInstall => Plugins.CanInstall && Files is not null;

	private bool Busy => _picking || _changing;

	private IReadOnlyList<PluginRecord> Listed =>
	[
		.. Plugins.Plugins
			.OrderBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase)
			.ThenBy(plugin => plugin.Id, StringComparer.Ordinal),
	];

	private string InstalledDescription => Plugins.CanInstall
		? "Every change takes effect at the next start."
		: "Read only. The operator adds and removes plugins on the server.";

	private string EmptyDescription => Plugins.CanInstall
		? "Install one from a .mokaplugin file to add protocols, tools or terminal themes."
		: "The operator has not put any plugins in the plugins folder.";

	private string FolderDescription => Plugins.CanInstall
		? "Each plugin gets a folder of its own here, named after its id."
		: "Plugins on this server come only from this folder, which the operator manages. Nothing can be installed or changed from this page.";

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		Plugins.Changed -= OnPluginsChanged;
		_lifetime.Cancel();
		_lifetime.Dispose();
	}

	protected override void OnInitialized() => Plugins.Changed += OnPluginsChanged;

	private void OnPluginsChanged(object? sender, EventArgs e) => _ = InvokeAsync(StateHasChanged);

	/// <summary>
	/// The plugin's own settings page: one whose component comes from the plugin's assembly. Only a plugin this start
	/// loaded has one, since its pages exist only once it is loaded.
	/// </summary>
	private string? SettingsPageOf(PluginRecord plugin) =>
		Loaded.FirstOrDefault(info => info.Id == plugin.Id) is { } info
			&& Services.GetService<IUiContributions>() is { } contributions
			? PluginSettingsPages.For(info, contributions.SettingsPages)?.Id
			: null;

	private void OpenSettings(string pageId) => Services.GetService<ISettingsNavigator>()?.Open(pageId);

	private async Task PickPackageAsync()
	{
		if (Busy || !Plugins.CanInstall || Files is not { } files)
		{
			return;
		}

		_picking = true;
		try
		{
			PickedFile<IPluginPackage> picked = await LocalFilePick.ReadStreamAsync(
				files,
				MaxPackageBytes,
				"a plugin package",
				(stream, token) => Plugins.ReadPackageAsync(stream, token),
				_lifetime.Token);

			if (picked.Error is { } refusal)
			{
				Interaction.Notify(NoticeSeverity.Error, refusal);
				return;
			}

			if (!picked.WasRead)
			{
				// The picker was closed.
				return;
			}

			if (_disposed)
			{
				picked.Content.Dispose();
				return;
			}

			// The dialog owns the package from here and disposes it when it closes.
			_package = picked.Content;
			_installOpen = true;
		}
		catch (InvalidDataException ex)
		{
			// The manager says what is wrong with the file in words meant for the user.
			Interaction.Notify(NoticeSeverity.Error, ex.Message, "Not a plugin Mokaterm can install");
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			Logger.LogWarning("Reading a plugin package failed: {Failure}", LogSafe.Describe(ex));
			Interaction.Notify(NoticeSeverity.Error, ex.Message, "The file could not be read");
		}
		catch (OperationCanceledException) when (_disposed)
		{
			// The page went away while the file was being read.
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError(ex, "Reading a plugin package failed");
			Interaction.Notify(NoticeSeverity.Error, "Pick the file again.", "The plugin package could not be read");
		}
		finally
		{
			_picking = false;
		}
	}

	private void OnInstallOpenChanged(bool open)
	{
		_installOpen = open;
		if (!open)
		{
			// Disposed by the dialog as it closed.
			_package = null;
		}
	}

	private async Task TurnOnAsync(PluginRecord plugin)
	{
		if (Busy)
		{
			return;
		}

		string? trusted = null;
		if (plugin.HasCode)
		{
			if (await ReadFilesAsync(plugin) is not { } files)
			{
				return;
			}

			bool confirmed = (await Interaction.ConfirmAsync(new ConfirmPrompt
			{
				Title = "Turn on a plugin that runs code",
				Message = $"{PluginDisplay.CodeWarning} {plugin.Name} loads at the next start.",
				Details = PluginDisplay.TrustDetails(plugin, files),
				ConfirmText = "Trust and turn on",
			})).Confirmed;

			if (!confirmed)
			{
				return;
			}

			trusted = files.Hash;
		}

		await ChangeAsync(token => Plugins.SetEnabledAsync(plugin.Id, true, trusted, token), "The plugin could not be turned on");
	}

	private Task TurnOffAsync(PluginRecord plugin) => Busy
		? Task.CompletedTask
		: ChangeAsync(token => Plugins.SetEnabledAsync(plugin.Id, false, null, token), "The plugin could not be turned off");

	private async Task TrustChangedFilesAsync(PluginRecord plugin)
	{
		if (Busy)
		{
			return;
		}

		if (await ReadFilesAsync(plugin) is not { } files)
		{
			return;
		}

		bool confirmed = (await Interaction.ConfirmAsync(new ConfirmPrompt
		{
			Title = "Trust the new files",
			Message = $"The files of {plugin.Name} changed since you trusted them, so this start did not load it. Trust them only "
				+ $"if you updated it yourself. {PluginDisplay.CodeWarning}",
			Details = PluginDisplay.TrustDetails(plugin, files),
			ConfirmText = "Trust the new files",
		})).Confirmed;

		if (!confirmed)
		{
			return;
		}

		// The hash that was shown: if the files moved again meanwhile, the manager refuses instead of trusting those.
		await ChangeAsync(token => Plugins.SetEnabledAsync(plugin.Id, true, files.Hash, token), "The new files could not be trusted");
	}

	private async Task<PluginFileSet?> ReadFilesAsync(PluginRecord plugin)
	{
		_changing = true;
		try
		{
			return await Plugins.ReadFilesAsync(plugin.Id, _lifetime.Token);
		}
		catch (OperationCanceledException) when (_disposed)
		{
			return null;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
		{
			Logger.LogWarning("Reading a plugin's files failed: {Failure}", LogSafe.Describe(ex));
			Interaction.Notify(NoticeSeverity.Error, ex.Message, "The plugin's files could not be read");
			return null;
		}
		finally
		{
			_changing = false;
		}
	}

	private async Task RestartAsync()
	{
		if (Busy || _restarting || Restarter is not { } restarter)
		{
			return;
		}

		bool confirmed = (await Interaction.ConfirmAsync(new ConfirmPrompt
		{
			Title = "Restart Mokaterm",
			Message = "Every open session closes, and the new start applies the plugin changes.",
			ConfirmText = "Restart",
		})).Confirmed;

		if (!confirmed)
		{
			return;
		}

		_restarting = true;
		try
		{
			if (!await restarter.RestartAsync())
			{
				Interaction.Notify(NoticeSeverity.Error, "Close Mokaterm and start it again to apply the changes.", "Mokaterm could not restart itself");
			}
		}
		finally
		{
			_restarting = false;
		}
	}

	private async Task RemoveAsync(PluginRecord plugin)
	{
		if (Busy)
		{
			return;
		}

		string themes = plugin.ThemeCount > 0 ? ", its themes with them" : "";
		bool confirmed = (await Interaction.ConfirmAsync(new ConfirmPrompt
		{
			Title = "Remove plugin",
			Message = $"Remove {plugin.Name}? Its files are deleted at the next start{themes}.",
			ConfirmText = "Remove",
			Destructive = true,
		})).Confirmed;

		if (!confirmed)
		{
			return;
		}

		await ChangeAsync(token => Plugins.RemoveAsync(plugin.Id, token), "The plugin could not be removed");
	}

	private async Task ChangeAsync(Func<CancellationToken, Task> change, string failure)
	{
		if (Busy || _disposed)
		{
			return;
		}

		_changing = true;
		try
		{
			await change(_lifetime.Token);
		}
		catch (OperationCanceledException) when (_disposed)
		{
			// The page went away while the change was being written.
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
		{
			Logger.LogWarning("Changing a plugin failed: {Failure}", LogSafe.Describe(ex));
			Interaction.Notify(NoticeSeverity.Error, ex.Message, failure);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError(ex, "Changing a plugin failed");
			Interaction.Notify(NoticeSeverity.Error, $"{failure}.");
		}
		finally
		{
			_changing = false;
		}
	}
}
