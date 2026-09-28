using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Transfers;

namespace Mokaterm.UI.FileBrowser.Transfers;

/// <summary>The uploads enqueued for one pick or drop, and which of them the server refused for lack of permission.</summary>
internal sealed class UploadBatch
{
	private readonly Lock _lock = new();
	private readonly Dictionary<LocalFileItem, ITransferItem> _transfers = new(ReferenceEqualityComparer.Instance);
	private readonly List<LocalFileItem> _denied = [];

	/// <summary>Enqueued transfers, in the order the files were listed.</summary>
	public IReadOnlyList<ITransferItem> Transfers
	{
		get
		{
			lock (_lock)
			{
				return [.. _transfers.Values];
			}
		}
	}

	/// <summary>True when the user cancelled at an overwrite prompt and the remaining files were not enqueued.</summary>
	public bool Cancelled { get; set; }

	/// <summary>Files whose upload failed with a permission error, on their latest attempt.</summary>
	public IReadOnlyList<LocalFileItem> PermissionDenied
	{
		get
		{
			lock (_lock)
			{
				return [.. _denied];
			}
		}
	}

	public ITransferItem? TransferFor(LocalFileItem file)
	{
		lock (_lock)
		{
			return _transfers.GetValueOrDefault(file);
		}
	}

	internal void Add(LocalFileItem file, ITransferItem transfer)
	{
		lock (_lock)
		{
			_transfers[file] = transfer;
		}
	}

	internal void MarkPermissionDenied(LocalFileItem file)
	{
		lock (_lock)
		{
			if (!_denied.Contains(file, ReferenceEqualityComparer.Instance))
			{
				_denied.Add(file);
			}
		}
	}

	/// <summary>A later attempt got past the permission check, for example after a queue retry.</summary>
	internal void ClearPermissionDenied(LocalFileItem file)
	{
		lock (_lock)
		{
			_denied.RemoveAll(item => ReferenceEquals(item, file));
		}
	}
}
