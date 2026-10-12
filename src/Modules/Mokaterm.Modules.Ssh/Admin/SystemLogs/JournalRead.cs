using Mokaterm.Modules.Ssh.Tools;

namespace Mokaterm.Modules.Ssh.Admin.SystemLogs;

/// <summary>Lines journalctl printed, and the cursor a follow continues from, or null when the journal gave none.</summary>
internal sealed record JournalRead(IReadOnlyList<LogLine> Lines, string? Cursor);
