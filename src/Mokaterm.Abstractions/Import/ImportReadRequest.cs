namespace Mokaterm.Abstractions.Import;

/// <summary>Asks a source for a preview, either from where the client keeps its data or from a picked file.</summary>
/// <param name="File">Null reads the source's own location.</param>
public sealed record ImportReadRequest(ImportFile? File = null);
