using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Mokaterm.Core.Tests.TestSupport;

public sealed record LogEntry(LogLevel Level, string Category, string Message, Exception? Exception);

/// <summary>Collects log entries from every <see cref="TestLogger{T}"/> in a test's service provider.</summary>
public sealed class LogSink
{
	private readonly ConcurrentQueue<LogEntry> _entries = new();

	public IReadOnlyList<LogEntry> Entries => [.. _entries];

	public void Add(LogEntry entry) => _entries.Enqueue(entry);
}

public sealed class TestLogger<T> : ILogger<T>
{
	private readonly LogSink _sink;

	public TestLogger(LogSink sink) => _sink = sink;

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => true;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
		_sink.Add(new LogEntry(logLevel, typeof(T).Name, formatter(state, exception), exception));
}
