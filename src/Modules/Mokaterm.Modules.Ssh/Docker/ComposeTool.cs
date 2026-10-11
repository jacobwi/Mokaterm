using System.Globalization;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// One compose found on the server. The <c>docker compose</c> plugin is always 2.x or later; a standalone
/// <c>docker-compose</c> is either the Python 1.x, which has no <c>ls</c> and no JSON at all, or a 2.x binary installed
/// under the old name, which has both. What a compose can do follows from its version alone, so this is the one place
/// that says it.
/// </summary>
internal sealed record ComposeTool(DockerProgram Program, string Version)
{
	/// <summary><c>docker compose</c> or <c>docker-compose</c>.</summary>
	public string Command => Program == DockerProgram.ComposePlugin ? "docker compose" : "docker-compose";

	/// <summary>The major version, or 0 when the version text did not start with a number.</summary>
	public int MajorVersion
	{
		get
		{
			string text = Version.TrimStart('v', 'V');
			int dot = text.IndexOf('.', StringComparison.Ordinal);
			return int.TryParse(dot < 0 ? text : text[..dot], NumberStyles.Integer, CultureInfo.InvariantCulture, out int major) ? major : 0;
		}
	}

	/// <summary>True for the Python 1.x compose.</summary>
	public bool IsLegacy => Program == DockerProgram.ComposeStandalone && MajorVersion is > 0 and < 2;

	/// <summary>True when <c>ls</c> exists; without it projects come from the containers' labels.</summary>
	public bool SupportsList => !IsLegacy;

	/// <summary>True when <c>ps</c> and <c>ls</c> take <c>--format json</c>.</summary>
	public bool SupportsJson => !IsLegacy;

	/// <summary>
	/// True when <c>config --format json</c> may work. Only 2.x has the flag, and early 2.x releases lack it too, so a
	/// caller that gets a failure reads the YAML instead.
	/// </summary>
	public bool MaySupportConfigJson => !IsLegacy;

	/// <summary><c>docker compose 2.29.7</c>.</summary>
	public string Display => $"{Command} {Version}";

	/// <summary>
	/// The version a version command printed: <c>2.29.7</c> and <c>v2.29.7</c> from <c>version --short</c>, or the long
	/// forms <c>Docker Compose version v2.29.7</c> and <c>docker-compose version 1.29.2, build 5becea4c</c> that
	/// <c>version</c> and <c>-v</c> print. Null when the output is no version, which is what a shell that found no compose
	/// leaves on stdout.
	/// </summary>
	public static string? ParseVersion(string? output)
	{
		string line = (output ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
		const string Marker = "version ";
		int at = line.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
		if (at >= 0)
		{
			line = line[(at + Marker.Length)..];
		}

		int end = line.IndexOfAny([',', ' ']);
		string version = (end < 0 ? line : line[..end]).TrimStart('v', 'V');
		return version.Length > 0
			&& char.IsAsciiDigit(version[0])
			&& version.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '+')
				? version
				: null;
	}
}
