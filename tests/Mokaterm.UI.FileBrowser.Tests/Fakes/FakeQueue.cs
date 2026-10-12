using Mokaterm.Abstractions.Transfers;

namespace Mokaterm.UI.FileBrowser.Tests.Fakes;

/// <summary>Keeps what was enqueued and runs it on request, as the queue would when a transfer starts or is retried.</summary>
internal sealed class FakeQueue : ITransferQueue
{
	private Action? _changed;

	public List<TransferRequest> Requests { get; } = [];

	public int ItemsReads { get; private set; }

	public int Subscribers => _changed?.GetInvocationList().Length ?? 0;

	public IReadOnlyList<ITransferItem> Items
	{
		get
		{
			ItemsReads++;
			return [];
		}
	}

	public int ActiveCount => 0;

	public event Action? Changed
	{
		add => _changed += value;
		remove => _changed -= value;
	}

	public ITransferItem Enqueue(TransferRequest request)
	{
		Requests.Add(request);
		return new FakeTransferItem(request.Name);
	}

	public Task RunAsync(int index) =>
		Requests[index].ExecuteAsync(new TransferContext(new Progress<long>(), _ => { }, CancellationToken.None));

	public void RaiseChanged() => _changed?.Invoke();

	public void Cancel(Guid id) => throw new NotSupportedException();

	public void Retry(Guid id) => throw new NotSupportedException();

	public void Remove(Guid id) => throw new NotSupportedException();

	public void ClearFinished() => throw new NotSupportedException();
}
