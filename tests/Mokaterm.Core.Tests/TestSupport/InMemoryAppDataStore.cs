using System.Collections.Concurrent;
using System.Text.Json;
using Mokaterm.Abstractions.Storage;

namespace Mokaterm.Core.Tests.TestSupport;

/// <summary>Keeps plain documents as JSON strings and counts calls, for tests that must not touch the disk.</summary>
public sealed class InMemoryAppDataStore : IAppDataStore
{
	private readonly ConcurrentDictionary<string, string> _documents = new(StringComparer.Ordinal);
	private int _reads;
	private int _writes;

	public int ReadCount => _reads;

	public int WriteCount => _writes;

	public string? GetJson(string name) => _documents.TryGetValue(name, out string? json) ? json : null;

	public void SetJson(string name, string json) => _documents[name] = json;

	public ValueTask<T?> ReadAsync<T>(string name, CancellationToken cancellationToken = default) where T : class
	{
		Interlocked.Increment(ref _reads);
		return ValueTask.FromResult(_documents.TryGetValue(name, out string? json) ? JsonSerializer.Deserialize<T>(json) : null);
	}

	public Task WriteAsync<T>(string name, T document, CancellationToken cancellationToken = default) where T : class
	{
		_documents[name] = JsonSerializer.Serialize(document);
		Interlocked.Increment(ref _writes);
		return Task.CompletedTask;
	}

	public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
	{
		_documents.TryRemove(name, out _);
		return Task.CompletedTask;
	}
}
