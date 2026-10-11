namespace Mokaterm.Modules.Ssh.SecurityKeys;

/// <summary>
/// The touch gate of the attempt a key source currently serves. One key source outlives several attempts (a host key
/// confirmation reconnects with it), so each attempt attaches its own gate and only ever removes that one.
/// </summary>
internal sealed class SecurityKeyTouchSlot
{
	private SecurityKeyTouch? _current;

	public SecurityKeyTouch? Current => Volatile.Read(ref _current);

	public void Attach(SecurityKeyTouch touch) => Volatile.Write(ref _current, touch);

	/// <summary>Removes <paramref name="touch"/> unless a later attempt already replaced it.</summary>
	public void Detach(SecurityKeyTouch touch) => Interlocked.CompareExchange(ref _current, null, touch);
}
