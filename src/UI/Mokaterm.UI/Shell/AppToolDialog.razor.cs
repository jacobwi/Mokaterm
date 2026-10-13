using Microsoft.AspNetCore.Components;
using Mokaterm.UI.Tools;

namespace Mokaterm.UI.Shell;

/// <summary>Shows the app tool <see cref="AppToolActions.Dialog"/> names, from the top bar's Tools menu or the palette.</summary>
public sealed partial class AppToolDialog : ComponentBase, IDisposable
{
	[Inject]
	private AppToolActions Tools { get; set; } = default!;

	public void Dispose() => Tools.Changed -= OnToolsChanged;

	protected override void OnInitialized() => Tools.Changed += OnToolsChanged;

	private void OnToolsChanged() => _ = InvokeAsync(StateHasChanged);
}
