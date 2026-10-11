using System.Globalization;
using System.Text.RegularExpressions;
using Mokaterm.Modules.Ssh.Admin.Detection;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Admin.Firewall;

/// <summary>
/// The firewall's status for every kind, and for ufw and firewalld the changes this view offers: on, off, allow a port,
/// deny a port. Anything that could shut out the SSH port this session arrived on says so before it runs, and turning
/// a firewall on opens that port first.
/// </summary>
internal sealed partial class FirewallCommands
{
	public FirewallCommands(FirewallKind kind, int sshPort)
	{
		if (kind == FirewallKind.None)
		{
			throw new ArgumentException("A machine without a firewall tool has no firewall commands.", nameof(kind));
		}

		Kind = kind;
		SshPort = sshPort is > 0 and <= 65535 ? sshPort : 22;
	}

	public FirewallKind Kind { get; }

	/// <summary>The port this session arrived on, from <c>SSH_CONNECTION</c>, or 22 when the server did not say.</summary>
	public int SshPort { get; }

	/// <summary>Only ufw and firewalld are changed here; raw nftables and iptables rule sets are shown, not edited.</summary>
	public bool CanChange => Kind is FirewallKind.Ufw or FirewallKind.Firewalld;

	public string DisplayName => Kind switch
	{
		FirewallKind.Ufw => "ufw",
		FirewallKind.Firewalld => "firewalld",
		FirewallKind.Nftables => "nftables",
		_ => "iptables",
	};

	/// <summary>Every firewall tool needs root to say anything at all.</summary>
	public AdminScript Status() => new()
	{
		Name = "firewall.status",
		AsRoot = true,
		Body = Kind switch
		{
			FirewallKind.Ufw => "ufw status verbose 2>&1",
			FirewallKind.Firewalld => string.Join('\n',
				AdminSections.Start("state"),
				"firewall-cmd --state 2>&1",
				AdminSections.Start("zone"),
				"firewall-cmd --get-default-zone 2>/dev/null",
				AdminSections.Start("all"),
				"firewall-cmd --list-all 2>/dev/null",
				"exit 0"),
			FirewallKind.Nftables => "nft list ruleset 2>&1",
			_ => "iptables -S 2>&1\necho\nip6tables -S 2>&1",
		},
	};

	public FirewallStatus Parse(AdminOutcome outcome)
	{
		ArgumentNullException.ThrowIfNull(outcome);
		return Kind switch
		{
			FirewallKind.Ufw => ParseUfw(outcome.Output),
			FirewallKind.Firewalld => ParseFirewalld(outcome.Output),
			_ => new FirewallStatus { Raw = outcome.Output.TrimEnd() },
		};
	}

	/// <summary>
	/// <c>ufw status verbose</c>: <c>Key: value</c> lines, then a <c>To / Action / From</c> table whose columns are
	/// separated by two or more spaces.
	/// </summary>
	public static FirewallStatus ParseUfw(string output)
	{
		bool? active = null;
		List<(string Key, string Value)> summary = [];
		List<FirewallRule> rules = [];
		bool inTable = false;
		foreach (string line in AdminSections.Lines(output))
		{
			if (inTable)
			{
				string[] cells = Columns().Split(line.Trim());
				if (cells.Length >= 3)
				{
					rules.Add(new FirewallRule(cells[0], cells[1], string.Join(' ', cells[2..])));
				}

				continue;
			}

			if (line.StartsWith("--", StringComparison.Ordinal))
			{
				inTable = true;
				continue;
			}

			int colon = line.IndexOf(':', StringComparison.Ordinal);
			if (colon <= 0 || line.StartsWith("To ", StringComparison.Ordinal))
			{
				continue;
			}

			string key = line[..colon].Trim();
			string value = line[(colon + 1)..].Trim();
			if (key == "Status")
			{
				active = value switch
				{
					"active" => true,
					"inactive" => false,
					_ => null,
				};
			}
			else
			{
				summary.Add((key, value));
			}
		}

		return new FirewallStatus { Active = active, Summary = summary, Rules = rules };
	}

