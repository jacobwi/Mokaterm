using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Ssh.Forwarding;

/// <summary>
/// Agent and X11 forwarding for one connection, the <c>ssh -A</c> and <c>ssh -X</c> of a saved login. Kept in
/// <see cref="ProtocolOptions"/> under the ssh prefix, inside <see cref="SshConnectionOptions"/>. Both are off unless
/// the connection turns them on, because both let the server's programs reach something on this machine.
/// </summary>
public sealed record SshForwardingOptions
{
	public const string ForwardAgentKey = "ssh.forwardAgent";

	public const string ForwardX11Key = "ssh.forwardX11";

	public const string X11DisplayKey = "ssh.x11Display";

	public static SshForwardingOptions None { get; } = new();

	/// <summary>Offer the agent to programs on the server while the session lasts.</summary>
	public bool ForwardAgent { get; init; }

	/// <summary>Show programs from the server on the local X server.</summary>
	public bool ForwardX11 { get; init; }

	/// <summary>The local X display, such as <c>localhost:0.0</c>. Null means that default.</summary>
	public string? Display { get; init; }

	public static SshForwardingOptions From(ProtocolOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		string? display = options.GetString(X11DisplayKey);
		return new SshForwardingOptions
		{
			ForwardAgent = options.GetBoolean(ForwardAgentKey, fallback: false),
			ForwardX11 = options.GetBoolean(ForwardX11Key, fallback: false),
			Display = string.IsNullOrWhiteSpace(display) ? null : display.Trim(),
		};
	}

	/// <summary>Writes these values into <paramref name="options"/>; what is off or unset removes its key.</summary>
	public ProtocolOptions ApplyTo(ProtocolOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		return options
			.With(ForwardAgentKey, ForwardAgent ? true : null)
			.With(ForwardX11Key, ForwardX11 ? true : null)
			.With(X11DisplayKey, string.IsNullOrWhiteSpace(Display) ? null : Display.Trim());
	}
}
