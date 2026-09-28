using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Mokaterm.Abstractions.Platform;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.FileBrowser.Platform;

/// <summary>
/// One element listening for OS file drops. The page keeps the dropped File objects under a drop id and reports their
/// metadata in chunks, so a large folder never exceeds the circuit's message size limit.
/// </summary>
internal sealed class BrowserFileDropRegistration : IAsyncDisposable
{
	// More drops in flight than this means the page abandoned some; the oldest are released.
	private const int MaxPendingDrops = 4;

	private readonly JsModule _module;
	private readonly FileDropHandlers _handlers;
	private readonly ILogger _logger;
	private readonly Lock _lock = new();
	private readonly Dictionary<string, List<DroppedFileInfo>> _pending = new(StringComparer.Ordinal);
	private readonly List<string> _pendingOrder = [];
	private DotNetObjectReference<BrowserFileDropRegistration>? _reference;
	private IJSObjectReference? _listener;

	public BrowserFileDropRegistration(JsModule module, FileDropHandlers handlers, ILogger logger)
	{
		_module = module;
		_handlers = handlers;
		_logger = logger;
	}

	public async ValueTask AttachAsync(ElementReference element, CancellationToken cancellationToken)
	{
		_reference = DotNetObjectReference.Create(this);
		_listener = await _module.InvokeAsync<IJSObjectReference>("attach", cancellationToken, element, _reference);
	}

	[JSInvokable]
	public Task OnDragOver(bool isOver) => _handlers.OnDragOver?.Invoke(isOver) ?? Task.CompletedTask;

	[JSInvokable]
	public async Task AddDropItems(string dropId, DroppedFileInfo[] items)
	{
		List<string> abandoned = [];
		lock (_lock)
		{
			if (!_pending.TryGetValue(dropId, out List<DroppedFileInfo>? list))
			{
				list = [];
				_pending[dropId] = list;
				_pendingOrder.Add(dropId);
				while (_pendingOrder.Count > MaxPendingDrops)
				{
					string oldest = _pendingOrder[0];
					_pendingOrder.RemoveAt(0);
					_pending.Remove(oldest);
					abandoned.Add(oldest);
				}
			}

			list.AddRange(items);
		}

		foreach (string id in abandoned)
		{
			await _module.TryInvokeVoidAsync("release", id);
		}
	}

	/// <summary>
	/// Hands the drop to the browser and waits for it to finish, uploads included, before the page may let go of the
	/// dropped files.
	/// </summary>
	[JSInvokable]
	public async Task OnDrop(string dropId, bool ctrlKey, bool shiftKey, bool altKey)
	{
		List<DroppedFileInfo>? infos;
		lock (_lock)
		{
			_pending.Remove(dropId, out infos);
			_pendingOrder.Remove(dropId);
		}

		try
		{
			if (infos is { Count: > 0 })
			{
				await _handlers.OnDrop(new FileDropEvent(ToItems(dropId, infos), ctrlKey, shiftKey, altKey));
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Handling dropped files failed");
		}
		finally
		{
			await _module.TryInvokeVoidAsync("release", dropId);
		}
	}

	public async ValueTask DisposeAsync()
	{
		IJSObjectReference? listener = Interlocked.Exchange(ref _listener, null);
		await listener.ReleaseAsync();
		Interlocked.Exchange(ref _reference, null)?.Dispose();
	}

	private List<LocalFileItem> ToItems(string dropId, List<DroppedFileInfo> infos)
	{
		List<LocalFileItem> items = new(infos.Count);
		foreach (DroppedFileInfo info in infos)
		{
			if (info.IsDirectory)
			{
				items.Add(new LocalFileItem { Name = info.Name, RelativePath = info.RelativePath, IsDirectory = true });
				continue;
			}

			int fileIndex = info.FileIndex;
			items.Add(new LocalFileItem
			{
				Name = info.Name,
				RelativePath = info.RelativePath,
				Length = info.Size,
				LastModified = info.LastModified > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(info.LastModified) : null,
				OpenReadAsync = cancellationToken => OpenAsync(dropId, fileIndex, cancellationToken),
			});
		}

		return items;
	}

	private async ValueTask<Stream> OpenAsync(string dropId, int fileIndex, CancellationToken cancellationToken)
	{
		IJSStreamReference reference = await _module.InvokeAsync<IJSStreamReference>("getFile", cancellationToken, dropId, fileIndex);
		return await JsStreamReferenceStream.OpenAsync(reference, cancellationToken);
	}
}
