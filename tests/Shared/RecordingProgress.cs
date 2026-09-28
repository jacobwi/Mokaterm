using System.Collections.Concurrent;

namespace Mokaterm.Tests.Shared;

/// <summary>Records reports synchronously, unlike <see cref="Progress{T}"/>, which posts them to the thread pool.</summary>
internal sealed class RecordingProgress<T> : IProgress<T>
{
	public ConcurrentQueue<T> Reports { get; } = new();

	public void Report(T value) => Reports.Enqueue(value);
}
