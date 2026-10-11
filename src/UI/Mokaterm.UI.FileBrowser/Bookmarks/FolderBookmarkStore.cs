using System.Globalization;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Storage;

namespace Mokaterm.UI.FileBrowser.Bookmarks;

/// <summary>
/// Bookmarked folders per saved login, in an encrypted vault document of their own (<see cref="IVaultDataStore"/>, the
/// store for module data) and never in settings: a path says what a server holds and how it is laid out. Every member
/// throws <see cref="Abstractions.Security.VaultLockedException"/> while the vault is locked.
/// </summary>
internal sealed class FolderBookmarkStore : IDisposable
{
	/// <summary>A menu, not a file manager of its own: past this many the list stops being quicker than typing a path.</summary>
	public const int MaxPerLogin = 50;

	public const int MaxPathLength = 4096;

	// Every window and circuit has a store of its own, and a change reads the document, changes it and writes it back.
	private static readonly SemaphoreSlim UpdateLock = new(1, 1);

	private readonly IVaultDataStore _vault;
	private readonly IConnectionRepository _connections;

	public FolderBookmarkStore(IVaultDataStore vault, IConnectionRepository connections)
	{
		_vault = vault;
		_connections = connections;
		_vault.DocumentChanged += OnDocumentChanged;
	}

	/// <summary>Raised after the bookmarks changed, in this window or another one. Handlers may run on any thread.</summary>
	public event Action? Changed;

	/// <summary>The bookmarked folders of <paramref name="connectionId"/>, oldest first.</summary>
	public async ValueTask<IReadOnlyList<string>> ListAsync(Guid connectionId, CancellationToken cancellationToken = default)
	{
		FolderBookmarksDocument? document = await _vault.ReadAsync<FolderBookmarksDocument>(FolderBookmarksDocument.DocumentName, cancellationToken);
		return document?.Logins.GetValueOrDefault(connectionId) is { } paths ? [.. paths] : [];
	}

	/// <summary>Adds <paramref name="path"/> at the end, unless it is there already. Returns the login's bookmarks.</summary>
	/// <exception cref="InvalidOperationException">The login already has <see cref="MaxPerLogin"/> bookmarks, or the path is too long.</exception>
	public Task<IReadOnlyList<string>> AddAsync(Guid connectionId, string path, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(path);
		string folder = RemotePath.Normalize(path);
		if (folder.Length > MaxPathLength)
		{
			throw new InvalidOperationException("The path is too long to bookmark.");
		}

		return UpdateAsync(
			connectionId,
			paths =>
			{
				if (paths.Contains(folder, StringComparer.Ordinal))
				{
					return;
				}

				if (paths.Count >= MaxPerLogin)
				{
					throw new InvalidOperationException(string.Create(CultureInfo.CurrentCulture, $"A login keeps at most {MaxPerLogin} bookmarks. Remove one first."));
				}

				paths.Add(folder);
			},
			cancellationToken);
	}

	/// <summary>Removes <paramref name="path"/>. Returns the login's bookmarks.</summary>
	public Task<IReadOnlyList<string>> RemoveAsync(Guid connectionId, string path, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(path);
		string folder = RemotePath.Normalize(path);
		return UpdateAsync(connectionId, paths => paths.RemoveAll(item => string.Equals(item, folder, StringComparison.Ordinal)), cancellationToken);
	}

	public void Dispose() => _vault.DocumentChanged -= OnDocumentChanged;

	private async Task<IReadOnlyList<string>> UpdateAsync(Guid connectionId, Action<List<string>> change, CancellationToken cancellationToken)
	{
		await UpdateLock.WaitAsync(cancellationToken);
		try
		{
			FolderBookmarksDocument document =
				await _vault.ReadAsync<FolderBookmarksDocument>(FolderBookmarksDocument.DocumentName, cancellationToken) ?? new FolderBookmarksDocument();
			List<string> paths = document.Logins.GetValueOrDefault(connectionId) ?? [];
			change(paths);
			document.Logins[connectionId] = paths;

			// Deleting a login does not reach this document, so the bookmarks of logins that are gone go with the next change.
			ConnectionCatalog catalog = await _connections.GetCatalogAsync(cancellationToken);
			HashSet<Guid> kept = [.. catalog.Connections.Select(connection => connection.Id)];
			kept.Add(connectionId);
			foreach (Guid id in document.Logins.Keys.ToList())
			{
				if (!kept.Contains(id) || document.Logins[id].Count == 0)
				{
					document.Logins.Remove(id);
				}
			}

			if (document.Logins.Count == 0)
			{
				await _vault.DeleteAsync(FolderBookmarksDocument.DocumentName, cancellationToken);
			}
			else
			{
				await _vault.WriteAsync(FolderBookmarksDocument.DocumentName, document, cancellationToken);
			}

			return [.. paths];
		}
		finally
		{
			UpdateLock.Release();
		}
	}

	private void OnDocumentChanged(string name)
	{
		if (name == FolderBookmarksDocument.DocumentName)
		{
			Changed?.Invoke();
		}
	}
}
