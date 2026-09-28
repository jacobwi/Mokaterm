using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Renci.SshNet;

namespace Mokaterm.Modules.Ssh.Connection;

/// <summary>
/// The connection's proxy as SSH.NET wants it. SSH.NET runs the CONNECT request and the SOCKS handshakes itself once a
/// <see cref="ConnectionInfo"/> carries a <see cref="ProxyTypes"/>, so nothing here speaks either protocol.
/// </summary>
internal static class SshProxyDial
{
	/// <summary>
	/// The proxy a dial really uses. Behind jump hosts the socket goes to a loopback port an earlier hop holds open, so a
	/// proxy asked to reach it would be asked to reach this machine: only a hop that is dialled directly uses its own.
	/// </summary>
	public static ProxyOptions ForDial(ProxyOptions configured, bool dialsDirectly)
	{
		ArgumentNullException.ThrowIfNull(configured);
		return dialsDirectly ? configured : ProxyOptions.None;
	}

	/// <summary>
	/// The connection info for one attempt. <paramref name="proxy"/> must have passed <see cref="ProxyOptions.EnsureUsable"/>;
	/// anything that does not name a proxy dials the address itself.
	/// </summary>
	public static ConnectionInfo CreateConnectionInfo(
		SshEndpoint dial,
		string username,
		ProxyOptions proxy,
		SecretBuffer? proxyPassword,
		AuthenticationMethod[] methods)
	{
		ArgumentNullException.ThrowIfNull(dial);
		ArgumentNullException.ThrowIfNull(proxy);
		if (!proxy.IsEnabled)
		{
			return new ConnectionInfo(dial.Host, dial.Port, username, methods);
		}

		// SSH.NET writes the proxy user and password to the socket without checking them for null, and its SOCKS4
		// connector sends the user name as the ident string even when the proxy asks for no login, so both are strings.
		return new ConnectionInfo(
			dial.Host,
			dial.Port,
			username,
			ToProxyType(proxy.Kind),
			proxy.Host!,
			proxy.EffectivePort,
			proxy.User ?? "",
			proxyPassword?.RevealString() ?? "",
			methods);
	}

	public static ProxyTypes ToProxyType(ProxyKind kind) => kind switch
	{
		ProxyKind.Http => ProxyTypes.Http,
		ProxyKind.Socks4 => ProxyTypes.Socks4,
		ProxyKind.Socks5 => ProxyTypes.Socks5,
		_ => ProxyTypes.None,
	};
}
