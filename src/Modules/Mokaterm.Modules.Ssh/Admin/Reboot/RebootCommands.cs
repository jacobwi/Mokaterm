using System.Globalization;
using Mokaterm.Modules.Ssh.Admin.Detection;

namespace Mokaterm.Modules.Ssh.Admin.Reboot;

/// <summary>
/// Whether the machine wants a reboot, and the reboot itself. Debian and Ubuntu flag it in
/// <c>/var/run/reboot-required</c> (with the packages that asked in <c>.pkgs</c>), RHEL-likes answer
/// <c>needs-restarting -r</c>, and anywhere a kernel update removed the running kernel's modules folder it is due too.
/// </summary>
internal static class RebootCommands
{
	private const string FlagFile = "/var/run/reboot-required";

	/// <summary>Reads without root: the flag files are world readable and needs-restarting reads the package database.</summary>
	public static AdminScript Status() => new()
	{
		Name = "reboot.status",
		Body = string.Join('\n',
			$"if [ -f {FlagFile} ]; then",
			$"\t{AdminSections.Start("flag")}",
			$"\tcat {FlagFile} 2>/dev/null",
			$"\tif [ -f {FlagFile}.pkgs ]; then",
			$"\t\t{AdminSections.Start("packages")}",
			$"\t\tsort -u {FlagFile}.pkgs 2>/dev/null | head -n 50",
			"\tfi",
			"fi",
			"if command -v needs-restarting >/dev/null 2>&1; then",
			$"\t{AdminSections.Start("needs-restarting")}",
			"\tneeds-restarting -r 2>&1",
			"\tcode=$?",
			$"\t{AdminSections.Start("needs-restarting-exit")}",
			"\techo \"$code\"",
			"fi",

			// The hooks that write the flag file; without one its absence says nothing.
			$"if [ -f /etc/kernel/postinst.d/update-notifier ] || [ -f /etc/kernel/postinst.d/unattended-upgrades ]; then {AdminSections.Start("flag-watch")}; fi",
			"kernel=$(uname -r)",
			$"if [ -n \"$(ls -A /lib/modules 2>/dev/null)\" ] && [ ! -d \"/lib/modules/$kernel\" ] && [ ! -d \"/usr/lib/modules/$kernel\" ]; then {AdminSections.Start("modules-gone")}; fi",
			"exit 0"),
	};

	public static RebootStatus Parse(string output)
	{
		AdminSections sections = AdminSections.Parse(output);
		if (sections.Has("flag"))
		{
			return new RebootStatus
			{
				Required = true,
				Packages = [.. AdminSections.Lines(sections.Get("packages")).Select(line => line.Trim())],
				Source = FlagFile,
			};
		}

		if (sections.Get("needs-restarting") is { } text)
		{
			int? exit = int.TryParse(AdminSections.Lines(sections.Get("needs-restarting-exit")).FirstOrDefault()?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int code) ? code : null;
			bool required = text.Contains("Reboot is required", StringComparison.Ordinal);
			bool notNeeded = text.Contains("should not be necessary", StringComparison.Ordinal);
			if (required || notNeeded)
			{
				return new RebootStatus
				{
					Required = required || (!notNeeded && exit == 1),
					Packages = [.. AdminSections.Lines(text).Select(line => line.Trim()).Where(line => line.StartsWith("* ", StringComparison.Ordinal)).Select(line => line[2..].Trim())],
					Source = "needs-restarting",
				};
			}
		}

		if (sections.Has("modules-gone"))
		{
			return new RebootStatus { Required = true, Source = "the running kernel's modules are gone" };
		}

		return sections.Has("flag-watch") ? new RebootStatus { Required = false, Source = FlagFile } : RebootStatus.Unknown;
	}

	/// <summary>Whether the machine can be rebooted from here: something has to run as PID 1 to do it.</summary>
	public static bool CanReboot(SystemProfile profile)
	{
		ArgumentNullException.ThrowIfNull(profile);
		return profile.Platform == PlatformKind.Linux && profile.Init != InitSystem.None;
	}

	/// <summary>
	/// Reboots now, the init system's own way. The command returns at once and the connection goes a moment later, so
	/// the result usually comes back before the session drops.
	/// </summary>
	public static AdminAction Reboot(SystemProfile profile)
	{
		if (!CanReboot(profile))
		{
			throw new InvalidOperationException("No init system runs here, so there is nothing to reboot.");
		}

		string name = profile.Facts.HostName.Length > 0 ? profile.Facts.HostName : "the machine";
		return new AdminAction
		{
			Title = $"Reboot {name}",
			Script = new AdminScript
			{
				Name = "reboot.now",
				Body = profile.Init switch
				{
					InitSystem.Systemd => "systemctl reboot",
					InitSystem.SysV => "shutdown -r now",
					_ => "reboot",
				},
				AsRoot = true,
				Timeout = AdminScript.ActionTimeout,
				OutputLimitBytes = AdminScript.ActionOutputBytes,
			},
			Description = "Restarts the machine now. Once the session drops, it is reconnected when the server answers SSH again, for up to ten minutes.",
			Warning = "Every service stops and every connection to the machine ends, this session included. A machine that does not come back up needs a console.",
			CutsConnection = true,
			Destructive = true,
			ConfirmText = "Reboot",
		};
	}
}
