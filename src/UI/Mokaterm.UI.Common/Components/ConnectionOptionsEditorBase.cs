using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// Base for the protocol-specific part of the connection editor. The shell renders it with
/// <c>DynamicComponent</c> and owns saving; the editor only reports new <see cref="ProtocolOptions"/>.
/// </summary>
public abstract class ConnectionOptionsEditorBase : ComponentBase
{
	private int? _reportedPort;

	[Parameter, EditorRequired]
	public ProtocolOptions Options { get; set; } = ProtocolOptions.Empty;

	[Parameter]
	public EventCallback<ProtocolOptions> OptionsChanged { get; set; }

	/// <summary>The protocol being edited. A variant such as <c>sftp</c> can share its parent's editor.</summary>
	[Parameter]
	public string ProtocolId { get; set; } = "";

	[Parameter]
	public bool Disabled { get; set; }

	/// <summary>
	/// Raised by an editor whose options change what an empty port means, so the port field shows the number that will
	/// really be used (implicit FTPS answers on 990, not on the protocol's 21). Null goes back to the protocol default.
	/// </summary>
	[Parameter]
	public EventCallback<int?> DefaultPortChanged { get; set; }

	/// <summary>
	/// What the proxy password field shows, for the protocols that can dial through a proxy. A proxy password is a secret,
	/// so it is not in <see cref="Options"/>: the connection editor owns it and saves it with the login's credential.
	/// </summary>
	[Parameter]
	public string ProxyPassword { get; set; } = "";

	/// <summary>
	/// Reports the proxy password: null keeps the one stored with the login, an empty string forgets it, and anything else
	/// replaces it. An editor sends null for an empty field and an empty string when its proxy is turned off.
	/// </summary>
	[Parameter]
	public EventCallback<string?> ProxyPasswordChanged { get; set; }

	/// <summary>True when a proxy password is already stored with the login's credential.</summary>
	[Parameter]
	public bool HasProxyPassword { get; set; }

	/// <summary>
	/// False when the login keeps no credential of its own to store a proxy password in (an agent or anonymous login, one
	/// taken from the keychain, or a password the login asks for at connect time). The module then asks for the proxy
	/// password while it connects instead.
	/// </summary>
	[Parameter]
	public bool CanSaveProxyPassword { get; set; }

	/// <summary>
	/// Reports the port an empty port field stands for through <see cref="DefaultPortChanged"/>. Only a number that
	/// differs from the last one is reported: the editor above renders again for it, which brings the options back
	/// here, and reporting it again from there would never settle.
	/// </summary>
	protected void ReportDefaultPort(int? port)
	{
		if (port == _reportedPort)
		{
			return;
		}

		_reportedPort = port;
		_ = DefaultPortChanged.InvokeAsync(port);
	}

	protected Task SetAsync(string key, string? value) => OptionsChanged.InvokeAsync(Options.With(key, value));

	protected Task SetAsync(string key, int? value) => OptionsChanged.InvokeAsync(Options.With(key, value));

	protected Task SetAsync(string key, bool? value) => OptionsChanged.InvokeAsync(Options.With(key, value));

	protected Task SetEnumAsync<TEnum>(string key, TEnum? value) where TEnum : struct, Enum =>
		OptionsChanged.InvokeAsync(Options.WithEnum(key, value));
}
