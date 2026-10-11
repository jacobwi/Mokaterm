using Microsoft.AspNetCore.Components;
using Moka.Red.Core.Icons;
using Moka.Red.Feedback.Dialog;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// A <see cref="MokaDialog"/> under <see cref="DialogHeader"/>: the same parameters, plus an icon, a context after the
/// title and buttons of its own in the header. Focus trapping, Escape and the backdrop stay Moka's; only the header
/// and its Close are drawn here, and Close raises what Moka's own close button would.
/// </summary>
public sealed partial class HeadedDialog : ComponentBase
{
	private readonly string _titleId = $"mt-dialog-title-{Guid.NewGuid():N}";

	[Parameter]
	public bool Open { get; set; }

	[Parameter]
	public EventCallback<bool> OpenChanged { get; set; }

	/// <summary>Raised whenever the dialog closes itself: Escape, the backdrop or Close.</summary>
	[Parameter]
	public EventCallback OnClose { get; set; }

	[Parameter]
	public string? Title { get; set; }

	[Parameter]
	public MokaIconDefinition? Icon { get; set; }

	/// <summary>What the dialog is about, drawn after the title.</summary>
	[Parameter]
	public RenderFragment? HeaderDetail { get; set; }

	/// <summary>Buttons in the header before Close.</summary>
	[Parameter]
	public RenderFragment? HeaderActions { get; set; }

	/// <summary>
	/// A header of the caller's own in place of <see cref="DialogHeader"/>, for one that other places draw too. It gets
	/// the id its heading must carry, which the dialog is named by, and closes the dialog through its own callbacks.
	/// </summary>
	[Parameter]
	public RenderFragment<string>? Header { get; set; }

	[Parameter]
	public MokaDialogSize DialogSize { get; set; } = MokaDialogSize.Medium;

	[Parameter]
	public bool ShowCloseButton { get; set; } = true;

	[Parameter]
	public bool CloseOnEscape { get; set; } = true;

	[Parameter]
	public bool CloseOnBackdropClick { get; set; } = true;

	[Parameter]
	public bool PreventScroll { get; set; } = true;

	/// <summary>
	/// The content fills the box edge to edge and scrolls its own parts, for views with a fixed height such as a wide
	/// session tool. Otherwise it gets the dialog's padding and scrolls as a whole.
	/// </summary>
	[Parameter]
	public bool Fill { get; set; }

	[Parameter]
	public string? Class { get; set; }

	[Parameter]
	public string? Style { get; set; }

	[Parameter]
	public RenderFragment? ChildContent { get; set; }

	[Parameter]
	public RenderFragment? Actions { get; set; }

	[Parameter(CaptureUnmatchedValues = true)]
	public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

	private string BoxClass => Class is { Length: > 0 } extra ? "mt-dialog-box " + extra : "mt-dialog-box";

	private Task OnMokaOpenChanged(bool open) => OpenChanged.InvokeAsync(open);

	private Task OnMokaClose() => OnClose.InvokeAsync();

	// Moka's own button raises both, in this order; the parent then passes Open false, or refuses by keeping it.
	private async Task CloseAsync()
	{
		await OpenChanged.InvokeAsync(false);
		await OnClose.InvokeAsync();
	}
}
