using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using Mokaterm.Abstractions.Platform;
using Mokaterm.UI.Common.Interop;

namespace Mokaterm.Web.Services;

/// <summary>
/// Local files for the web host: picking streams bytes from the browser into the circuit, saving hands the writer to
/// a one-time HTTP download so the browser shows its normal download UI. The page keeps picked files until
/// <see cref="ReleaseAsync"/>, the way the drop bridge keeps dropped ones until their uploads are over.
/// </summary>
internal sealed class BrowserLocalFileAccess : ILocalFileAccess, IAsyncDisposable
{
	private readonly JsModule _module;
	private readonly DownloadTicketStore _tickets;
	private readonly Lock _gate = new();

	// The page's id for every pick .NET still holds, keyed by the list that was handed out for it.
	private readonly Dictionary<IReadOnlyList<LocalFileItem>, string> _picks = new(ReferenceEqualityComparer.Instance);

	public BrowserLocalFileAccess(IJSRuntime jsRuntime, DownloadTicketStore tickets)
	{
		_module = new JsModule(jsRuntime, "./js/files.js");
		_tickets = tickets;
	}

	public bool CanPickFolders => true;

	public async ValueTask<IReadOnlyList<LocalFileItem>> PickFilesAsync(bool multiple = true, CancellationToken cancellationToken = default)
	{
		PickResult result = await _module.InvokeAsync<PickResult>("pickFiles", cancellationToken, multiple, false);
		return await HoldAsync(result, includeDirectories: false);
	}

	public async ValueTask<IReadOnlyList<LocalFileItem>> PickFolderAsync(CancellationToken cancellationToken = default)
	{
		PickResult result = await _module.InvokeAsync<PickResult>("pickFiles", cancellationToken, true, true);
		return await HoldAsync(result, includeDirectories: true);
	}

	public async ValueTask ReleaseAsync(IReadOnlyList<LocalFileItem> picked)
	{
		string? pickId;
		lock (_gate)
		{
			_picks.Remove(picked, out pickId);
		}

		if (pickId is not null)
		{
			await _module.TryInvokeVoidAsync("release", pickId);
		}
	}

	public async ValueTask<bool> SaveFileAsync(string suggestedName, long? length, Func<Stream, CancellationToken, Task> writeAsync, CancellationToken cancellationToken = default)
	{
		// The length is not passed on: the download promises no size, because the file may have changed since it was listed.
		DownloadTicket ticket = _tickets.Create(suggestedName, writeAsync);
		await _module.InvokeVoidAsync("download", cancellationToken, $"{DownloadEndpoints.RoutePrefix}/{ticket.Id}");
		return await ticket.Completion.Task.WaitAsync(cancellationToken);
	}

	public async ValueTask DisposeAsync()
	{
		string[] held;
		lock (_gate)
		{
			held = [.. _picks.Values];
			_picks.Clear();
		}

		// Usually the page is already gone with the circuit, and the files with it; when it is not, it lets go of them here.
		foreach (string pickId in held)
		{
			await _module.TryInvokeVoidAsync("release", pickId);
		}

		await _module.DisposeAsync();
	}

	/// <summary>
	/// Tells the page .NET holds the pick, which stops the page from dropping it on its own after a while, and remembers it
	/// for <see cref="ReleaseAsync"/>. A pick .NET never answered for, because the call was cancelled, is dropped by the page.
	/// </summary>
	private async ValueTask<IReadOnlyList<LocalFileItem>> HoldAsync(PickResult result, bool includeDirectories)
	{
		List<LocalFileItem> items = ToItems(result, includeDirectories);
		if (result.Id is { } pickId)
		{
			lock (_gate)
			{
				_picks[items] = pickId;
			}

			await _module.TryInvokeVoidAsync("hold", pickId);
		}

		return items;
	}

	private List<LocalFileItem> ToItems(PickResult result, bool includeDirectories)
	{
		List<LocalFileItem> items = [];
		if (result.Id is null)
		{
			return items;
		}

		if (includeDirectories)
		{
			// Folder pickers only report files; rebuild the directory entries from their relative paths so the remote
			// side can create parents before uploading into them.
			HashSet<string> directories = new(StringComparer.Ordinal);
			foreach (PickedFile file in result.Files)
			{
				string[] parts = file.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
				for (int depth = 1; depth < parts.Length; depth++)
				{
					string directory = string.Join('/', parts[..depth]);
					if (directories.Add(directory))
					{
						items.Add(new LocalFileItem { Name = parts[depth - 1], RelativePath = directory, IsDirectory = true });
					}
				}
			}

			items.Sort((a, b) => a.RelativePath.Count(c => c == '/').CompareTo(b.RelativePath.Count(c => c == '/')));
		}

		for (int index = 0; index < result.Files.Count; index++)
		{
			PickedFile file = result.Files[index];
			int fileIndex = index;
			items.Add(new LocalFileItem
			{
				Name = file.Name,
				RelativePath = string.IsNullOrEmpty(file.RelativePath) ? file.Name : file.RelativePath,
				Length = file.Size,
				LastModified = DateTimeOffset.FromUnixTimeMilliseconds(file.LastModified),
				OpenReadAsync = cancellationToken => OpenAsync(result.Id, fileIndex, cancellationToken),
			});
		}

		return items;
	}

	private async ValueTask<Stream> OpenAsync(string pickId, int index, CancellationToken cancellationToken)
	{
		IJSStreamReference reference = await _module.InvokeAsync<IJSStreamReference>("getFile", cancellationToken, pickId, index);
		return await JsStreamReferenceStream.OpenAsync(reference, cancellationToken);
	}

	private sealed record PickResult(
		[property: JsonPropertyName("id")] string? Id,
		[property: JsonPropertyName("files")] IReadOnlyList<PickedFile> Files);

	private sealed record PickedFile(
		[property: JsonPropertyName("name")] string Name,
		[property: JsonPropertyName("relativePath")] string RelativePath,
		[property: JsonPropertyName("size")] long Size,
		[property: JsonPropertyName("lastModified")] long LastModified);
}
