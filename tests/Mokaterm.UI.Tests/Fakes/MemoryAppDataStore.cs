using Mokaterm.Abstractions.Storage;

namespace Mokaterm.UI.Tests.Fakes;

/// <summary>An app data store in memory: empty unless a test seeds it, and it keeps what is written to it.</summary>
internal sealed class MemoryAppDataStore : IAppDataStore
{
	private readonly Dictionary<string, object> _documents = new(StringComparer.Ordinal);

	/// <summary>Seeds a document, as if an earlier run had saved it.</summary>
	public void Set(string name, object document) => _documents[name] = document;

	public ValueTask<T?> ReadAsync<T>(string name, CancellationToken cancellationToken = default) where T : class =>
		ValueTask.FromResult(_documents.GetValueOrDefault(name) as T);

	public Task WriteAsync<T>(string name, T document, CancellationToken cancellationToken = default) where T : class
	{
		_documents[name] = document;
		return Task.CompletedTask;
	}

	public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
	{
		_documents.Remove(name);
		return Task.CompletedTask;
	}
}
