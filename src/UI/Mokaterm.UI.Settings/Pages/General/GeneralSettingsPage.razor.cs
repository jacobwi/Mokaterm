using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.Settings.Pages.General;

/// <summary>Settings page for <see cref="GeneralSettings"/>, and the backup of every setting through <see cref="ISettingsBackup"/>.</summary>
public sealed partial class GeneralSettingsPage : SettingsSectionBase<GeneralSettings>
{
	/// <summary>A settings file is a few kilobytes; anything near this is not one.</summary>
	private const int MaxBackupBytes = 1024 * 1024;

	private static readonly IReadOnlyList<EnumOption<SessionRestoreMode>> RestoreOptions =
	[
		new(SessionRestoreMode.Off, "Never"),
		new(SessionRestoreMode.Ask, "Ask"),
		new(SessionRestoreMode.Always, "Always"),
	];

	private bool _exporting;
	private bool _importing;

	[Inject]
	private ISettingsBackup Backup { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private IServiceProvider Services { get; set; } = default!;

	[Inject]
	private ILogger<GeneralSettingsPage> Logger { get; set; } = default!;

	/// <summary>The host's picker and save dialog. A host without one gets no backup section.</summary>
	private ILocalFileAccess? Files => Services.GetService<ILocalFileAccess>();

	private bool Busy => _exporting || _importing;

	private Task SaveAsync(Func<GeneralSettings, GeneralSettings> change) => UpdateAsync(s => change(s).Clamped());

	private async Task ExportAsync()
	{
		if (Busy || Files is not { } files)
		{
			return;
		}

		_exporting = true;
		try
		{
			SettingsBackupFile export = await Backup.ExportAsync();
			bool saved = await files.SaveFileAsync(
				export.FileName,
				export.Content.Length,
				async (stream, cancellationToken) => await stream.WriteAsync(export.Content, cancellationToken));
			if (saved)
			{
				Interaction.Notify(NoticeSeverity.Success, $"Your settings are in {export.FileName}.", "Settings exported");
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError(ex, "Exporting the settings failed");
			Interaction.Notify(NoticeSeverity.Error, "The settings file could not be saved.");
		}
		finally
		{
			_exporting = false;
		}
	}

	private async Task ImportAsync()
	{
		if (Busy || Files is not { } files)
		{
			return;
		}

		_importing = true;
		try
		{
			PickedFile<byte[]> picked = await LocalFilePick.ReadBytesAsync(files, MaxBackupBytes, "a settings file");
			if (picked.Error is { } pickError)
			{
				Interaction.Notify(NoticeSeverity.Error, pickError);
				return;
			}

			if (!picked.WasRead)
			{
				return;
			}

			SettingsBackupCheck check = Backup.Check(picked.Name, picked.Content);
			if (check.Error is { } error)
			{
				Interaction.Notify(NoticeSeverity.Error, error);
				return;
			}

			bool confirmed = (await Interaction.ConfirmAsync(new ConfirmPrompt
			{
				Title = "Import settings",
				Message = $"Replace every setting with the ones in {picked.Name}{ExportedOn(check)}? Anything the file does not set "
					+ "goes back to its default. This machine keeps its own folders and update feed.",
				ConfirmText = "Replace",
				Destructive = true,
			})).Confirmed;
			if (!confirmed)
			{
				return;
			}

			await Backup.ImportAsync(picked.Name, picked.Content);
			Interaction.Notify(NoticeSeverity.Success, $"Every setting now comes from {picked.Name}.", "Settings imported");
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError(ex, "Importing the settings failed");
			Interaction.Notify(NoticeSeverity.Error, "The settings could not be imported.");
		}
		finally
		{
			_importing = false;
		}
	}

	private static string ExportedOn(SettingsBackupCheck check) =>
		check.ExportedAt is { } exported
			? string.Create(CultureInfo.CurrentCulture, $", exported on {exported.ToLocalTime():d}")
			: "";
}
