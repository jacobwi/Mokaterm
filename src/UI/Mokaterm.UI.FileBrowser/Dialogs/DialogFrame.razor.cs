using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.FileBrowser.Interop;

namespace Mokaterm.UI.FileBrowser.Dialogs;

/// <summary>
/// Body and action row shared by the file browser dialogs. Enter presses the button marked <c>data-dialog-default</c>
/// unless another button has the focus, and the input marked <c>data-dialog-focus</c> gets focus with
/// <see cref="SelectionStart"/> to <see cref="SelectionEnd"/> selected. Without such an input the default button starts
/// focused.
/// </summary>
public sealed partial class DialogFrame : ComponentBase, IAsyncDisposable
{
	private ElementReference _element;
	private IJSObjectReference? _binding;
	private bool _disposed;

	[Inject]
	private FileBrowserInterop Interop { get; set; } = default!;

	[Parameter]
	public RenderFragment? ChildContent { get; set; }

	[Parameter]
	public RenderFragment? Actions { get; set; }

	[Parameter]
	public int? SelectionStart { get; set; }

	[Parameter]
	public int? SelectionEnd { get; set; }

	public async ValueTask DisposeAsync()
	{
		_disposed = true;
		await Interlocked.Exchange(ref _binding, null).ReleaseAsync();
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (!firstRender)
		{
			return;
		}

		try
		{
			IJSObjectReference binding = await Interop.BindDialogAsync(_element, SelectionStart, SelectionEnd, CancellationToken.None);
			if (_disposed)
			{
				await binding.ReleaseAsync();
				return;
			}

			_binding = binding;
		}
		catch (Exception ex) when (JsModule.IsExpected(ex))
		{
			// Without the binding Enter and initial focus fall back to the browser defaults; the dialog still works.
		}
	}
}
