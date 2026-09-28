namespace Mokaterm.Core.Import;

/// <summary>Keeps imported logins free of ports that only repeat the protocol's own default.</summary>
internal static class ImportPorts
{
	public static int? Normalize(int? port, int defaultPort) =>
		port is { } value && value > 0 && value <= 65535 && value != defaultPort ? value : null;
}
