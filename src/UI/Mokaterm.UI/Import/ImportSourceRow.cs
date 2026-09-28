using Mokaterm.Abstractions.Import;

namespace Mokaterm.UI.Import;

/// <summary>One source in the first step, with what probing it turned up.</summary>
internal sealed record ImportSourceRow(ImportSourceInfo Info, ImportPreview Preview);
