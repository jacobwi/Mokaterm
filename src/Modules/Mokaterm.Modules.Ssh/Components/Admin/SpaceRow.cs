using Mokaterm.Modules.Ssh.Admin.Storage;

namespace Mokaterm.Modules.Ssh.Components.Admin;

// A row with no Path is the files lying directly in the folder, and one with a Mount is a file system mounted in it,
// which du's -x left out of the sizes.
internal sealed record SpaceRow(string Name, string? Path, long Kb, double Share, FileSystemUsage? Mount = null);
