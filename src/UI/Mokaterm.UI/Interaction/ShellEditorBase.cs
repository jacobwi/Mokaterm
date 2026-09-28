using Microsoft.AspNetCore.Components;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Presentation;
using Mokaterm.UI.Shell;

namespace Mokaterm.UI.Interaction;

/// <summary>
/// Base for the editor dialogs the shell opens one at a time. It follows the shell's open editor: a request of type
/// <typeparamref name="TRequest"/> is loaded once and kept until the shell hands over a different one, and each new
/// request gets the focus trap and the Enter binding after the render that puts its body in the page. A close is
/// refused while <see cref="IsBusy"/> holds, which is what keeps a save that is still running on screen.
/// </summary>
public abstract class ShellEditorBase<TRequest> : ComponentBase, IDisposable
	where TRequest : ShellEditorRequest
{
	private TRequest? _prepared;
	private bool _disposed;

	[Inject]
	private ShellState Shell { get; set; } = default!;

	[Inject]
	private FormInterop Forms { get; set; } = default!;

	/// <summary>The request on screen, which is also what the dialog's <c>Open</c> reads, or null while it is closed.</summary>
	protected TRequest? Request { get; private set; }

	/// <summary>The dialog body, assigned with <c>@ref</c>. The focus trap and the Enter binding start from it.</summary>
	protected PromptBody? Body { get; set; }

	/// <summary>True while the editor is saving: the dialog then refuses Escape, its close button and the backdrop.</summary>
	protected abstract bool IsBusy { get; }

	/// <summary>True when Enter presses the <c>data-primary</c> button. Off where Enter belongs to a field.</summary>
	protected virtual bool SubmitOnEnter => false;

	/// <summary>False while the editor is still filling its fields, so the focus target is not in the page yet.</summary>
	protected virtual bool IsReady => true;

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected override async Task OnInitializedAsync()
	{
		Shell.Changed += OnShellChanged;
		await SyncAsync();
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (Request is { } request && IsReady && !ReferenceEquals(_prepared, request) && Body is { } body)
		{
			_prepared = request;
			await Forms.PrepareAsync(body.Element, dialog: true, submitOnEnter: SubmitOnEnter);
		}
	}

	/// <summary>Fills the editor for <paramref name="request"/>, once per request, after <see cref="Reset"/> ran.</summary>
	protected abstract Task LoadAsync(TRequest request);

	/// <summary>
	/// Lets go of what the last request left behind: before a new one is loaded, after the editor closed and when the
	/// component goes away. Secrets in particular must not outlive their request. The default does nothing.
	/// </summary>
	protected virtual void Reset()
	{
	}

	/// <summary>True while <paramref name="request"/> is still the one on screen, so a load that came back late stops.</summary>
	protected bool IsCurrent(TRequest request) => ReferenceEquals(Request, request);

	/// <summary>Closes the editor, unless it is busy. This is the dialog's own close and the Cancel button.</summary>
	protected void Close()
	{
		if (!IsBusy)
		{
			CloseSaved();
		}
	}

	/// <summary>Closes the editor although it may still count as busy, for the moment its work went through.</summary>
	protected void CloseSaved()
	{
		if (Request is { } request)
		{
			Shell.CloseEditor(request);
		}
	}

	/// <summary>Opens another editor. The shell ignores it while this one is still open, so close first.</summary>
	protected bool OpenEditor(ShellEditorRequest request) => Shell.OpenEditor(request);

	protected virtual void Dispose(bool disposing)
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		if (disposing)
		{
			Shell.Changed -= OnShellChanged;
			Reset();
		}
	}

	private void OnShellChanged() => _ = InvokeAsync(async () =>
	{
		await SyncAsync();
		StateHasChanged();
	});

	private async Task SyncAsync()
	{
		TRequest? request = Shell.Editor as TRequest;
		if (ReferenceEquals(request, Request))
		{
			return;
		}

		Request = request;
		_prepared = null;
		Reset();
		if (request is not null)
		{
			await LoadAsync(request);
		}
	}
}
