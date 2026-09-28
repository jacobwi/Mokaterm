using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Storage;
using Mokaterm.Core.Security;

namespace Mokaterm.Core.Storage;

/// <summary>
/// The encrypted document files at <c>{DataDirectory}/vault/{name}.vault</c>. A singleton, so every UI scope shares
/// the per-document locks and writes to one document never overlap.
/// </summary>
internal sealed class VaultDocumentFiles
{
	public const string Extension = ".vault";

	private readonly DocumentFileSet _files;

	public VaultDocumentFiles(IAppEnvironment environment, TimeProvider timeProvider, ILogger<VaultDocumentFiles> logger)
	{
		string directory = Path.Combine(environment.DataDirectory, VaultHeaderStore.DocumentsDirectoryName);
		_files = new DocumentFileSet(directory, Extension, timeProvider, logger);
	}

	/// <summary>
	/// Raised after a document that could not be decrypted or parsed was set aside, whichever scope read it. Handlers may
	/// run on any thread.
	/// </summary>
	public event Action<DamagedDocument>? Damaged
	{
		add => _files.Damaged += value;
		remove => _files.Damaged -= value;
	}

	public string GetPath(string name) => _files.GetPath(name);

	/// <inheritdoc cref="DocumentFileSet.ReadAsync{T}"/>
	public ValueTask<T?> ReadAsync<T>(string name, Func<byte[], T?> decode, CancellationToken cancellationToken) where T : class =>
		_files.ReadAsync(name, decode, cancellationToken);

	public Task WriteAsync(string name, ReadOnlyMemory<byte> envelope, CancellationToken cancellationToken) =>
		_files.WriteAsync(name, envelope, cancellationToken);

	/// <inheritdoc cref="DocumentFileSet.DeleteAsync"/>
	public Task<bool> DeleteAsync(string name, CancellationToken cancellationToken) =>
		_files.DeleteAsync(name, cancellationToken);
}
