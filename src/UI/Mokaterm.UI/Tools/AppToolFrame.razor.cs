using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Contributions;

namespace Mokaterm.UI.Tools;

/// <summary>
/// A tool that needs no session, such as the subnet calculator, in the app tool dialog or in a window of its own. Where
/// it opens, moves and closes is <see cref="AppToolActions"/>' business; this only wires the frame's buttons to it.
/// </summary>
public sealed partial class AppToolFrame : ComponentBase, IDisposable
{
	private bool _showDescription;

	[Parameter, EditorRequired]
	public ToolPresentation Presentation { get; set; }

	[Parameter, EditorRequired]
	public AppToolDescriptor Tool { get; set; } = default!;

	[Inject]
	private AppToolActions Tools { get; set; } = default!;

	[Inject]
	private ISettingsService Settings { get; set; } = default!;

	public void Dispose() => Settings.Changed -= OnSettingsChanged;

	protected override void OnInitialized()
	{
		_showDescription = Settings.Get<AppearanceSettings>().ShowToolDescriptions;
		Settings.Changed += OnSettingsChanged;
	}

	private Task PopOutAsync() => Tools.PopOutAsync(Tool);

	private void ReturnToDialog() => Tools.ReturnToDialog(Tool);

	private void CloseDialog() => Tools.CloseDialog();

	private void OnSettingsChanged(string sectionKey)
	{
		if (sectionKey == AppearanceSettings.SectionKey)
		{
			_ = InvokeAsync(() =>
			{
				_showDescription = Settings.Get<AppearanceSettings>().ShowToolDescriptions;
				StateHasChanged();
			});
		}
	}
}
