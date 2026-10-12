using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Common.ToolPages;

namespace MokatermPlugin1;

/// <summary>
/// An app tool: it needs no session, so the shell lists it in the top bar's Tools menu and the command palette and opens
/// it in a dialog of its own. Services come by injection, the host's own included.
/// </summary>
public sealed partial class PasswordGeneratorTool : AppToolBase
{
	private PluginSettings _settings = new();
	private string _password = "";

	[Inject]
	private ISettingsService SettingsService { get; set; } = default!;

	[Inject]
	private IClipboardService Clipboard { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	private string Facts => string.Create(
		CultureInfo.CurrentCulture,
		$"{_settings.PasswordLength} characters, about {PasswordGenerator.StrengthBits(_settings)} bits of randomness. Length and symbols are in Settings, Plugins.");

	// Read when the tool opens, so a changed setting applies the next time it is opened.
	protected override void OnInitialized()
	{
		_settings = SettingsService.Get<PluginSettings>().Clamped();
		Generate();
	}

	private void Generate() => _password = PasswordGenerator.Generate(_settings);

	// A password is a secret, so it goes through the host's secret copy, which clears the clipboard again after the time
	// set under Security.
	private async Task CopyAsync()
	{
		try
		{
			await Clipboard.WriteSecretAsync(_password);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Interaction.Notify(NoticeSeverity.Error, "The password could not be copied.");
			return;
		}

		int clearSeconds = SettingsService.Get<SecuritySettings>().ClipboardClearSeconds;
		Interaction.Notify(
			NoticeSeverity.Success,
			clearSeconds > 0
				? string.Create(CultureInfo.CurrentCulture, $"Password copied. The clipboard clears in {clearSeconds} s.")
				: "Password copied.");
	}
}
