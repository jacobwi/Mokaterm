using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// The proxy group of a protocol's options editor: kind, address, port, user and password. Shared so every protocol that
/// can dial through a proxy shows the same fields and the same rules. The password is not part of
/// <see cref="ProxyOptions"/>: it is a secret, and the connection editor saves it with the login's credential.
/// </summary>
public partial class ProxyFields : ComponentBase
{
	// What the user typed while it is not a port, so a keystroke is not swallowed.
	private string _portDraft = "";
	private string? _portError;

	[Parameter, EditorRequired]
	public ProxyOptions Proxy { get; set; } = ProxyOptions.None;

	[Parameter]
	public EventCallback<ProxyOptions> ProxyChanged { get; set; }

	/// <summary>What the password field shows. Empty means a stored password, if there is one, stays as it is.</summary>
	[Parameter]
	public string ProxyPassword { get; set; } = "";

	/// <summary>Null keeps the stored password, an empty string forgets it, anything else replaces it.</summary>
	[Parameter]
	public EventCallback<string?> ProxyPasswordChanged { get; set; }

	/// <summary>True when a proxy password is already stored with the login's credential.</summary>
	[Parameter]
	public bool HasProxyPassword { get; set; }

	/// <summary>False when the login keeps no credential to store a proxy password in, so it is asked for at connect time.</summary>
	[Parameter]
	public bool CanSaveProxyPassword { get; set; }

	[Parameter]
	public bool Disabled { get; set; }

	private bool IsOn => Proxy.Kind != ProxyKind.None;

	private string KindHelp => Proxy.Kind switch
	{
		ProxyKind.Http => "The proxy is asked for a tunnel with CONNECT. A proxy that wants a login gets it as HTTP basic.",
		ProxyKind.Socks4 => "SOCKS4 takes an address this machine resolved, and carries the proxy user as its ident string.",
		ProxyKind.Socks5 => "SOCKS5 resolves the address itself and can ask for a user name and password.",
		_ => "The server is dialled from this machine, with no proxy in between.",
	};

	private string PasswordHelp => (CanSaveProxyPassword, HasProxyPassword) switch
	{
		(true, true) => "Saved with this login. Leave it empty to keep it, or turn the proxy off to forget it.",
		(true, false) => "Only needed when the proxy asks for a login.",
		(false, true) => "Saved with the credential this login uses. Change it where that credential is edited.",
		(false, false) => "Asked for while connecting: this login keeps no credential of its own to save it in.",
	};

	private string PortPlaceholder => ProxyOptions.DefaultPortFor(Proxy.Kind).ToString(CultureInfo.InvariantCulture);

	private string PortText => _portError is null ? Proxy.Port?.ToString(CultureInfo.InvariantCulture) ?? "" : _portDraft;

	private string? HostError => IsOn ? ProxyOptions.ValidateHost(Proxy.Host) : null;

	private string? UserError => IsOn ? ProxyOptions.ValidateUser(Proxy.User) : null;

	private Task OnKindChangedAsync(string value)
	{
		if (!Enum.TryParse(value, out ProxyKind kind) || !Enum.IsDefined(kind) || kind == Proxy.Kind)
		{
			return Task.CompletedTask;
		}

		ClearPortDraft();

		// Nothing dials through the proxy once it is off, so the password saved for it goes with it.
		return kind == ProxyKind.None
			? ChangeAsync(ProxyOptions.None, proxyPassword: "")
			: ChangeAsync(Proxy with { Kind = kind });
	}

	private Task OnHostChangedAsync(string value) => ChangeAsync(Proxy with { Host = value });

	private Task OnUserChangedAsync(string value) => ChangeAsync(Proxy with { User = value });

	private Task OnPortChangedAsync(string value)
	{
		if (value.Length == 0)
		{
			ClearPortDraft();
			return ChangeAsync(Proxy with { Port = null });
		}

		// 0 stands in for text that is not a number at all, so one rule produces the message either way.
		int port = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;
		if (ProxyOptions.ValidatePort(port) is { } error)
		{
			_portDraft = value;
			_portError = error;
			return Task.CompletedTask;
		}

		ClearPortDraft();
		return ChangeAsync(Proxy with { Port = port });
	}

	private Task OnPasswordChangedAsync(string? value) =>
		ProxyPasswordChanged.InvokeAsync(string.IsNullOrEmpty(value) ? null : value);

	private void ClearPortDraft()
	{
		_portDraft = "";
		_portError = null;
	}

	private Task ChangeAsync(ProxyOptions proxy) => ProxyChanged.InvokeAsync(proxy);

	private async Task ChangeAsync(ProxyOptions proxy, string? proxyPassword)
	{
		await ProxyPasswordChanged.InvokeAsync(proxyPassword);
		await ProxyChanged.InvokeAsync(proxy);
	}
}
