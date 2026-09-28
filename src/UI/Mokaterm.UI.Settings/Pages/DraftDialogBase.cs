using Microsoft.AspNetCore.Components;

namespace Mokaterm.UI.Settings.Pages;

/// <summary>
/// Base for the settings dialogs that edit a draft of their own. The draft is dropped and filled again every time the
/// dialog opens, and dropped again when it closes, so nothing a dialog was shown before can survive into the next one.
/// Passwords and keys make that the rule rather than a nicety: a form field cannot be wiped, and letting go of the
/// string is all a page can do.
/// </summary>
public abstract class DraftDialogBase : ComponentBase
{
	private bool _wasOpen;

	protected DraftDialogBase() => FormId = $"mt-{GetType().Name}-{Guid.NewGuid():N}";

	[Parameter]
	public bool Open { get; set; }

	[Parameter]
	public EventCallback<bool> OpenChanged { get; set; }

	/// <summary>
	/// The id of this dialog's form. A footer button submits it through <c>form="@FormId"</c>, which needs an id, and
	/// the same dialog can be rendered more than once in a page.
	/// </summary>
	protected string FormId { get; }

	/// <summary>True while the dialog is working, which is when it refuses to close itself.</summary>
	protected virtual bool IsBusy => false;

	/// <summary>Closes the dialog and reports it. The draft goes on the way out, whether it was saved or given up on.</summary>
	protected async Task CloseAsync()
	{
		Clear();

		// Kept in step with the parent's own flag: a parent that renders Open as true again reopens a fresh dialog.
		Open = false;
		_wasOpen = false;
		await OpenChanged.InvokeAsync(false);
	}

	/// <summary>The dialog closed itself: Escape, the backdrop or its close button. Refused while it is working.</summary>
	protected Task OnOpenChangedAsync(bool open) => open || IsBusy ? Task.CompletedTask : CloseAsync();

	protected override void OnParametersSet()
	{
		if (Open && !_wasOpen)
		{
			Clear();
			Load();
		}

		_wasOpen = Open;
	}

	/// <summary>Fills the fields from the parameters for a dialog that is opening, after <see cref="Clear"/> ran.</summary>
	protected virtual void Load()
	{
	}

	/// <summary>Drops every field, secrets first.</summary>
	protected abstract void Clear();
}
