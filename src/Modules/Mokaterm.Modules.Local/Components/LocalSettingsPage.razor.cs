using Mokaterm.Modules.Local.Shells;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Local.Components;

/// <summary>
/// Settings page for <see cref="LocalSettings"/>: the default shell, where shells start and the exit code notice, plus
/// the shells found here and the address that names each one.
/// </summary>
public partial class LocalSettingsPage : SettingsSectionBase<LocalSettings>
{
	/// <summary>The empty choice: the first of PowerShell 7, Windows PowerShell and the Command Prompt that is here.</summary>
	private const string Automatic = "";

	private IReadOnlyList<LocalShell> _shells = [];

	/// <summary>Every shell found here, and the saved one even when it is not: a choice nobody can see cannot be changed.</summary>
	private IReadOnlyList<string> DefaultChoices
	{
		get
		{
			List<string> choices = [Automatic, .. _shells.Select(shell => shell.Id)];
			string saved = Settings.DefaultShell;
			if (saved.Length > 0 && !choices.Contains(saved, StringComparer.OrdinalIgnoreCase))
			{
				choices.Add(saved);
			}

			return choices;
		}
	}

	protected override void OnInitialized()
	{
		base.OnInitialized();
		_shells = LocalShellDetector.Detect(LocalMachine.Current());
	}

	private string DefaultShellLabel(string id)
	{
		if (id.Length == 0)
		{
			return LocalShellResolver.Default(new LocalSettings(), _shells) is { } first ? $"Automatic ({first.DisplayName})" : "Automatic";
		}

		return _shells.FirstOrDefault(shell => shell.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.DisplayName
			?? $"{id} (not on this computer)";
	}

	private Task OnDefaultShellChangedAsync(string? value) => SaveAsync(settings => settings with { DefaultShell = value ?? "" });

	private Task SaveAsync(Func<LocalSettings, LocalSettings> change) => UpdateAsync(settings => change(settings).Clamped());
}
