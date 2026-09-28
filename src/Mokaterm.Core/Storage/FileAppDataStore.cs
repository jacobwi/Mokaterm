using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Storage;
using Mokaterm.Core.Serialization;

namespace Mokaterm.Core.Storage;

/// <summary>Plain JSON documents at <c>{DataDirectory}/{name}.json</c>.</summary>
internal sealed class FileAppDataStore : IAppDataStore
{
	private readonly DocumentFileSet _files;

	public FileAppDataStore(IAppEnvironment environment, TimeProvider timeProvider, ILogger<FileAppDataStore> logger)
	{
		_files = new DocumentFileSet(environment.DataDirectory, ".json", timeProvider, logger);
	}

	public ValueTask<T?> ReadAsync<T>(string name, CancellationToken cancellationToken = default) where T : class
	{
		EnsureNotReserved(name);
		return _files.ReadAsync(name, static content => JsonSerializer.Deserialize<T>(content, MokatermJson.Document), cancellationToken);
	}

	public Task WriteAsync<T>(string name, T document, CancellationToken cancellationToken = default) where T : class
	{
		ArgumentNullException.ThrowIfNull(document);
		EnsureNotReserved(name);
		byte[] content = JsonSerializer.SerializeToUtf8Bytes(document, MokatermJson.Document);
		return _files.WriteAsync(name, content, cancellationToken);
	}

	public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
	{
		EnsureNotReserved(name);
		return _files.DeleteAsync(name, cancellationToken);
	}

	private static void EnsureNotReserved(string name)
	{
		DocumentNames.Validate(name);
		if (name == DocumentNames.ReservedVaultHeader)
		{
			throw new ArgumentException("The name 'vault' is reserved for the vault header.", nameof(name));
		}
	}
}
