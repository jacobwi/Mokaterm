using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Core.Serialization;
using Mokaterm.Core.Storage;

namespace Mokaterm.Core.Security;

/// <summary>
/// Reads and writes <c>{DataDirectory}/vault.json</c> for every UI scope. Writes are serialized, and
/// <see cref="Changed"/> tells the other scopes' vaults about creation, password changes, device unlock and resets.
/// </summary>
/// <remarks>
/// Every read goes to the file instead of a cached copy. A second copy of the desktop app shares the file but none of
/// this process's state, and a header cached before it changed the password would otherwise still unlock with the old
/// one, or be written back over the change by the next device unlock toggle here.
/// </remarks>
internal sealed class VaultHeaderStore : IDisposable
{
	public const string HeaderFileName = "vault.json";

	public const string DocumentsDirectoryName = "vault";

	private readonly string _headerPath;
	private readonly ILogger<VaultHeaderStore> _logger;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private long _version;

	public VaultHeaderStore(IAppEnvironment environment, ILogger<VaultHeaderStore> logger)
	{
		_headerPath = Path.Combine(environment.DataDirectory, HeaderFileName);
		DocumentsDirectory = Path.Combine(environment.DataDirectory, DocumentsDirectoryName);
		_logger = logger;
	}

	/// <summary>Raised after a write or delete with the new snapshot and the id of the scope that made the change.</summary>
	public event Action<VaultHeaderSnapshot, Guid>? Changed;

	/// <summary>Holds the encrypted documents; deleted together with the header on reset.</summary>
	public string DocumentsDirectory { get; }

	public async Task<VaultHeaderSnapshot> GetAsync(CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			return await LoadAsync(cancellationToken);
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <summary>Writes the header of a new vault. Returns null when any header, even a damaged one, already exists.</summary>
	public async Task<VaultHeaderSnapshot?> TryCreateAsync(VaultHeader header, Guid originId, CancellationToken cancellationToken)
	{
		VaultHeaderSnapshot created;
		await _gate.WaitAsync(cancellationToken);
		try
		{
			VaultHeaderSnapshot current = await LoadAsync(cancellationToken);
			if (current.State != VaultHeaderState.Missing || File.Exists(_headerPath))
			{
				return null;
			}

			await WriteFileAsync(header, cancellationToken);
			created = new VaultHeaderSnapshot(VaultHeaderState.Valid, header, NextVersion());
		}
		finally
		{
			_gate.Release();
		}

		Changed?.Invoke(created, originId);
		return created;
	}

	/// <summary>
	/// Runs <paramref name="update"/> against the current header while holding the store lock, so slow work such as
	/// key derivation sees the header it replaces. Returning null leaves the header unchanged.
	/// </summary>
	/// <exception cref="InvalidOperationException">No valid header exists.</exception>
	public async Task<VaultHeaderSnapshot?> UpdateAsync(Guid originId, Func<VaultHeader, CancellationToken, Task<VaultHeader?>> update, CancellationToken cancellationToken)
	{
		VaultHeaderSnapshot updated;
		await _gate.WaitAsync(cancellationToken);
		try
		{
			VaultHeaderSnapshot current = await LoadAsync(cancellationToken);
			if (current is not { State: VaultHeaderState.Valid, Header: { } header })
			{
				throw new InvalidOperationException("The vault header is missing or damaged.");
			}

			VaultHeader? replacement = await update(header, cancellationToken);
			if (replacement is null)
			{
				return null;
			}

			await WriteFileAsync(replacement, cancellationToken);
			updated = new VaultHeaderSnapshot(VaultHeaderState.Valid, replacement, NextVersion());
		}
		finally
		{
			_gate.Release();
		}

		Changed?.Invoke(updated, originId);
		return updated;
	}

	/// <summary>Deletes the header, then the encrypted documents. Settings and other plain documents stay.</summary>
	public async Task<VaultHeaderSnapshot> DeleteAsync(Guid originId, CancellationToken cancellationToken)
	{
		VaultHeaderSnapshot deleted;
		await _gate.WaitAsync(cancellationToken);
		try
		{
			// The header goes first: without it nothing can be decrypted, even if some documents cannot be removed.
			if (File.Exists(_headerPath))
			{
				File.Delete(_headerPath);
			}

			deleted = new VaultHeaderSnapshot(VaultHeaderState.Missing, null, NextVersion());
			DeleteDocuments();
		}
		finally
		{
			_gate.Release();
		}

		Changed?.Invoke(deleted, originId);
		return deleted;
	}

	public void Dispose() => _gate.Dispose();

	/// <summary>Call while holding <see cref="_gate"/>.</summary>
	private async Task<VaultHeaderSnapshot> LoadAsync(CancellationToken cancellationToken)
	{
		byte[] content;
		try
		{
			content = await File.ReadAllBytesAsync(_headerPath, cancellationToken);
		}
		catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
		{
			return new VaultHeaderSnapshot(VaultHeaderState.Missing, null, NextVersion());
		}

		try
		{
			VaultHeader? header = JsonSerializer.Deserialize<VaultHeader>(content, MokatermJson.Document);
			if (header is not null && header.IsWellFormed())
			{
				return new VaultHeaderSnapshot(VaultHeaderState.Valid, header, NextVersion());
			}

			_logger.LogWarning("The vault header is damaged or was written by a newer version");
		}
		catch (JsonException ex)
		{
			_logger.LogWarning(ex, "The vault header could not be parsed");
		}

		return new VaultHeaderSnapshot(VaultHeaderState.Corrupted, null, NextVersion());
	}

	private Task WriteFileAsync(VaultHeader header, CancellationToken cancellationToken) =>
		AtomicFile.WriteAsync(_headerPath, JsonSerializer.SerializeToUtf8Bytes(header, MokatermJson.Document), cancellationToken);

	private void DeleteDocuments()
	{
		try
		{
			if (Directory.Exists(DocumentsDirectory))
			{
				Directory.Delete(DocumentsDirectory, recursive: true);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// Leftovers are unreadable without the deleted header and are quarantined if a new vault meets them.
			_logger.LogWarning(ex, "Some encrypted documents could not be deleted during the vault reset");
		}
	}

	private long NextVersion() => Interlocked.Increment(ref _version);
}
