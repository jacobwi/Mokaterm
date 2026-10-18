using Mokaterm.Abstractions.Commands;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Live output of a command run through sudo, as a view should hear it: stdout as it comes, and stderr only from the
/// line after <see cref="RemoteScripts.ElevatedMarker"/>, because what comes before it is sudo's own (a prompt, a
/// lecture, a refusal) and is read from the outcome instead. A stderr that never reaches the marker shows nothing live.
/// </summary>
internal sealed class ElevatedOutputGate
{
	// sudo prints a line or two before the marker; past this much, what comes is not sudo's and is not held for it.
	private const int MaxHeldBytes = 64 * 1024;

	private static readonly byte[] Marker = System.Text.Encoding.ASCII.GetBytes(RemoteScripts.ElevatedMarker + "\n");

	private readonly CommandOutputHandler _target;
	private byte[] _held = new byte[256];
	private int _heldCount;
	private bool _open;
	private bool _gaveUp;

	public ElevatedOutputGate(CommandOutputHandler target)
	{
		ArgumentNullException.ThrowIfNull(target);
		_target = target;
	}

	/// <summary>The handler to give the request. Only the stderr loop touches the held bytes, so no lock is needed.</summary>
	public void Write(CommandStream stream, ReadOnlySpan<byte> bytes)
	{
		if (stream == CommandStream.Output || _open)
		{
			_target(stream, bytes);
			return;
		}

		if (_gaveUp)
		{
			return;
		}

		if (_heldCount + bytes.Length > MaxHeldBytes)
		{
			_gaveUp = true;
			_held = [];
			_heldCount = 0;
			return;
		}

		if (_heldCount + bytes.Length > _held.Length)
		{
			Array.Resize(ref _held, Math.Max(_held.Length * 2, _heldCount + bytes.Length));
		}

		bytes.CopyTo(_held.AsSpan(_heldCount));
		_heldCount += bytes.Length;
		ReadOnlySpan<byte> held = _held.AsSpan(0, _heldCount);
		int found = held.IndexOf(Marker);
		if (found < 0)
		{
			return;
		}

		_open = true;
		ReadOnlySpan<byte> rest = held[(found + Marker.Length)..];
		if (!rest.IsEmpty)
		{
			_target(CommandStream.Error, rest);
		}

		_held = [];
		_heldCount = 0;
	}
}
