using Microsoft.AspNetCore.Components;
using Moka.Red.Feedback.Dialog;
using Mokaterm.UI.Common.Files;

namespace Mokaterm.UI.FileBrowser.Dialogs;

/// <summary>
/// The body of the dialog <see cref="IFileBrowserDialog"/> opens: the browser over the request's files, and nothing else,
/// so it behaves exactly as it does beside a terminal. The dialog closes when its caller stops waiting.
/// </summary>
public sealed partial class FileBrowserDialog : ComponentBase, IDisposable
{
	private CancellationTokenRegistration _closing;

	[CascadingParameter]
	public MokaDialogContext? Dialog { get; set; }

	[Parameter, EditorRequired]
	public FileBrowserRequest Request { get; set; } = default!;

	/// <summary>Closes the dialog when cancelled: the page that opened it went away.</summary>
	[Parameter]
	public CancellationToken Closing { get; set; }

	public void Dispose() => _closing.Dispose();

	protected override void OnInitialized() =>
		_closing = Closing.Register(() => _ = InvokeAsync(() => Dialog?.Cancel()));
}
