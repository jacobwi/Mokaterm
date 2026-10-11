using System.Text;
using Mokaterm.Modules.Local.Pty;

namespace Mokaterm.Modules.Local.Tests.Fakes;

/// <summary>Collects what a real pseudo terminal writes, from its reader thread.</summary>
internal sealed class RecordingOutput : IPseudoTerminalOutput
{
	private readonly Lock _lock = new();
	private readonly List<byte> _bytes = [];

	public string Text
	{
		get
		{
			lock (_lock)
			{
				return Encoding.UTF8.GetString([.. _bytes]);
			}
		}
	}

	public bool Write(ReadOnlySpan<byte> chunk)
	{
		lock (_lock)
		{
			_bytes.AddRange(chunk);
		}

		return true;
	}
}
