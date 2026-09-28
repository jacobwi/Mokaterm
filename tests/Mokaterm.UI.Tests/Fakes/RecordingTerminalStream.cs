using System.Text;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.UI.Tests.Fakes;

/// <summary>A terminal stream that keeps what it was told to send.</summary>
internal sealed class RecordingTerminalStream : ITerminalStream
{
	public event Action? StateChanged
	{
		add { }
		remove { }
	}

	public List<string> Sent { get; } = [];

	public List<string> Local { get; } = [];

	public TerminalSize? LastSize { get; private set; }

	public TerminalSize Size => new(80, 24);

	public bool IsOpen { get; set; } = true;

	public IDisposable Attach(ITerminalSink sink) => throw new NotSupportedException();

	public ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
	{
		Sent.Add(Encoding.UTF8.GetString(data.Span));
		return ValueTask.CompletedTask;
	}

	public ValueTask SendTextAsync(string text, CancellationToken cancellationToken = default)
	{
		Sent.Add(text);
		return ValueTask.CompletedTask;
	}

	public ValueTask ResizeAsync(TerminalSize size, CancellationToken cancellationToken = default)
	{
		LastSize = size;
		return ValueTask.CompletedTask;
	}

	public ValueTask WriteLocalAsync(string text, CancellationToken cancellationToken = default)
	{
		Local.Add(text);
		return ValueTask.CompletedTask;
	}
}
