using Mokaterm.Modules.Ssh.Admin.Detection;

namespace Mokaterm.Modules.Ssh.Admin.Security;

/// <summary>
/// Changing sshd's settings. A setting goes into the security page's own drop-in when the main file includes the
/// drop-in folder before setting it; the old drop-in is kept until <c>sshd -t</c> accepts the new one and
/// <c>sshd -T</c> shows the value took, and is put back otherwise. Anything else is edited by hand, and checked with
/// <c>sshd -t</c> before sshd reloads. A reload leaves open sessions as they are; the next login gets the new settings.
/// </summary>
internal static class SshdCommands
{
	private const string UnreloadedWarning =
		"If sshd -t refuses the files, sshd is not reloaded and keeps the settings it has, but it would not start with these: fix the file before sshd restarts.";

	/// <summary>
	/// Sets <paramref name="fix"/> in <see cref="SshdConfig.DropInFile"/> and reloads sshd, or puts the old drop-in
	/// back when sshd refuses the result or another file still wins.
	/// </summary>
	public static AdminAction SetInDropIn(SshdFix fix, InitSystem init)
	{
		ArgumentNullException.ThrowIfNull(fix);
		RequireWord(fix.Keyword, nameof(fix));
		RequireWord(fix.Value, nameof(fix));
		if (fix.Accepts.Count == 0 || fix.Accepts.Any(value => !IsWord(value)))
		{
			throw new ArgumentException("The values sshd may print back are plain words.", nameof(fix));
		}

		string setting = $"{fix.Keyword} {fix.Value}";
		string check = $"^{fix.Keyword.ToLowerInvariant()} ({string.Join('|', fix.Accepts)})$";
		return new AdminAction
		{
			Title = $"Set {setting}",
			Script = Root("sshd.set", string.Join('\n',
				"set -e",
				$"file={SshdConfig.DropInFile}",
				$"mkdir -p {SshdConfig.DropInFolder}",
				$"new=$(mktemp {SshdConfig.DropInFolder}/.mokaterm.XXXXXX)",
				"trap 'rm -f \"$new\"' EXIT",
				"if [ -f \"$file\" ]; then",
				$"\tgrep -v -i -E '^[[:space:]]*{fix.Keyword}([[:space:]]|=)' \"$file\" > \"$new\" || true",
				"else",
				"\techo '# Settings changed from the Mokaterm system view.' > \"$new\"",
				"fi",
				$"echo '{setting}' >> \"$new\"",
				"chmod 644 \"$new\"",
				"if [ -f \"$file\" ]; then cp -p \"$file\" \"$file.mokaterm-old\"; fi",
				"mv -f \"$new\" \"$file\"",
				$"if ! sshd -t || ! sshd -T | grep -q -i -E '{check}'; then",
				"\tif [ -f \"$file.mokaterm-old\" ]; then mv -f \"$file.mokaterm-old\" \"$file\"; else rm -f \"$file\"; fi",
				"\techo 'sshd did not take the new setting, so the drop-in was put back as it was.' >&2",
				"\texit 1",
				"fi",
				"rm -f \"$file.mokaterm-old\"",
				Reload(init))),
			Description = $"Writes {setting} into {SshdConfig.DropInFile}, which sshd reads before anything that sets it now, checks the result with sshd -t and sshd -T, and reloads sshd. If sshd refuses it, the drop-in is put back as it was. Open sessions stay as they are; the next login gets the new setting.",
			Warning = fix.Warning,
			CutsConnection = fix.CutsConnection,
			Destructive = fix.CutsConnection,
			ConfirmText = fix.CutsConnection ? "Change anyway" : "Change",
		};
	}

	/// <summary>
	/// After a file was edited by hand: <c>sshd -t</c>, and the reload only when it passes, so a broken file never
	/// reaches the running server.
	/// </summary>
	/// <param name="fix">The change the edit was for, which says whether the new settings refuse this session's next login.</param>
	public static AdminAction CheckAndReload(InitSystem init, string file, SshdFix? fix)
	{
		ArgumentNullException.ThrowIfNull(file);
		return new AdminAction
		{
			Title = "Check and reload the SSH server",
			Script = Root("sshd.reload", string.Join('\n', "set -e", "sshd -t", Reload(init))),
			Description = $"Checks every sshd file with sshd -t after the edit to {file}, then reloads sshd so the next login gets the new settings. Open sessions stay as they are.",
			Warning = fix?.Warning is { } warning ? warning + " " + UnreloadedWarning : UnreloadedWarning,
			CutsConnection = fix?.CutsConnection == true,
			Destructive = fix?.CutsConnection == true,
			ConfirmText = "Check and reload",
		};
	}

	/// <summary>
	/// Has sshd read its files again, the init system's way. A reload keeps open sessions; with systemd the unit is
	/// <c>ssh</c> on Debian and Ubuntu and <c>sshd</c> elsewhere, and an inactive socket-activated one needs nothing.
	/// </summary>
	public static string Reload(InitSystem init) => init switch
	{
		InitSystem.Systemd => "if systemctl cat ssh.service >/dev/null 2>&1; then unit=ssh.service; else unit=sshd.service; fi\nsystemctl try-reload-or-restart \"$unit\"",
		InitSystem.OpenRc => "rc-service sshd reload",
		InitSystem.SysV => "if [ -x /etc/init.d/ssh ]; then /etc/init.d/ssh reload; else /etc/init.d/sshd reload; fi",
		_ => "kill -HUP \"$(cat /var/run/sshd.pid)\"",
	};

	private static AdminScript Root(string name, string body) => new()
	{
		Name = name,
		Body = body,
		AsRoot = true,
		Timeout = AdminScript.ActionTimeout,
		OutputLimitBytes = AdminScript.ActionOutputBytes,
	};

	private static bool IsWord(string value) => value.Length is > 0 and <= 64 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

	private static void RequireWord(string value, string parameter)
	{
		if (!IsWord(value))
		{
			throw new ArgumentException("sshd keywords and the values set here are plain words.", parameter);
		}
	}
}
