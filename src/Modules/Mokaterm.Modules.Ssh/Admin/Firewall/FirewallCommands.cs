using System.Globalization;
using System.Text.RegularExpressions;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Modules.Ssh.Admin.Detection;

namespace Mokaterm.Modules.Ssh.Admin.Firewall;

/// <summary>
/// The firewall's status for every kind, and for ufw and firewalld the changes this view offers: on, off, allow a port,
/// deny a port, delete a rule. Anything that could shut out the SSH port this session arrived on says so before it
/// runs, and turning a firewall on opens that port first.
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
			// The verbose list says the defaults; the numbered one gives the numbers a delete goes by.
			FirewallKind.Ufw => string.Join('\n',
				AdminSections.Start("verbose"),
				"ufw status verbose 2>&1",
				AdminSections.Start("numbered"),
				"ufw status numbered 2>/dev/null",
				"exit 0"),
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
			FirewallKind.Ufw => ParseUfwSections(outcome.Output),
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
	/// What <see cref="Status"/> prints for ufw: the verbose list, and the numbered one whose numbers go onto the rules
	/// they belong to. A rule whose numbered line does not say the same as its verbose line gets no number, so it cannot
	/// be deleted by a number that may mean another rule.
	/// </summary>
	public static FirewallStatus ParseUfwSections(string output)
	{
		AdminSections sections = AdminSections.Parse(output);
		FirewallStatus status = ParseUfw(sections.Get("verbose") ?? output);
		IReadOnlyList<FirewallRule> numbered = ParseUfwNumbered(sections.Get("numbered"));
		if (numbered.Count != status.Rules.Count)
		{
			return status;
		}

		List<FirewallRule> rules = [];
		for (int index = 0; index < numbered.Count; index++)
		{
			FirewallRule rule = status.Rules[index];
			FirewallRule match = numbered[index];
			rules.Add(match.Target == rule.Target && match.Action == rule.Action && match.From == rule.From
				? rule with { Number = match.Number, NumberedText = match.NumberedText }
				: rule);
		}

		return status with { Rules = rules };
	}

	/// <summary><c>ufw status numbered</c>: <c>[ 1] 22/tcp   ALLOW IN   Anywhere</c>, columns two or more spaces apart.</summary>
	public static IReadOnlyList<FirewallRule> ParseUfwNumbered(string? output)
	{
		List<FirewallRule> rules = [];
		foreach (string line in AdminSections.Lines(output))
		{
			Match match = NumberedRule().Match(line);
			if (!match.Success || !int.TryParse(match.Groups["number"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
			{
				continue;
			}

			string text = match.Groups["rule"].Value.Trim();
			string[] cells = Columns().Split(text);
			if (cells.Length >= 3)
			{
				rules.Add(new FirewallRule(cells[0], cells[1], string.Join(' ', cells[2..])) { Number = number, NumberedText = text });
			}
		}

		return rules;
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

		return status.Rules.Any(AllowsSsh);
	}

	/// <summary>Whether one rule lets this session's port in over TCP.</summary>
	public bool AllowsSsh(FirewallRule rule)
	{
		ArgumentNullException.ThrowIfNull(rule);
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
		return parts[0].Split(',').Contains(SshPort.ToString(CultureInfo.InvariantCulture)) && (parts.Length == 1 || parts[1] == "tcp");
	}

	/// <summary>
	/// Why <paramref name="rule"/> cannot be deleted here, or null: ufw needs the rule's number, firewalld deletes the
	/// zone's services, ports and rich rules, and nft and iptables rule sets are only shown.
	/// </summary>
	public string? WhyNotDelete(FirewallRule rule)
	{
		ArgumentNullException.ThrowIfNull(rule);
		return Kind switch
		{
			FirewallKind.Ufw => rule.Number is > 0 && rule.NumberedText.Length > 0 && !rule.NumberedText.Any(char.IsControl)
				? null
				: "ufw's numbered list did not match this rule. Read the rules again.",
			FirewallKind.Firewalld => rule.IsService && FirewalldService().IsMatch(rule.Target)
				|| rule.IsPort && FirewalldPort().IsMatch(rule.Target)
				|| rule.IsRichRule && rule.Target.StartsWith("rule ", StringComparison.Ordinal) && !rule.Target.Any(char.IsControl)
					? null
					: "Only the zone's services, ports and rich rules can be deleted here.",
			_ => $"{DisplayName} rules are shown here, not changed.",
		};
	}

	/// <summary>
	/// Deletes one rule. ufw goes by the rule's number, after checking the numbered list still shows the same rule
	/// there; firewalld removes it from the default zone's permanent settings and reloads. Taking away the last rule
	/// that lets this session's port in says so in red.
	/// </summary>
	public AdminAction Delete(FirewallRule rule, FirewallStatus status)
	{
		RequireChange();
		ArgumentNullException.ThrowIfNull(rule);
		ArgumentNullException.ThrowIfNull(status);
		if (WhyNotDelete(rule) is { } refusal)
		{
			throw new ArgumentException(refusal, nameof(rule));
		}

		string body;
		string title;
		if (Kind == FirewallKind.Ufw)
		{
			string number = rule.Number!.Value.ToString(CultureInfo.InvariantCulture);
			title = $"Delete ufw rule {number}";
			body = string.Join('\n',
				"set -e",
				$"current=$(ufw status numbered | sed -n 's/^\\[ *{number}\\] *//p' | sed 's/[[:space:]]*$//')",
				$"if [ \"$current\" != {PosixShell.Quote(rule.NumberedText)} ]; then",
				"\techo 'The rules changed since they were read. Read them again and pick the rule anew.' >&2",
				"\texit 1",
				"fi",
				$"ufw --force delete {number}");
		}
		else
		{
			string option = rule.IsService ? "--remove-service=" : rule.IsPort ? "--remove-port=" : "--remove-rich-rule=";
			title = rule.IsRichRule ? "Delete a rich rule" : $"Delete {rule.Target}";
			body = string.Join('\n', "set -e", $"firewall-cmd --permanent {PosixShell.Quote(option + rule.Target)}", "firewall-cmd --reload");
		}

		bool cutsSession = status.Active == true
			&& AllowsSsh(rule)
			&& !status.Rules.Any(other => other != rule && SameFamily(other, rule) && AllowsSsh(other));
		return new AdminAction
		{
			Title = title,
			Script = Root("firewall.delete", body),
			Description = Kind == FirewallKind.Ufw
				? $"Deletes the rule \"{rule.NumberedText}\" by its number, once the list shows the same rule at that number."
				: "Removes it from the default zone's permanent settings, then reloads so it stops applying now.",
			Warning = cutsSession
				? $"This is the rule that lets port {SshPort} in, which this session arrived on. Without it every new SSH login, this app's included, is refused, and getting back in needs a console."
				: null,
			CutsConnection = cutsSession,
			Destructive = true,
			ConfirmText = cutsSession ? "Delete anyway" : "Delete rule",
		};
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

	// ufw lists a rule once per address family, the IPv6 copy marked (v6); one family's rule does not let the other in.
	private static bool SameFamily(FirewallRule left, FirewallRule right) =>
		left.Target.EndsWith("(v6)", StringComparison.Ordinal) == right.Target.EndsWith("(v6)", StringComparison.Ordinal);

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

	[GeneratedRegex(@"^\[\s*(?<number>[0-9]{1,5})\]\s*(?<rule>\S.*)$", RegexOptions.CultureInvariant)]
	private static partial Regex NumberedRule();

	[GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.+-]{0,63}$", RegexOptions.CultureInvariant)]
	private static partial Regex FirewalldService();

	[GeneratedRegex(@"^[0-9]{1,5}(-[0-9]{1,5})?/(tcp|udp|sctp|dccp)$", RegexOptions.CultureInvariant)]
	private static partial Regex FirewalldPort();
}
