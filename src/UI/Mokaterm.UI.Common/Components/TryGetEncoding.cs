using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// Looks a character set up by name: <c>TerminalEncodings.TryGet</c> for a terminal, and the FTP module's own for
/// commands and file names, where an unrepresentable byte has to be reported rather than substituted.
/// </summary>
public delegate bool TryGetEncoding(string? name, [NotNullWhen(true)] out Encoding? encoding);
