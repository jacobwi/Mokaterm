using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Mokaterm.Web.Services;

/// <summary>
/// Hands a download from a Blazor circuit to a plain HTTP request. The browser can only save files it fetches, so
/// the circuit registers the writer under an unguessable single-use ticket and points the browser at it.
/// </summary>
internal sealed class DownloadTicketStore
{
	private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

	private readonly ConcurrentDictionary<string, DownloadTicket> _tickets = new(StringComparer.Ordinal);
	private readonly TimeProvider _timeProvider;

	public DownloadTicketStore(TimeProvider timeProvider) => _timeProvider = timeProvider;

	public DownloadTicket Create(string fileName, Func<Stream, CancellationToken, Task> writeAsync)
	{
		string id = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
		DownloadTicket ticket = new(id, fileName, writeAsync);
		_tickets[id] = ticket;

		// A ticket the browser never fetched (blocked download, closed tab) resolves as cancelled instead of hanging.
		ticket.ExpiryTimer = _timeProvider.CreateTimer(
			state =>
			{
				DownloadTicket expired = (DownloadTicket)state!;
				if (_tickets.TryRemove(expired.Id, out _))
				{
					expired.Completion.TrySetResult(false);
				}
			},
			ticket,
			Lifetime,
			Timeout.InfiniteTimeSpan);

		return ticket;
	}

	/// <summary>Takes a ticket once. A second request with the same id gets nothing.</summary>
	public bool TryTake(string id, out DownloadTicket ticket)
	{
		if (_tickets.TryRemove(id, out ticket!))
		{
			ticket.ExpiryTimer?.Dispose();
			return true;
		}

		return false;
	}

	private static string Base64UrlEncode(byte[] bytes) =>
		Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal sealed class DownloadTicket
{
	public DownloadTicket(string id, string fileName, Func<Stream, CancellationToken, Task> writeAsync)
	{
		Id = id;
		FileName = fileName;
		WriteAsync = writeAsync;
	}

	public string Id { get; }

	public string FileName { get; }

	public Func<Stream, CancellationToken, Task> WriteAsync { get; }

	public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public ITimer? ExpiryTimer { get; set; }
}
