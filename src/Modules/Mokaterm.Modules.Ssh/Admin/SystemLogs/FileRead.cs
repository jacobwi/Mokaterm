namespace Mokaterm.Modules.Ssh.Admin.SystemLogs;

/// <summary>One read of a log file: where it stood (inode and size), whether it had turned over, and the text read.</summary>
/// <param name="Inode">The file's inode as <c>ls -i</c> printed it, or empty when the file could not be listed.</param>
/// <param name="Size">The size the text ends at, or null when the file could not be listed.</param>
/// <param name="Rotated">The file has another inode, or shrank, since the offset asked for, so it was read from its start.</param>
/// <param name="Skipped">More arrived than one poll takes, so only the newest part was read.</param>
internal sealed record FileRead(string Inode, long? Size, bool Rotated, bool Skipped, string Text);
