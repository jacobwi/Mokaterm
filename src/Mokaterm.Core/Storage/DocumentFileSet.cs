using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Storage;

namespace Mokaterm.Core.Storage;

/// <summary>
/// Named documents stored as <c>{name}{extension}</c> in one directory, with a lock per document, atomic writes
/// and quarantine of files that cannot be decoded. Shared by the plain and the encrypted store.
/// </summary>
internal sealed class DocumentFileSet
{
	private readonly string _extension;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly AsyncKeyedLock _locks = new();

	public DocumentFileSet(string directory, string extension, TimeProvider timeProvider, ILogger logger)
	{
		DirectoryPath = directory;
		_extension = extension;
		_timeProvider = timeProvider;
		_logger = logger;
	}

	public string DirectoryPath { get; }

	/// <summary>Raised after a document that could not be decoded was set aside. Handlers may run on any thread.</summary>
	public event Action<DamagedDocument>? Damaged;

	public string GetPath(string name) => Path.Combine(DirectoryPath, name + _extension);

	/// <summary>
	/// Reads and decodes a document. Missing files read as null. When <paramref name="decode"/> throws
	/// <see cref="JsonException"/>, <see cref="CryptographicException"/>, <see cref="InvalidDataException"/> or
	/// <see cref="ArgumentException"/> the file is renamed to <c>{name}.corrupt-{timestamp}{extension}</c>, reads as null and
	/// <see cref="Damaged"/> says so.
	/// </summary>
	public async ValueTask<T?> ReadAsync<T>(string name, Func<byte[], T?> decode, CancellationToken cancellationToken)
		where T : class
	{
		DocumentNames.Validate(name);
		string path = GetPath(name);
		DamagedDocument damaged;
		using (await _locks.AcquireAsync(name, cancellationToken))
		{
			byte[] content;
			try
			{
				content = await File.ReadAllBytesAsync(path, cancellationToken);
			}
			catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
			{
				return null;
			}

			try
			{
				return decode(content);
			}
			// ArgumentException: a JSON object with the same property twice fails as a duplicate dictionary key.
			catch (Exception ex) when (ex is JsonException or CryptographicException or InvalidDataException or ArgumentException)
			{
				damaged = Quarantine(name, path, ex);
			}
		}

		// Outside the document lock, so a listener that reads the document again cannot wait on this read.
		try
		{
			Damaged?.Invoke(damaged);
		}
		catch (Exception ex)
		{
			// The read already has its answer; a failing listener must not turn it into an error.
			_logger.LogError(ex, "A damaged document listener failed");
		}

		return null;
	}

	public async Task WriteAsync(string name, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
	{
		DocumentNames.Validate(name);
		using (await _locks.AcquireAsync(name, cancellationToken))
		{
			await AtomicFile.WriteAsync(GetPath(name), content, cancellationToken);
		}
	}

	/// <summary>Deletes a document. Returns false when it did not exist.</summary>
	public async Task<bool> DeleteAsync(string name, CancellationToken cancellationToken)
	{
		DocumentNames.Validate(name);
		string path = GetPath(name);
		using (await _locks.AcquireAsync(name, cancellationToken))
		{
			if (!File.Exists(path))
			{
				return false;
			}

			File.Delete(path);
			return true;
		}
	}

	private DamagedDocument Quarantine(string name, string path, Exception error)
	{
		string stamp = _timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
		string target = Path.Combine(DirectoryPath, $"{name}.corrupt-{stamp}{_extension}");
		for (int suffix = 2; File.Exists(target); suffix++)
		{
			target = Path.Combine(DirectoryPath, string.Create(CultureInfo.InvariantCulture, $"{name}.corrupt-{stamp}-{suffix}{_extension}"));
		}

		try
		{
			File.Move(path, target);
			_logger.LogWarning(error, "Document {Document} could not be read and was moved to {QuarantinePath}", name, target);
			return new DamagedDocument { Name = name, Path = target, MovedAside = true };
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			_logger.LogWarning(ex, "Document {Document} could not be read and could not be moved aside", name);
			return new DamagedDocument { Name = name, Path = path, MovedAside = false };
		}
	}
}
