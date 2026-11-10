using Microsoft.AspNetCore.Components.Forms;
using Moka.Red.Core.Files;

namespace Mokaterm.Maui.Services;

// Moka's own file inputs (a plugin page's MokaFileUpload, say) ask this instead of opening WebView2's file dialog, which
// can end the app on Windows once it has been open a while (WebView2Feedback #5741).
internal sealed class DesktopFilePicker : IMokaFilePicker
{
	public async Task<IReadOnlyList<IBrowserFile>> PickAsync(MokaFilePickRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		IReadOnlyList<FileResult> picked = await MauiLocalFileAccess.PickAsync(request.Multiple, FilePickExtensions.For(request.AcceptedTypes()));
		cancellationToken.ThrowIfCancellationRequested();
		return [.. picked.Select(result => new PickedLocalFile(result))];
	}
}
