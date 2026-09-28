using System.Globalization;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Transfers;

namespace Mokaterm.UI.FileBrowser.Transfers;

/// <summary>
/// Turns browser actions into transfer queue items: uploads of picked or dropped files, downloads of files and zipped
/// selections. Each work delegate keeps the account it was queued as, so a transfer runs as user or root the way it
/// started even when the browser switches afterwards, and asks the session for that account's file system each time
/// it runs, so a retry after the connection reopened does not reach for the closed one.
/// </summary>
internal sealed class RemoteTransferService
{
	private readonly ITransferQueue _queue;
	private readonly ILocalFileAccess _localFiles;
	private readonly IUserInteraction _interaction;
	private readonly ISettingsService _settings;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<RemoteTransferService> _logger;

	public RemoteTransferService(
		ITransferQueue queue,
		ILocalFileAccess localFiles,
		IUserInteraction interaction,
		ISettingsService settings,
		TimeProvider timeProvider,
		ILogger<RemoteTransferService> logger)
	{
		_queue = queue;
		_localFiles = localFiles;
		_interaction = interaction;
		_settings = settings;
		_timeProvider = timeProvider;
		_logger = logger;
	}

	/// <summary>
	/// Creates the folders the items need, resolves name conflicts per <see cref="FileTransferSettings.Overwrite"/> and
	/// enqueues one upload per file.
	/// </summary>
	/// <exception cref="RemoteFileSystemException">A folder could not be created. Nothing was enqueued.</exception>
	public async Task<UploadBatch> UploadAsync(
		TransferFileSystem target,
		TransferOrigin origin,
		string directory,
		IReadOnlyList<LocalFileItem> items,
		CancellationToken cancellationToken)
	{
		FileTransferSettings settings = _settings.Get<FileTransferSettings>();
		IRemoteFileSystem fileSystem = await target.GetAsync(cancellationToken);
		HashSet<string> createdDirectories = await CreateDirectoriesAsync(fileSystem, directory, items, cancellationToken);

		List<(LocalFileItem File, string RelativePath, Func<CancellationToken, ValueTask<Stream>> Open)> files = [];
		foreach (LocalFileItem item in items)
		{
			if (!item.IsDirectory && item.OpenReadAsync is { } open && SafeRelativePath(item) is { } relativePath)
			{
				files.Add((item, relativePath, open));
			}
		}

		UploadBatch batch = new();
		OverwriteResolver resolver = new(_interaction, settings.Overwrite, files.Count > 1);
		foreach ((LocalFileItem file, string relativePath, Func<CancellationToken, ValueTask<Stream>> open) in files)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string remotePath = RemotePath.Combine(directory, relativePath);
			bool overwrite = !resolver.ChecksExisting;

			// A folder this batch just created cannot hold a conflicting file.
			if (resolver.ChecksExisting && !createdDirectories.Contains(RemotePath.GetParent(remotePath)))
			{
				RemoteFileEntry? existing = await TryStatAsync(fileSystem, remotePath, cancellationToken);
				if (existing is not null)
				{
					OverwriteChoice choice = await resolver.ResolveAsync(remotePath, existing, file.Length, file.LastModified, cancellationToken);
					if (choice == OverwriteChoice.Cancel)
					{
						batch.Cancelled = true;
						break;
					}

					if (choice == OverwriteChoice.Skip)
					{
						continue;
					}

					overwrite = true;
				}
			}

			UploadOptions options = new()
			{
				Overwrite = overwrite,
				LastModified = settings.PreserveTimestamps ? file.LastModified : null,
			};
			batch.Add(file, _queue.Enqueue(CreateUploadRequest(target, origin, file, open, remotePath, options, batch)));
		}