	/// <summary>
	/// firewalld's state, default zone and <c>--list-all</c>: a zone line, <c>key: value</c> lines, and rich rules
	/// indented under their key.
	/// </summary>
	public static FirewallStatus ParseFirewalld(string output)
	{
		AdminSections sections = AdminSections.Parse(output);
		string state = AdminSections.Lines(sections.Get("state")).FirstOrDefault()?.Trim() ?? "";
		List<(string Key, string Value)> summary = [];
		if (AdminSections.Lines(sections.Get("zone")).FirstOrDefault()?.Trim() is { Length: > 0 } zone)
		{
			summary.Add(("Default zone", zone));
		}

		List<FirewallRule> rules = [];
		bool inRichRules = false;
		foreach (string line in AdminSections.Lines(sections.Get("all")))
		{
			string trimmed = line.Trim();
			int colon = trimmed.IndexOf(':', StringComparison.Ordinal);
			bool keyLine = colon > 0 && !trimmed.StartsWith("rule ", StringComparison.Ordinal);
			if (inRichRules && !keyLine)
			{
				rules.Add(new FirewallRule(trimmed, "RICH RULE", ""));
				continue;
			}

			inRichRules = false;
			if (!keyLine)
			{
				continue;
			}

			string key = trimmed[..colon].Trim();
			string value = trimmed[(colon + 1)..].Trim();
			switch (key)
			{
				case "services":
					rules.AddRange(Words(value).Select(service => new FirewallRule(service, "ALLOW", "service")));
					break;
				case "ports":
					rules.AddRange(Words(value).Select(port => new FirewallRule(port, "ALLOW", "port")));
					break;
				case "rich rules":
					inRichRules = true;
					break;
				case "target" or "interfaces" or "sources" or "masquerade" or "forward":
					if (value.Length > 0)
					{
						summary.Add((key, value));
					}

					break;
			}
		}

		return new FirewallStatus
		{
			Active = state switch
			{
				"running" => true,
				"not running" => false,
				_ => null,
			},
			Summary = summary,
			Rules = rules,
		};
	}

	/// <summary>Whether the rules already let this session's port in over TCP; null when the firewall's rules were not read.</summary>
	public bool? AllowsSsh(FirewallStatus? status)
	{
		if (status is null || status.Rules.Count == 0 && status.Active != true)
		{
			return null;
		}

		string port = SshPort.ToString(CultureInfo.InvariantCulture);
		return status.Rules.Any(rule =>
		{
			if (!rule.Action.StartsWith("ALLOW", StringComparison.Ordinal) && !rule.Action.StartsWith("LIMIT", StringComparison.Ordinal))
			{
				return false;
			}

			string target = rule.Target.Split(' ', 2)[0];
			if (SshPort == 22 && target is "ssh" or "OpenSSH")
			{
				return true;
			}

			string[] parts = target.Split('/');
			return parts[0].Split(',').Contains(port) && (parts.Length == 1 || parts[1] == "tcp");
		});
	}

	/// <summary>
	/// Turns the firewall on. Over SSH that is how machines get locked out, so this session's port is allowed first
	/// unless the rules read earlier already allow it.
	/// </summary>
	public AdminAction Enable(FirewallStatus? status)
	{
		RequireChange();
		string ssh = string.Create(CultureInfo.InvariantCulture, $"{SshPort}/tcp");
		bool allowFirst = AllowsSsh(status) != true;
		string body = Kind == FirewallKind.Ufw
			? (allowFirst ? $"set -e\nufw allow {ssh}\n" : "") + "ufw --force enable"
			: $"set -e\nsystemctl enable --now firewalld\nfirewall-cmd --permanent --add-port={ssh}\nfirewall-cmd --reload";

		return new AdminAction
		{
			Title = $"Turn on {DisplayName}",
			Script = Root("firewall.enable", body),
			Description = Kind == FirewallKind.Ufw && !allowFirst
				? $"Turns the firewall on and keeps it on at boot. The rules already allow {ssh}, the port this session arrived on."
				: $"Turns the firewall on and keeps it on at boot. {ssh} is allowed first, because this session arrived on it.",
			Warning = "Any rule that denies this session's own address still applies once the firewall is on.",
			CutsConnection = true,
			ConfirmText = "Turn on",
		};
	}

