using System.Text.Json;
using Mokaterm.Abstractions.Storage;
using Mokaterm.Core.Security;
using Mokaterm.Core.Serialization;

namespace Mokaterm.Core.Storage;

/// <summary>
/// JSON documents encrypted with the vault's data key. Each document is bound to its vault and name through the
/// associated data, so a file copied under another name or from another vault fails to decrypt.
/// </summary>
internal sealed class VaultDataStore : IVaultDataStore, IDisposable
{
	private readonly IVaultCipher _cipher;
	private readonly VaultDocumentFiles _files;
	private readonly VaultDocumentChangeNotifier _notifier;
	private readonly Guid _scopeId = Guid.NewGuid();

	public VaultDataStore(IVaultCipher cipher, VaultDocumentFiles files, VaultDocumentChangeNotifier notifier)
	{
		_cipher = cipher;
		_files = files;
		_notifier = notifier;
		_notifier.Changed += OnDocumentChanged;
		_files.Damaged += OnDocumentDamaged;
	}

	public event Action<string>? DocumentChanged;

	public event Action<DamagedDocument>? DocumentDamaged;

	/// <summary>Raised with the document name after another UI scope wrote or deleted it.</summary>
	public event Action<string>? ExternalDocumentChanged;

	public async ValueTask<T?> ReadAsync<T>(string name, CancellationToken cancellationToken = default) where T : class
	{
		DocumentNames.Validate(name);
		byte[] associatedData = VaultCrypto.DocumentAssociatedData(_cipher.VaultId, name);
		return await _files.ReadAsync(name, envelope => Decode<T>(envelope, associatedData), cancellationToken);
	}

	public async Task WriteAsync<T>(string name, T document, CancellationToken cancellationToken = default) where T : class
	{
		ArgumentNullException.ThrowIfNull(document);
		DocumentNames.Validate(name);
		byte[] associatedData = VaultCrypto.DocumentAssociatedData(_cipher.VaultId, name);
		byte[] envelope;
		using (PinnedBufferWriter plaintext = new())
		{
			using (Utf8JsonWriter writer = new(plaintext))
			{
				JsonSerializer.Serialize(writer, document, MokatermJson.Compact);
			}

			envelope = VaultEnvelope.Seal(_cipher, plaintext.WrittenSpan, associatedData);
		}

		await _files.WriteAsync(name, envelope, cancellationToken);
		_notifier.Publish(name, _scopeId);
	}

	public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
	{
		DocumentNames.Validate(name);

		// Reading the vault id throws while locked, which the contract requires for every member.
		_ = _cipher.VaultId;
		if (await _files.DeleteAsync(name, cancellationToken))
		{
			_notifier.Publish(name, _scopeId);
		}
	}

	public void Dispose()
	{
		_notifier.Changed -= OnDocumentChanged;
		_files.Damaged -= OnDocumentDamaged;
	}

	private T? Decode<T>(byte[] envelope, byte[] associatedData) where T : class
	{
		using PinnedBytes plaintext = VaultEnvelope.Open(_cipher, envelope, associatedData);
		return JsonSerializer.Deserialize<T>(plaintext.Span, MokatermJson.Compact);
	}

	private void OnDocumentDamaged(DamagedDocument document) => DocumentDamaged?.Invoke(document);

	private void OnDocumentChanged(string name, Guid originScopeId)
	{
		if (originScopeId != _scopeId)
		{
			ExternalDocumentChanged?.Invoke(name);
		}

		DocumentChanged?.Invoke(name);
	}
}
