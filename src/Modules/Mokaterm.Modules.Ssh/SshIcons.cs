using Moka.Red.Core.Icons;

namespace Mokaterm.Modules.Ssh;

/// <summary>Icons this module needs beyond the shared set, drawn the same way: single stroked path on a 24x24 grid.</summary>
internal static class SshIcons
{
	/// <summary>A pipe with an arrow through it, for port forwarding.</summary>
	public static readonly MokaIconDefinition Tunnel = new("mt-tunnel",
		"M2 12a10 6 0 0 1 20 0v6 M2 12v6 M7 12a5 3 0 0 1 10 0 M7 18v-6 M17 18v-6 M2 15h20");
}
