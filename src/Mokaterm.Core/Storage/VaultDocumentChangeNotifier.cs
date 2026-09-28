namespace Mokaterm.Core.Storage;

/// <summary>
/// Broadcasts encrypted document writes and deletes to every UI scope, so a second window or browser tab sees them.
/// </summary>
internal sealed class VaultDocumentChangeNotifier
{
	/// <summary>Raised with the document name and the id of the scope that made the change.</summary>
	public event Action<string, Guid>? Changed;

	public void Publish(string documentName, Guid originScopeId) => Changed?.Invoke(documentName, originScopeId);
}
