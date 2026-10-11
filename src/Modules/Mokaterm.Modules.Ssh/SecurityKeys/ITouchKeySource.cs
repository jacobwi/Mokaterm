namespace Mokaterm.Modules.Ssh.SecurityKeys;

/// <summary>A key source with keys that may wait for a touch, so the login attempt hands it a gate while it runs.</summary>
internal interface ITouchKeySource
{
	SecurityKeyTouchSlot Touch { get; }
}
