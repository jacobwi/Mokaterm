using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.UI.Common.Editing;

namespace Mokaterm.Modules.Ssh.Components.Admin;

/// <summary>
/// Editing a system file from the system view: through the file browser's editor, on the session's one root file
/// system (opened on first use, which may ask for the sudo password). What could not be opened comes back as a sentence
/// or a failed result, never as an exception, so a page shows it beside the thing it was editing.
/// </summary>
internal static class AdminFileEditing
{
	/// <summary>The session's root file system, or a sentence on why there is none.</summary>
	public static async Task<(IRemoteFileSystem? Root, string? Problem)> OpenRootAsync(SystemAdminFeature feature, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(feature);
		if (feature.RootFiles is not { CanUseRoot: true } rootFiles)
		{
			return (null, "This session has no way to open files as root.");
		}

		try
		{
			return (await rootFiles.OpenAsync(cancellationToken), null);
		}
		catch (RemoteFileSystemException ex)
		{
			return (null, ex.Message);
		}
	}

	/// <summary>Edits <paramref name="path"/> as root; with <paramref name="createNew"/> it is created empty first.</summary>
	public static async Task<RemoteTextEditResult> EditAsRootAsync(
		IRemoteTextEditor editor,
		SystemAdminFeature feature,
		string path,
		string displayName,
		bool createNew,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(editor);
		(IRemoteFileSystem? root, string? problem) = await OpenRootAsync(feature, cancellationToken);
		return root is null
			? RemoteTextEditResult.Failed(new InvalidOperationException(problem), problem ?? "The file could not be opened as root.")
			: await editor.EditAsync(new RemoteTextEditRequest { FileSystem = root, Path = path, DisplayName = displayName, CreateNew = createNew }, cancellationToken);
	}
}
