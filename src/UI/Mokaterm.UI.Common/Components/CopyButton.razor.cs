using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// Icon button that copies plain text through <see cref="IClipboardService"/> (so hosts can swap the clipboard) and shows
/// a check mark for a moment. Never use it for secrets; those go through <see cref="IClipboardService.WriteSecretAsync"/>.
/// </summary>
public sealed partial class CopyButton : ComponentBase, IDisposable
{
	private static readonly TimeSpan ConfirmationTime = TimeSpan.FromSeconds(1.5);

	private CancellationTokenSource? _confirmation;
	private bool _copied;

	[Inject]
	private IClipboardService Clipboard { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private TimeProvider Time { get; set; } = default!;

	[Inject]
	private ILogger<CopyButton> Logger { get; set; } = default!;

	[Parameter, EditorRequired]
	public string Text { get; set; } = "";

	/// <summary>Tooltip and accessible name, for example "Copy fingerprint".</summary>
	[Parameter]
	public string Label { get; set; } = "Copy";

	public void Dispose() => StopConfirmation();

	private async Task CopyAsync()
	{
		try
		{
			await Clipboard.WriteTextAsync(Text);
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex, "Writing to the clipboard failed");
			Interaction.Notify(NoticeSeverity.Error, "Could not copy to the clipboard.");
			return;
		}

		StopConfirmation();
		CancellationTokenSource confirmation = new();
		_confirmation = confirmation;
		_copied = true;
		StateHasChanged();

		try
		{
			await Task.Delay(ConfirmationTime, Time, confirmation.Token);
		}
		catch (OperationCanceledException)
		{
			// A newer copy or disposal took over the check mark.
			return;
		}

		_copied = false;
	}

	private void StopConfirmation()
	{
		if (Interlocked.Exchange(ref _confirmation, null) is { } previous)
		{
			previous.Cancel();
			previous.Dispose();
		}
	}
}
