using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.ServerAdmin;

namespace Mokaterm.Plugins.Sample.Fail2ban;

/// <summary>
/// The scripts the fail2ban page runs. Jail names never pass through here on the way to the read: the script takes them
/// from fail2ban's own list. A value that goes into a change (a jail, an address) is checked first and then quoted.
/// </summary>
internal static partial class Fail2banCommands
{
	/// <summary>The program the page needs; the System view's detection looks for it because the descriptor names it.</summary>
	public const string Client = "fail2ban-client";

	/// <summary>Starts each jail's part of <see cref="Status"/>'s output, followed by the jail's name.</summary>
	public const string JailMarker = "@@f2b:jail ";

	// set -f: the jail list is split into words unquoted, and a jail called * must not become every file here. The list
	// is read once, so a jail added meanwhile is not half listed.
	private const string StatusBody = """
		set -f
		status=$(fail2ban-client status 2>&1) || { code=$?; printf '%s\n' "$status" >&2; exit "$code"; }
		printf '%s\n' "$status"
		jails=$(printf '%s\n' "$status" | sed -n 's/^.*Jail list:[[:space:]]*//p' | tr ',' ' ')
		for jail in $jails; do
			printf '@@f2b:jail %s\n' "$jail"
			fail2ban-client status "$jail" 2>&1
		done
		exit 0
		""";

	/// <summary>Why the page cannot be used on this server, or null: the descriptor's rule and the page's empty state.</summary>
	public static string? Unavailable(ServerFacts server)
	{
		ArgumentNullException.ThrowIfNull(server);
		return server.HasCommand(Client) ? null : "fail2ban is not installed here: there is no fail2ban-client command.";
	}

	/// <summary>Every jail's status in one round trip; as root when <paramref name="asRoot"/>, since fail2ban's socket usually answers only root.</summary>
	public static SystemScript Status(bool asRoot) => new()
	{
		Name = "fail2ban.status",
		Body = StatusBody,
		AsRoot = asRoot,
	};

	/// <summary>True when fail2ban turned the login away from its socket, which root mode fixes.</summary>
	public static bool NeedsRoot(SystemScriptResult result)
	{
		ArgumentNullException.ThrowIfNull(result);
		string text = result.Error + "\n" + result.Output;
		return !result.Succeeded
			&& (text.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) || text.Contains("you must be root", StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>Lets <paramref name="address"/> back in to <paramref name="jail"/>, as root.</summary>
	/// <exception cref="ArgumentException">The jail or the address is not one fail2ban could have listed.</exception>
	public static SystemChange Unban(string jail, string address)
	{
		if (JailProblem(jail) is { } jailProblem)
		{
			throw new ArgumentException(jailProblem, nameof(jail));
		}

		if (AddressProblem(address) is { } addressProblem)
		{
			throw new ArgumentException(addressProblem, nameof(address));
		}

		return new SystemChange
		{
			Title = $"Unban {address} in {jail}",
			Description = "fail2ban lets the address in again at once. It is banned again if it keeps failing.",
			ConfirmText = "Unban",
			Script = new SystemScript
			{
				Name = "fail2ban.unban",
				Body = $"fail2ban-client set {PosixShell.Quote(jail)} unbanip {PosixShell.Quote(address)}",
				AsRoot = true,
			},
		};
	}

	/// <summary>Why <paramref name="jail"/> is not a jail name, or null. Jails are config sections: letters, digits and <c>_ . @ -</c>.</summary>
	public static string? JailProblem(string? jail) =>
		jail is not null && JailPattern().IsMatch(jail)
			? null
			: "A jail is named with letters, digits and the characters _ . @ -, starting with a letter or a digit.";

	/// <summary>
	/// Why <paramref name="address"/> is not an address fail2ban lists, or null: a dotted IPv4 address or an IPv6 one, with
	/// a prefix length when the jail bans networks. It goes to fail2ban as it was listed, so it has to be one already.
	/// </summary>
	public static string? AddressProblem(string? address)
	{
		const string Problem = "That is not an address fail2ban lists, such as 203.0.113.4 or 2001:db8::1.";
		if (string.IsNullOrEmpty(address) || address.Length > 64)
		{
			return Problem;
		}

		int slash = address.IndexOf('/', StringComparison.Ordinal);
		string host = slash < 0 ? address : address[..slash];
		if (!IPAddress.TryParse(host, out IPAddress? parsed))
		{
			return Problem;
		}

		// TryParse also takes 10.1 and 167772161 as IPv4; fail2ban lists four dotted parts.
		int bits = parsed.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
		bool shaped = parsed.AddressFamily == AddressFamily.InterNetwork ? DottedPattern().IsMatch(host) : host.Contains(':', StringComparison.Ordinal) && !host.Contains('%', StringComparison.Ordinal);
		bool prefix = slash < 0
			|| (int.TryParse(address.AsSpan(slash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int length) && length <= bits);
		return shaped && prefix ? null : Problem;
	}

	[GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.@-]{0,99}\z", RegexOptions.CultureInvariant)]
	private static partial Regex JailPattern();

	[GeneratedRegex(@"^[0-9]{1,3}(\.[0-9]{1,3}){3}\z", RegexOptions.CultureInvariant)]
	private static partial Regex DottedPattern();
}
