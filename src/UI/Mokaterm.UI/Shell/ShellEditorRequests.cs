using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Sessions;

namespace Mokaterm.UI.Shell;

/// <summary>
/// An editor the shell shows as a dialog. Editors compare requests by reference to notice a new one.
/// <para>
/// These records are public because every editor names its own as the type argument of
/// <see cref="Interaction.ShellEditorBase{TRequest}"/>, and a public component cannot have a base type that is less
/// visible than itself. Nothing outside can open an editor either way: <c>ShellState</c> stays internal.
/// </para>
/// </summary>
public abstract record ShellEditorRequest;

/// <summary>Creates a host in <see cref="FolderId"/> when <see cref="HostId"/> is null, otherwise edits that host.</summary>
public sealed record HostEditorRequest(Guid? HostId, Guid? FolderId = null) : ShellEditorRequest;

/// <summary>
/// Edits the login <see cref="ConnectionId"/>, saves the quick-connect <see cref="TransientSession"/>, or creates a
/// login, preselecting <see cref="HostId"/>.
/// </summary>
public sealed record ConnectionEditorRequest : ShellEditorRequest
{
	public Guid? ConnectionId { get; init; }

	public Guid? HostId { get; init; }

	public ISessionHandle? TransientSession { get; init; }
}

/// <summary>Creates a folder under <see cref="ParentId"/> when <see cref="FolderId"/> is null, otherwise renames it.</summary>
public sealed record FolderEditorRequest(Guid? FolderId, Guid? ParentId = null) : ShellEditorRequest;

/// <summary>Opens the import wizard, offering <see cref="FolderId"/> as the folder the hosts land in.</summary>
public sealed record ImportEditorRequest(Guid? FolderId = null) : ShellEditorRequest;

/// <summary>Opens the export dialog, which writes every saved host and login to one file.</summary>
public sealed record ExportEditorRequest : ShellEditorRequest;

/// <summary>
/// Names, tags and scopes a saved command. <see cref="Snippet"/> carries the command and the machine or login it came
/// from; the labels decide which scopes the editor offers.
/// </summary>
public sealed record CommandSnippetEditorRequest : ShellEditorRequest
{
	public required CommandSnippet Snippet { get; init; }

	/// <summary>The session the command came from, so its terminal takes focus again when the editor closes.</summary>
	public Guid? SessionId { get; init; }

	public string? HostLabel { get; init; }

	public string? ConnectionLabel { get; init; }
}
