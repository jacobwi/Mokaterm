using System.Text;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Local.Pty;

namespace Mokaterm.Modules.Local.Tests.Fakes;

/// <summary>A pseudo terminal the test drives: it prints, exits and records what was typed and every resize.</summary>
internal sealed class FakePseudoTerminal : IPseudoTerminal
{
	private readonly IPseudoTerminalOutput _output;
	private readonly TaskCompletionSource<int?> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly List<byte[]> _written = [];
	private readonly List<TerminalSize> _sizes = [];

	public FakePseudoTerminal(IPseudoTerminalOutput output) => _output = output;

	public int ProcessId => 4242;

	public Task<int?> Exited => _exited.Task;

	public bool Disposed { get; private set; }

	public string Typed
	{
		get
		{
			lock (_written)
			{
				return string.Concat(_written.Select(chunk => Encoding.UTF8.GetString(chunk)));
			}
		}
	}

	public IReadOnlyList<TerminalSize> Sizes
	{
		get
		{
			lock (_sizes)
			{
				return [.. _sizes];
			}
		}
	}

	/// <summary>Writes program output the way the reader thread would. False once nobody wants it.</summary>
	public bool Print(string text) => _output.Write(Encoding.UTF8.GetBytes(text));

	/// <summary>Ends the program: a code for an exit of its own, null for a console that went away under it.</summary>
	public void End(int? exitCode) => _exited.TrySetResult(exitCode);

	public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
	{
		lock (_written)
		{
			_written.Add(data.ToArray());
		}

		return ValueTask.CompletedTask;
	}

	public void Resize(TerminalSize size)
	{
		lock (_sizes)
		{
			_sizes.Add(size);
		}
	}

	public ValueTask DisposeAsync()
	{
		Disposed = true;
		_exited.TrySetResult(null);
		return ValueTask.CompletedTask;
	}
}
