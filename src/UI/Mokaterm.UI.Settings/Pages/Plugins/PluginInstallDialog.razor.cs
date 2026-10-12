using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.UI.Settings.Pages.Plugins;

/// <summary>
/// What a picked <c>.mokaplugin</c> holds, before anything is installed: who made it, what it is for and, for a plugin
/// that runs code, every file with its SHA-256 and the warning. Turning code on takes a tick the user has to give; a
/// data plugin installs with one button. Nothing applies before the next start.
/// </summary>
public sealed partial class PluginInstallDialog : DraftDialogBase, IDisposable
{
	private readonly CancellationTokenSource _lifetime = new();
	private IPluginPackage? _package;
	private bool _trusted;
	private bool _busy;
	private bool _enabling;
	private string? _error;
	private bool _disposed;

	/// <summary>
	/// The package to show. The dialog takes it over when it opens and disposes it once it closes, or when the dialog
	/// goes away; never while an install is still reading it.
	/// </summary>
	[Parameter]
	public IPluginPackage? Package { get; set; }

	[Inject]
	private IPluginManager Plugins { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private ILogger<PluginInstallDialog> Logger { get; set; } = default!;

	protected override bool IsBusy => _busy;

	/// <summary>A package with a problem cannot be installed at all, turned on or not.</summary>
	private bool CanInstall => _package is not null && string.IsNullOrWhiteSpace(_package.Problem);

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_lifetime.Cancel();
		_lifetime.Dispose();

		// An install that is still running releases the package itself once it stops.
		if (!_busy)
		{
			ReleasePackage();
		}
	}

	protected override void Load() => _package = Package;

	protected override void Clear()
	{
		_trusted = false;
		_enabling = false;
		_error = null;
		ReleasePackage();
	}

	private static string FilesLabel(IPluginPackage package) => package.Files.Count == 1
		? "1 file"
		: string.Create(CultureInfo.CurrentCulture, $"{package.Files.Count} files");

	private static string TrustLabel(IPluginPackage package) => string.IsNullOrWhiteSpace(package.Publisher)
		? "I trust whoever made this plugin"
		: "I trust this publisher";

	private static string InstalledMessage(IPluginPackage package, bool enabled) => !package.HasCode
		? $"{package.Name} loads at the next start."
		: enabled
			? $"{package.Name} is turned on and loads at the next start."
			: $"{package.Name} is installed turned off. Turn it on here when you want it.";

	private void SetTrusted(bool value) => _trusted = value;

	private async Task InstallAsync(bool enable)
	{
		if (_busy || _package is not { } package || !CanInstall || (enable && package.HasCode && !_trusted))
		{
			return;
		}

		_busy = true;
		_enabling = enable;
		_error = null;
		bool installed = false;
		try
		{
			await Plugins.InstallAsync(package, enable, _lifetime.Token);
			installed = true;
		}
		catch (OperationCanceledException) when (_disposed)
		{
			// The page went away while the files were being written.
		}
		catch (InvalidDataException ex)
		{
			// The manager's own words: an id or assembly name that clashes, a file that changed since it was read.
			_error = ex.Message;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			Logger.LogWarning("Installing a plugin failed: {Failure}", LogSafe.Describe(ex));
			_error = $"The plugin could not be written to the plugins folder. {ex.Message}";
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError(ex, "Installing a plugin failed");
			_error = "The plugin could not be installed.";
		}
		finally
		{
			_busy = false;
		}

		if (_disposed)
		{
			ReleasePackage();
			return;
		}

		if (installed)
		{
			Interaction.Notify(NoticeSeverity.Success, InstalledMessage(package, enable), "Plugin installed");
			await CloseAsync();
		}
	}

	private void ReleasePackage()
	{
		_package?.Dispose();
		_package = null;
	}
}
