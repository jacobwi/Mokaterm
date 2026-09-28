using System.Text;
using System.Threading.Channels;

namespace Mokaterm.DevHost.Demo.Terminal;

/// <summary>
/// Collects what a command prints and sends it to the terminal as UTF-8 chunks. Lines end in CRLF, as a pty would turn
/// them. Writing into a full channel waits, which is how a slow view slows a command down.
/// </summary>
internal sealed class ShellOutput
{
	private const int FlushThreshold = 8 * 1024;

	private readonly ChannelWriter<byte[]> _writer;
	private readonly StringBuilder _text = new();

	public ShellOutput(ChannelWriter<byte[]> writer) => _writer = writer;

	public void Write(string text) => _text.Append(text);

	public void WriteLine(string text) => _text.Append(text).Append("\r\n");

	public void WriteLine() => _text.Append("\r\n");

	/// <summary>Sends once enough has piled up. Long commands call this per line so output streams and Ctrl+C lands quickly.</summary>
	public ValueTask FlushIfFullAsync(CancellationToken cancellationToken) =>
		_text.Length >= FlushThreshold ? FlushAsync(cancellationToken) : ValueTask.CompletedTask;

	public async ValueTask FlushAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (_text.Length == 0)
		{
			return;
		}

		byte[] chunk = Encoding.UTF8.GetBytes(_text.ToString());
		_text.Clear();
		await _writer.WriteAsync(chunk, cancellationToken);
	}

	/// <summary>Writes <paramref name="text"/> and sends everything at once, for echoes and prompts.</summary>
	public ValueTask SendAsync(string text, CancellationToken cancellationToken)
	{
		_text.Append(text);
		return FlushAsync(cancellationToken);
	}
}
