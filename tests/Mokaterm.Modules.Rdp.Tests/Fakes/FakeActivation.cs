using System.Collections.Concurrent;
using Mokaterm.Modules.Rdp.Protocol;
using Mokaterm.Modules.Rdp.Sessions;

namespace Mokaterm.Modules.Rdp.Tests.Fakes;

/// <summary>A reactivation that reads a set number of TPKT messages, answers each, then finishes at a new size.</summary>
internal sealed class FakeActivation : IRdpActivation
{
	private readonly int _width;
	private readonly int _height;
	private int _stepsLeft;
	private int _disposed;

	public FakeActivation(int steps, int width, int height)
	{
		_stepsLeft = steps;
		_width = width;
		_height = height;
	}

	/// <summary>What each step was given.</summary>
	public ConcurrentQueue<byte[]> Inputs { get; } = new();

	public bool Disposed => Volatile.Read(ref _disposed) != 0;

	/// <summary>What every step answers with.</summary>
	public static byte[] Reply => [0x03, 0x00, 0x00, 0x05, 0xCA];

	public bool TryFinish(out int width, out int height)
	{
		bool done = _stepsLeft <= 0;
		width = done ? _width : 0;
		height = done ? _height : 0;
		return done;
	}

	public RdpMessageSize? NextInputSize() => new(RdpPdus.TpktLength);

	public byte[] Step(byte[]? input)
	{
		Inputs.Enqueue(input ?? []);
		_stepsLeft--;
		return Reply;
	}

	public void Dispose() => Volatile.Write(ref _disposed, 1);
}
