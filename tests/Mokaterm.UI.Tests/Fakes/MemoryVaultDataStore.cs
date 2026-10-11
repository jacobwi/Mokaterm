using System.Text.Json;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Storage;

namespace Mokaterm.UI.Tests.Fakes;

/// <summary>
/// Vault documents kept as JSON in memory, written with the web defaults the real store starts from, so a document that
/// would not survive the round trip fails here too. <see cref="Locked"/> makes every call throw as a locked vault does.
/// </summary>
internal sealed class MemoryVaultDataStore : IVaultDataStore
{
	private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { IgnoreReadOnlyProperties = true };

	public event Action<string>? DocumentChanged;

	// Nothing here is ever damaged.
	public event Action<DamagedDocument>? DocumentDamaged
	{
		add { }
		remove { }
	}

	public Dictionary<string, string> Documents { get; } = new(StringComparer.Ordinal);

	public bool Locked { get; set; }

	public ValueTask<T?> ReadAsync<T>(string name, CancellationToken cancellationToken = default) where T : class
	{
		ThrowIfLocked();
		return ValueTask.FromResult(Documents.TryGetValue(name, out string? json) ? JsonSerializer.Deserialize<T>(json, Options) : null);
	}

	public Task WriteAsync<T>(string name, T document, CancellationToken cancellationToken = default) where T : class
	{
		ThrowIfLocked();
		Documents[name] = JsonSerializer.Serialize(document, Options);
		DocumentChanged?.Invoke(name);
		return Task.CompletedTask;
	}

	public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
	{
		ThrowIfLocked();
		if (Documents.Remove(name))
		{
			DocumentChanged?.Invoke(name);
		}

		return Task.CompletedTask;
	}

	private void ThrowIfLocked()
	{
		if (Locked)
		{
			throw new VaultLockedException();
		}
	}
}
