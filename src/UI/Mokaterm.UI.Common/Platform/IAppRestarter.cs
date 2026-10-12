namespace Mokaterm.UI.Common.Platform;

/// <summary>
/// Starts the app again, for changes that only a new start applies (plugins). Only a host that can launch itself
/// registers one, which is the desktop; a page offers "Restart now" only when it is there.
/// </summary>
public interface IAppRestarter
{
	/// <summary>
	/// Closes every session, writes pending settings and starts a new copy of the app, which takes over once this one
	/// has gone. False when the new copy could not be started; this one then keeps running.
	/// </summary>
	Task<bool> RestartAsync();
}
