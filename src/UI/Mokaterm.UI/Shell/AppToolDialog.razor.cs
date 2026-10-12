using Microsoft.AspNetCore.Components;

namespace Mokaterm.UI.Shell;

/// <summary>Shows the app tool <see cref="ShellState.AppTool"/> names, from the top bar's Tools menu or the palette.</summary>
public sealed partial class AppToolDialog : ComponentBase, IDisposable
{
	[Inject]
	private ShellState Shell { get; set; } = default!;

	public void Dispose() => Shell.Changed -= OnShellChanged;

	protected override void OnInitialized() => Shell.Changed += OnShellChanged;

	private void OnOpenChanged(bool open)
	{
		if (!open)
		{
			Shell.CloseAppTool();
		}
	}

	private void OnShellChanged() => _ = InvokeAsync(StateHasChanged);
}
