using System.Collections.Concurrent;
using Mokaterm.Modules.Rdp.Protocol;
using Mokaterm.Modules.Rdp.Sessions;
using Action = Devolutions.IronRdp.Action;

namespace Mokaterm.Modules.Rdp.Tests.Fakes;

/// <summary>
/// Stands in for IronRDP's handles. It answers the way a test tells it to, paints a gradient, and notices when two
/// calls overlap or the stage is freed during one, which on the real handles would be memory corruption.
/// </summary>
internal sealed class FakeStage : IRdpStage
{
	/// <summary>The I/O channel a Windows server usually gives the session.</summary>
	public const int DefaultIoChannel = 1003;

	private int _inside;
	private int _violations;
	private int _disposed;

	public FakeStage(int width, int height)
	{
		Width = width;
		Height = height;
	}

	public int IoChannelId => DefaultIoChannel;

	public int Width { get; private set; }

	public int Height { get; private set; }

	/// <summary>Every message the session handed over, in order.</summary>
	public ConcurrentQueue<byte[]> Processed { get; } = new();

	public ConcurrentQueue<RdpOperation> Inputs { get; } = new();

	public ConcurrentQueue<(int Width, int Height)> ResizeRequests { get; } = new();

	public ConcurrentQueue<(int Width, int Height)> Resets { get; } = new();

	/// <summary>What one processed message turns into. Nothing by default.</summary>
	public Func<byte[], IReadOnlyList<RdpStageOutput>> OnProcess { get; set; } = static _ => [];

	/// <summary>Thrown from <see cref="CopyDesktop"/> when set.</summary>
	public Exception? CopyFailure { get; set; }

	public bool SupportsResize { get; set; } = true;

	public bool Disposed => Volatile.Read(ref _disposed) != 0;

	/// <summary>Calls that overlapped another or arrived after the stage was freed.</summary>
	public int Violations => Volatile.Read(ref _violations);

	/// <summary>What <see cref="ApplyInput"/> answers with, so a test can find it in what the session sent.</summary>
	public static byte[] InputReply(RdpOperation operation) => [0xF0, (byte)operation.Kind, (byte)operation.A];

	public static byte[] ResizeReply => [0xD1, 0x5E];

	public void Process(Action action, byte[] payload, List<RdpStageOutput> outputs) => Inside(() =>
	{
		Processed.Enqueue(payload);
		outputs.AddRange(OnProcess(payload));
	});

	public void ApplyInput(RdpOperation operation, List<RdpStageOutput> outputs) => Inside(() =>
	{
		Inputs.Enqueue(operation);
		outputs.Add(RdpStageOutput.Response(InputReply(operation)));
	});

	public bool TryEncodeResize(int width, int height, List<RdpStageOutput> outputs)
	{
		bool supported = SupportsResize;
		Inside(() =>
		{
			ResizeRequests.Enqueue((width, height));
			if (supported)
			{
				outputs.Add(RdpStageOutput.Response(ResizeReply));
			}
		});

		return supported;
	}

	public byte[] CopyDesktop(byte[] buffer)
	{
		byte[] copy = buffer;
		Inside(() =>
		{
			if (CopyFailure is { } failure)
			{
				throw failure;
			}

			byte[] desktop = Desktop(Width, Height);
			copy = buffer.Length == desktop.Length ? buffer : new byte[desktop.Length];
			desktop.CopyTo(copy, 0);
		});

		return copy;
	}

	public void ResetDesktop(int width, int height) => Inside(() =>
	{
		Width = width;
		Height = height;
		Resets.Enqueue((width, height));
	});

	public void Dispose()
	{
		if (Volatile.Read(ref _inside) != 0)
		{
			Interlocked.Increment(ref _violations);
		}

		Volatile.Write(ref _disposed, 1);
	}

	/// <summary>The picture this stage decodes at a size: a gradient with the alpha left at zero, which the encoder must fix.</summary>
	public static byte[] Desktop(int width, int height)
	{
		byte[] pixels = RdpHarness.Gradient(width, height);
		for (int i = 3; i < pixels.Length; i += 4)
		{
			pixels[i] = 0;
		}

		return pixels;
	}

	private void Inside(System.Action call)
	{
		if (Interlocked.Increment(ref _inside) != 1 || Disposed)
		{
			Interlocked.Increment(ref _violations);
		}

		try
		{
			call();
		}
		finally
		{
			Interlocked.Decrement(ref _inside);
		}
	}
}
