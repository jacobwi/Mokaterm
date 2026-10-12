using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.UI.Settings.Pages.Plugins;

/// <summary>
/// One installed plugin on the Plugins page: what it is, how this run treated it, what the next start will change, and
/// the buttons that change it. The page decides what each button asks first.
/// </summary>
public sealed partial class PluginRow : ComponentBase
{
	[Parameter, EditorRequired]
	public PluginRecord Plugin { get; set; } = default!;

	/// <summary>False on a host where plugins come only from the operator's folder: the row then only reports.</summary>
	[Parameter]
	public bool CanManage { get; set; }

	/// <summary>True while a change to any plugin runs, so two never overlap.</summary>
	[Parameter]
	public bool Busy { get; set; }

	[Parameter]
	public EventCallback<PluginRecord> OnTurnOn { get; set; }

	[Parameter]
	public EventCallback<PluginRecord> OnTurnOff { get; set; }

	/// <summary>Turns a plugin whose files changed back on, trusting the files as they are now.</summary>
	[Parameter]
	public EventCallback<PluginRecord> OnTrust { get; set; }

	[Parameter]
	public EventCallback<PluginRecord> OnRemove { get; set; }
}