	public AdminAction Disable()
	{
		RequireChange();
		return new AdminAction
		{
			Title = $"Turn off {DisplayName}",
			Script = Root("firewall.disable", Kind == FirewallKind.Ufw ? "ufw disable" : "systemctl disable --now firewalld"),
			Description = "Stops filtering and keeps it off at boot. Every port a service listens on becomes reachable.",
			Destructive = true,
			ConfirmText = "Turn off",
		};
	}

	public AdminAction Allow(FirewallPort port)
	{
		RequireChange();
		ArgumentNullException.ThrowIfNull(port);
		string body = Kind == FirewallKind.Ufw
			? $"ufw allow {PosixShell.Quote(port.ToString())}"
			: string.Join('\n', ["set -e", .. Protocols(port).Select(protocol => $"firewall-cmd --permanent {PosixShell.Quote($"--add-port={port.Port}/{protocol}")}"), "firewall-cmd --reload"]);

		return new AdminAction
		{
			Title = $"Allow {port}",
			Script = Root("firewall.allow", body),
			Description = Kind == FirewallKind.Ufw
				? "Adds a rule that lets connections to the port in."
				: "Opens the port in the default zone, permanently, and reloads.",
			ConfirmText = "Allow",
		};
	}

	public AdminAction Deny(FirewallPort port)
	{
		RequireChange();
		ArgumentNullException.ThrowIfNull(port);
		string body = Kind == FirewallKind.Ufw
			? $"ufw deny {PosixShell.Quote(port.ToString())}"
			: string.Join('\n',
			[
				"set -e",
				.. Protocols(port).SelectMany(protocol => (string[])
				[
					$"firewall-cmd --permanent {PosixShell.Quote($"--remove-port={port.Port}/{protocol}")} || true",
					$"firewall-cmd --permanent {PosixShell.Quote($"--add-rich-rule=rule port port=\"{port.Port}\" protocol=\"{protocol}\" reject")}",
				]),
				"firewall-cmd --reload",
			]);

		bool cutsSession = port.CoversTcp(SshPort);
		return new AdminAction
		{
			Title = $"Deny {port}",
			Script = Root("firewall.deny", body),
			Description = Kind == FirewallKind.Ufw
				? "Adds a rule that drops connections to the port."
				: "Removes the port from the default zone and adds a rule that rejects it, permanently, and reloads.",
			Warning = cutsSession
				? $"This session arrived on port {SshPort}. Denying it refuses every new SSH login, this app's included, and getting back in needs a console."
				: null,
			CutsConnection = cutsSession,
			Destructive = true,
			ConfirmText = cutsSession ? "Deny anyway" : "Deny",
		};
	}

	private static IEnumerable<string> Protocols(FirewallPort port) => port.Protocol.Length == 0 ? ["tcp", "udp"] : [port.Protocol];

	private static string[] Words(string value) => value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

	private static AdminScript Root(string name, string body) => new()
	{
		Name = name,
		Body = body,
		AsRoot = true,
		Timeout = AdminScript.ActionTimeout,
		OutputLimitBytes = AdminScript.ActionOutputBytes,
	};

	private void RequireChange()
	{
		if (!CanChange)
		{
			throw new InvalidOperationException($"{DisplayName} rules are shown here, not changed.");
		}
	}

	[GeneratedRegex(@"\s{2,}", RegexOptions.CultureInvariant)]
	private static partial Regex Columns();
}
