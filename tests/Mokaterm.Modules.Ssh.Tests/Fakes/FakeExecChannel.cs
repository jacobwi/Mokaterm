using System.Text;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests.Fakes;

/// <summary>An exec channel whose streams, exit and completion the test hands over.</summary>
internal sealed class FakeExecChannel : IExecChannel
{
	private int _disposed;

	public FakeExecChannel(Stream output, Stream error, int? exitStatus, string? exitSignal = null, Task? execution = null)
	{
		Output = output;
		ErrorOutput = error;
		ExitStatus = exitStatus;
		ExitSignal = exitSignal;
		Execution = execution ?? Task.CompletedTask;
	}

	public Stream Output { get; }

	public Stream ErrorOutput { get; }

	public Task Execution { get; }

	public int? ExitStatus { get; }

	public string? ExitSignal { get; }

	public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

	/// <summary>A command that already ended, having printed <paramref name="output"/> and <paramref name="error"/>.</summary>
	public static FakeExecChannel Finished(byte[] output, string error, int exitStatus) =>
		new(new MemoryStream(output), new MemoryStream(Encoding.UTF8.GetBytes(error)), exitStatus);

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		// Closing the channel ends the reads that are still waiting, as SSH.NET does.
		Output.Dispose();
		ErrorOutput.Dispose();
	}
}
