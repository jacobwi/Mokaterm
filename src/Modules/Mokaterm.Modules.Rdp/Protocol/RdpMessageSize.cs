using Devolutions.IronRdp;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// Finds the length of the next message in what has been read so far, the job an IronRDP <c>PduHint</c> does. Disposing
/// it releases the hint it wraps.
/// </summary>
internal sealed class RdpMessageSize : IDisposable
{
	private readonly Func<byte[], nuint?> _find;
	private readonly IDisposable? _owner;

	/// <param name="find">The length of the message at the start of the bytes, or null while they do not say yet.</param>
	/// <param name="owner">What <paramref name="find"/> reads through, released with this.</param>
	public RdpMessageSize(Func<byte[], nuint?> find, IDisposable? owner = null)
	{
		ArgumentNullException.ThrowIfNull(find);
		_find = find;
		_owner = owner;
	}

	/// <summary>Takes over <paramref name="hint"/>, which goes when this does.</summary>
	public static RdpMessageSize Of(PduHint hint)
	{
		ArgumentNullException.ThrowIfNull(hint);
		return new RdpMessageSize(bytes => Find(hint, bytes), hint);
	}

	/// <summary>Asks <paramref name="hint"/> without taking it over.</summary>
	public static nuint? Find(PduHint hint, byte[] bytes)
	{
		ArgumentNullException.ThrowIfNull(hint);
		using OptionalUsize found = hint.FindSize(bytes);
		return found.IsSome() ? found.Get() : null;
	}

	public nuint? Find(byte[] bytes) => _find(bytes);

	public void Dispose() => _owner?.Dispose();
}
