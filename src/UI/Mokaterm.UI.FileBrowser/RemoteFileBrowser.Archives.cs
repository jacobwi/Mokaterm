using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.FileBrowser.Browsing;

namespace Mokaterm.UI.FileBrowser;

// Packing the selection into a tar.gz and unpacking archives, both run on the server.
public partial class RemoteFileBrowser
{
	private bool CanUseArchives => HasFeature(RemoteFileSystemFeatures.Archives);

	/// <summary>Asks for a name and packs the entries into a new archive beside them. It never replaces a file.</summary>
	private async Task CompressAsync(List<RemoteFileEntry> entries)
	{
		if (entries.Count == 0 || !CanUseArchives || !BeginOperation())
		{
			return;
		}

		try
		{
			string directory = _path;
			List<string> names = [.. entries.Select(entry => entry.Name)];
			HashSet<string> taken = TakenNames(except: null);
			string suggested = EntryNames.Unique(RemoteArchives.ArchiveName(names, directory), taken, isFolder: false);
			string? name = await Dialogs.AskNameAsync("Compress", MokatermIcons.Archive, "Archive name", suggested, "Compress", taken, selectBaseName: true);
			if (name is null)
			{
				return;
			}

			string archive = RemotePath.Combine(directory, name);
			await RunChangeAsync(
				ChangeFailureTitle("compress", entries),
				(fileSystem, token) => fileSystem.CompressAsync(directory, names, archive, token).AsTask(),
				select: archive);
		}
		finally
		{
			EndOperation();
		}
	}

	/// <summary>
	/// Unpacks an archive into a new folder named after it beside it, <c>site</c> for <c>site.tar.gz</c>, so nothing that
	/// is already there can be replaced. A folder of that name already there gets a number: <c>site 2</c>.
	/// </summary>
	private async Task ExtractHereAsync(RemoteFileEntry archive)
	{
		if (RemoteArchives.KindOf(archive.Name) == RemoteArchiveKind.None || !CanUseArchives || !BeginOperation())
		{
			return;
		}

		try
		{
			string directory = RemotePath.GetParent(archive.Path);
			string folder = EntryNames.Unique(RemoteArchives.FolderName(archive.Name), TakenNames(except: null), isFolder: true);
			string target = RemotePath.Combine(directory, folder);
			await RunChangeAsync(
				"Could not extract " + archive.Name,
				(fileSystem, token) => fileSystem.ExtractAsync(archive.Path, target, token).AsTask(),
				select: target);
		}
		finally
		{
			EndOperation();
		}
	}
}
