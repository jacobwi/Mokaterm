using Microsoft.AspNetCore.Components;
using Moka.Red.Core.Icons;
using Moka.Red.Feedback.Dialog;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// The <see cref="MokaDialog"/> every Mokaterm dialog opens in: an icon before the title, what the dialog is about after
/// it, and <see cref="Fill"/> for a view that scrolls its own parts. Everything else is Moka's, passed straight through.
/// </summary>
public sealed partial class HeadedDialog : ComponentBase
{
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
	/// The content fills the body edge to edge and scrolls its own parts, for views with a fixed height such as a wide
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
}
