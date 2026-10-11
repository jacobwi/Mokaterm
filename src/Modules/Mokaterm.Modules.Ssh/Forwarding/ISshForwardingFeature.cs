namespace Mokaterm.Modules.Ssh.Forwarding;

/// <summary>Agent and X11 forwarding of a live SSH session, through <c>GetFeature&lt;ISshForwardingFeature&gt;()</c>.</summary>
internal interface ISshForwardingFeature
{
	/// <summary>Raised from any thread when a channel opens or closes, or something goes wrong.</summary>
	event Action? Changed;

	SshForwardingLine Agent { get; }

	SshForwardingLine X11 { get; }
}
