using Microsoft.AspNetCore.Components;
using Mokaterm.UI.Shell;

namespace Mokaterm.UI.Workspace;

/// <summary>
/// The session panes, laid out as <see cref="SessionWorkspace.Layout"/> says, and the settings view in their place while
/// it is open. Each pane draws its own tabs and views.
/// </summary>
public sealed partial class WorkspaceView : ComponentBase, IDisposable
{
	private PaneNode _layout = new PaneLeaf(0);
	private bool _split;
	private int _focusedPane;

	[Inject]
	private SessionWorkspace Workspace { get; set; } = default!;

	[Inject]
	private SessionRestore Restore { get; set; } = default!;

	[Inject]
	private ShellState Shell { get; set; } = default!;

	public void Dispose()
	{
		Workspace.Changed -= OnWorkspaceChanged;
		Shell.Changed -= OnShellChanged;
	}

	protected override async Task OnInitializedAsync()
	{
		Read();
		Workspace.Changed += OnWorkspaceChanged;
		Shell.Changed += OnShellChanged;

		// The shell renders only while the vault is unlocked, so this is the first moment a session can be opened.
		await Restore.StartAsync();
	}

	/// <summary>Takes the layout and the focused pane from the workspace. False when neither changed.</summary>
	private bool Read()
	{
		PaneNode layout = Workspace.Layout;
		int focused = Workspace.FocusedPane;
		if (ReferenceEquals(layout, _layout) && focused == _focusedPane)
		{
			return false;
		}

		_layout = layout;
		_split = layout is PaneSplit;
		_focusedPane = focused;
		return true;
	}

	// Most changes are a session's own (its state, its title), which only its pane redraws for.
	private void OnWorkspaceChanged() => _ = InvokeAsync(() =>
	{
		if (Read())
		{
			StateHasChanged();
		}
	});

	private void OnShellChanged() => _ = InvokeAsync(StateHasChanged);
}
