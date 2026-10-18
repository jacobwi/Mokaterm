using Microsoft.AspNetCore.Components;
using Moka.Red.Feedback.Dialog;
using Mokaterm.UI.Common.Files;

namespace Mokaterm.UI.FileBrowser.Dialogs;

/// <summary>
/// The body of the dialog <see cref="IFileBrowserDialog"/> opens: the browser over the request's files, so it behaves
/// exactly as it does beside a terminal, and for a folder pick the line that hands back the folder on show. The dialog
/// closes when its caller stops waiting.
/// </summary>
public sealed partial class FileBrowserDialog : ComponentBase, IDisposable
{
	private CancellationTokenRegistration _closing;
	private string? _folder;

	[CascadingParameter]
	public MokaDialogContext? Dialog { get; set; }

	[Parameter, EditorRequired]
	public FileBrowserRequest Request { get; set; } = default!;

	/// <summary>Closes the dialog when cancelled: the page that opened it went away.</summary>
	[Parameter]
	public CancellationToken Closing { get; set; }

	/// <summary>Offers "Use this folder", which closes the dialog with the folder the browser shows.</summary>
	[Parameter]
	public bool PickFolder { get; set; }

	public void Dispose() => _closing.Dispose();

	protected override void OnInitialized() =>
		_closing = Closing.Register(() => _ = InvokeAsync(() => Dialog?.Cancel()));

	private void OnFolderChanged(string folder) => _folder = folder;

	private void Use()
	{
		if (_folder is { } folder)
		{
			Dialog?.Close(folder);
		}
	}

	private void Cancel() => Dialog?.Cancel();
}
