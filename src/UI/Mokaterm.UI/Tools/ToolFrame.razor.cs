using Microsoft.AspNetCore.Components;
using Moka.Red.Core.Icons;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.Tools;

/// <summary>
/// The one frame every tool is drawn in: the session tool dialog, the app tool dialog and a tool's own window. The
/// header carries the tool's icon and title, what it works on (<see cref="Detail"/>) and the actions that fit where it
/// is: "Open in its own window" in a dialog where the host has windows; "Back to the main window", "Keep on top" and
/// "Show the main window" in a window. The body sits inside a <c>ViewBoundary</c>, sized by the Standard and Wide
/// rules.
/// </summary>
public sealed partial class ToolFrame : ComponentBase, IDisposable
{
	private bool _keptOnTop;
	private bool _following;

	[Parameter, EditorRequired]
	public ToolPresentation Presentation { get; set; }

	/// <summary>What the frame shows, which is also what its window is known by.</summary>
	[Parameter, EditorRequired]
	public ToolWindowTarget Target { get; set; } = default!;

	[Parameter, EditorRequired]
	public MokaIconDefinition Icon { get; set; }

	[Parameter, EditorRequired]
	public string Title { get; set; } = "";

	[Parameter]
	public SessionToolSize Size { get; set; }

	/// <summary>After the title: the session a session tool works on, an app tool's one-line description.</summary>
	[Parameter]
	public RenderFragment? Detail { get; set; }

	/// <summary>Shown when the title is pointed at, such as what the tool does when the header leaves it out.</summary>
	[Parameter]
	public string? Hint { get; set; }

	/// <summary>The dialog's "Open in its own window". Shown only where the host has windows.</summary>
	[Parameter]
	public EventCallback OnPopOut { get; set; }

	/// <summary>The window's "Back to the main window". Not shown without it, such as for a session that closed.</summary>
	[Parameter]
	public EventCallback OnReturn { get; set; }

	/// <summary>The dialog closed itself: Escape, the backdrop or its close button.</summary>
	[Parameter]
	public EventCallback OnClose { get; set; }

	[Parameter]
	public RenderFragment? ChildContent { get; set; }

	[Inject]
	private ShellToolWindows Windows { get; set; } = default!;

	private bool Wide => Size == SessionToolSize.Wide;

	public void Dispose()
	{
		if (_following)
		{
			Windows.Changed -= OnWindowsChanged;
		}
	}

	protected override void OnParametersSet()
	{
		// Only a window has a keep-on-top button to keep lit; a dialog has nothing to follow.
		if (Presentation == ToolPresentation.Window && !_following)
		{
			_following = true;
			Windows.Changed += OnWindowsChanged;
		}

		_keptOnTop = Presentation == ToolPresentation.Window && Windows.IsKeptOnTop(Target);
	}

	private Task OnOpenChanged(bool open) => open ? Task.CompletedTask : OnClose.InvokeAsync();

	private void ToggleKeepOnTop() => Windows.SetKeepOnTop(Target, !_keptOnTop);

	private void ShowMainWindow() => Windows.ShowOwner();

	private void OnWindowsChanged() => _ = InvokeAsync(() =>
	{
		bool keptOnTop = Windows.IsKeptOnTop(Target);
		if (keptOnTop != _keptOnTop)
		{
			_keptOnTop = keptOnTop;
			StateHasChanged();
		}
	});
}
