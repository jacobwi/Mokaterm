using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>One step of a search the server ran, in the order find took it: a folder it entered, or an entry it found.</summary>
/// <param name="Folder">The absolute path of a folder entered, for progress; null for a found entry.</param>
/// <param name="Entry">An entry the query matches; null for a folder.</param>
internal readonly record struct ServerSearchStep(string? Folder, RemoteFileEntry? Entry);
