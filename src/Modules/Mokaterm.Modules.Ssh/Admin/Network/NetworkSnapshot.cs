namespace Mokaterm.Modules.Ssh.Admin.Network;

/// <summary>Everything the network page shows, read in one go.</summary>
internal sealed record NetworkSnapshot
{
	public IReadOnlyList<NetInterface> Interfaces { get; init; } = [];

	public IReadOnlyList<NetRoute> Routes { get; init; } = [];

	/// <summary>Name servers by where they apply: <c>Global</c>, a link such as <c>eth0</c>, or <c>resolv.conf</c>.</summary>
	public IReadOnlyList<(string Scope, IReadOnlyList<string> Servers)> Dns { get; init; } = [];

	/// <summary>Search domains, from resolv.conf or resolvectl.</summary>
	public IReadOnlyList<string> SearchDomains { get; init; } = [];

	public IReadOnlyList<ListeningSocket> Listening { get; init; } = [];

	/// <summary>Which tool listed the ports: <c>ss</c>, <c>netstat</c>, or empty when neither is installed.</summary>
	public string ListeningSource { get; init; } = "";

	/// <summary>True when the interfaces came from text output because <c>ip -j</c> is missing (BusyBox, old iproute2).</summary>
	public bool FromText { get; init; }
}