		return batch;
	}

	/// <summary>Queues a download of one file through <see cref="ILocalFileAccess.SaveFileAsync"/>.</summary>
	public ITransferItem EnqueueDownload(TransferFileSystem source, TransferOrigin origin, RemoteFileEntry entry)
	{
		long? size = entry.Kind == RemoteEntryKind.File ? entry.Size : null;
		return _queue.Enqueue(new TransferRequest
		{
			Name = entry.Name,
			Direction = TransferDirection.Download,
			Source = origin.Describe(source, entry.Path),
			Destination = entry.Name,
			TotalBytes = size,
			Group = origin.Group,
			Elevated = source.IsElevated,
			ExecuteAsync = context => SaveAsync(
				entry.Name,
				size,
				context,
				async (stream, cancellationToken) =>
				{
					IRemoteFileSystem fileSystem = await source.GetAsync(cancellationToken);
					await fileSystem.DownloadAsync(entry.Path, stream, context.Progress, cancellationToken);
				}),
		});
	}

	/// <summary>Queues a zip of folders and files. The tree is walked when the transfer runs, inside the save.</summary>
	public ITransferItem EnqueueZipDownload(
		TransferFileSystem source,
		TransferOrigin origin,
		IReadOnlyList<RemoteFileEntry> entries,
		string archiveName)
	{
		RemoteFileEntry[] roots = [.. entries];
		string description = roots.Length == 1
			? origin.Describe(source, roots[0].Path)
			: string.Create(CultureInfo.CurrentCulture, $"{origin.Describe(source, RemotePath.GetParent(roots[0].Path))} ({roots.Length} items)");

		return _queue.Enqueue(new TransferRequest
		{
			Name = archiveName,
			Direction = TransferDirection.Download,
			Source = description,
			Destination = archiveName,
			Group = origin.Group,
			Elevated = source.IsElevated,
			ExecuteAsync = context => SaveAsync(archiveName, null, context, async (stream, cancellationToken) =>
			{
				IRemoteFileSystem fileSystem = await source.GetAsync(cancellationToken);
				IReadOnlyList<ZipItem> plan = await RemoteZipWriter.PlanAsync(fileSystem, roots, cancellationToken);
				context.SetTotalBytes(RemoteZipWriter.TotalBytes(plan));
				await RemoteZipWriter.WriteAsync(fileSystem, plan, stream, context.Progress, _timeProvider.GetLocalNow(), cancellationToken);
			}),
		});
	}

	/// <summary>
	/// Completes once every transfer completed, failed or was canceled, which is also how one taken off the queue ends. A
	/// transfer retried meanwhile is waited for again.
	/// </summary>
	public async Task WaitForCompletionAsync(IReadOnlyList<ITransferItem> transfers, CancellationToken cancellationToken)
	{
		if (transfers.Count == 0)
		{
			return;
		}

		TaskCompletionSource settled = new(TaskCreationOptions.RunContinuationsAsynchronously);
		Lock gate = new();

		// Every transfer before this index was settled when last looked at. The queue raises Changed for each start, end
		// and progress step of every transfer, and a batch settles about in the order it was queued, so each check goes on
		// from here instead of reading the whole batch, or the whole queue, again.
		int next = 0;

		void Check()
		{
			lock (gate)
			{
				while (next < transfers.Count && IsSettled(transfers[next]))
				{
					next++;
				}

				if (next < transfers.Count)
				{
					return;
				}

				// One read of the whole batch before finishing: a transfer that settled earlier may have been retried since.
				int unsettled = FirstUnsettled(transfers);
				if (unsettled < 0)
				{
					settled.TrySetResult();
				}
				else
				{
					next = unsettled;
				}
			}
		}

		_queue.Changed += Check;
		try
		{
			Check();
			await settled.Task.WaitAsync(cancellationToken);
		}
		finally
		{
			_queue.Changed -= Check;
		}
	}

	/// <summary>Takes finished items off the queue, for example failed uploads that are about to be retried as root.</summary>
	public void Remove(IEnumerable<ITransferItem> transfers)
	{
		foreach (ITransferItem transfer in transfers)
		{
			try
			{
				_queue.Remove(transfer.Id);
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "A transfer could not be removed from the queue");
			}
		}
	}

	private static bool IsSettled(ITransferItem transfer) =>
		transfer.State is TransferState.Completed or TransferState.Failed or TransferState.Canceled;

	private static int FirstUnsettled(IReadOnlyList<ITransferItem> transfers)
	{
		for (int index = 0; index < transfers.Count; index++)
		{
			if (!IsSettled(transfers[index]))
			{
				return index;
			}
		}

		return -1;
	}

	private static TransferRequest CreateUploadRequest(
		TransferFileSystem target,
		TransferOrigin origin,
		LocalFileItem file,
		Func<CancellationToken, ValueTask<Stream>> open,
		string remotePath,
		UploadOptions options,
		UploadBatch batch)
	{
		// Set once an attempt failed after it may have created the file, so the next attempt replaces the partial file.
		bool replaceLeftover = false;
		return new TransferRequest
		{
			Name = file.Name,
			Direction = TransferDirection.Upload,
			Source = file.RelativePath,
			Destination = origin.Describe(target, remotePath),
			TotalBytes = file.Length,
			Group = origin.Group,
			Elevated = target.IsElevated,
			ExecuteAsync = async context =>
			{
				UploadOptions attemptOptions = Volatile.Read(ref replaceLeftover) ? options with { Overwrite = true } : options;

				IRemoteFileSystem fileSystem = await target.GetAsync(context.CancellationToken);
				Stream source = await open(context.CancellationToken);
				await using (source)
				{
					if (file.Length is null && source.CanSeek)
					{
						context.SetTotalBytes(source.Length);
					}

					try
					{
						await fileSystem.UploadAsync(remotePath, source, attemptOptions, context.Progress, context.CancellationToken);
						batch.ClearPermissionDenied(file);
					}
					catch (Exception ex) when (ex is not RemoteFileSystemException { Kind: RemoteFileErrorKind.AlreadyExists })
					{
						// A refusal to replace comes before anything is written, and the file it found belongs to someone
						// else: retrying that one must ask again, not overwrite. Any other failure can come mid-write.
						Volatile.Write(ref replaceLeftover, true);
						if (ex is RemoteFileSystemException { Kind: RemoteFileErrorKind.PermissionDenied })
						{
							batch.MarkPermissionDenied(file);
						}

						throw;
					}
				}
			},
		};
	}

	private async Task SaveAsync(string name, long? size, TransferContext context, Func<Stream, CancellationToken, Task> write)
	{
		bool saved = await _localFiles.SaveFileAsync(
			name,
			size,
			async (stream, saveToken) =>
			{
				// Either side can end the save: the queue cancels the transfer, or the host aborts the download.
				using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(saveToken, context.CancellationToken);
				await write(stream, linked.Token);
			},
			context.CancellationToken);

		if (!saved)
		{
			throw new OperationCanceledException("The download was cancelled.");
		}
	}

	/// <summary>Creates every folder the batch needs, parents first. Returns the ones that did not exist yet.</summary>
	private static async Task<HashSet<string>> CreateDirectoriesAsync(
		IRemoteFileSystem fileSystem,
		string directory,
		IReadOnlyList<LocalFileItem> items,
		CancellationToken cancellationToken)
	{
		// Pickers and drops do not all list every folder, so parents are derived from the file paths as well.
		HashSet<string> relative = new(StringComparer.Ordinal);
		foreach (LocalFileItem item in items)
		{
			if (SafeRelativePath(item) is not { } path)
			{
				continue;
			}

			string folder = item.IsDirectory ? path : ParentOf(path);
			while (folder.Length > 0 && relative.Add(folder))
			{
				folder = ParentOf(folder);
			}
		}

		HashSet<string> created = new(StringComparer.Ordinal);
		IEnumerable<string> ordered = relative
			.OrderBy(path => path.Count(character => character == '/'))
			.ThenBy(path => path, StringComparer.Ordinal);
		foreach (string path in ordered)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string remotePath = RemotePath.Combine(directory, path);
			try
			{
				await fileSystem.CreateDirectoryAsync(remotePath, cancellationToken);
				created.Add(remotePath);
			}
			catch (RemoteFileSystemException ex) when (ex.Kind == RemoteFileErrorKind.AlreadyExists)
			{
				// Uploading into an existing folder is the normal case.
			}
		}

		return created;
	}

	private static async Task<RemoteFileEntry?> TryStatAsync(IRemoteFileSystem fileSystem, string path, CancellationToken cancellationToken)
	{
		try
		{
			return await fileSystem.StatAsync(path, cancellationToken);
		}
		catch (RemoteFileSystemException)
		{
			// Unknown is treated as absent: the upload itself then reports what is wrong.
			return null;
		}
	}

	// Relative paths come from the browser or the OS; '.' and '..' segments must not climb out of the target folder, and
	// neither may the name used when the path has no segment left. Null when there is nothing usable to upload to.
	private static string? SafeRelativePath(LocalFileItem item)
	{
		string[] segments = [.. item.RelativePath
			.Split('/', StringSplitOptions.RemoveEmptyEntries)
			.Where(RemotePath.IsValidName)];
		return segments.Length > 0 ? string.Join('/', segments)
			: RemotePath.IsValidName(item.Name) ? item.Name
			: null;
	}

	private static string ParentOf(string relativePath)
	{
		int slash = relativePath.LastIndexOf('/');
		return slash < 0 ? "" : relativePath[..slash];
	}
}
