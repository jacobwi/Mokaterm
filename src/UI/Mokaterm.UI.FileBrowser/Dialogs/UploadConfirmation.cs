namespace Mokaterm.UI.FileBrowser.Dialogs;

/// <summary>What <see cref="UploadConfirmDialog"/> closed with: the account, and whether drops should stop asking.</summary>
internal sealed record UploadConfirmation(UploadRunAs RunAs, bool StopAsking);
