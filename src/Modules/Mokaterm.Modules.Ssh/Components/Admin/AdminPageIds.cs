namespace Mokaterm.Modules.Ssh.Components.Admin;

/// <summary>
/// The ids the System view's own pages register under. Pages open each other by these, and a plugin page that names
/// one opens it the same way; changing one breaks both.
/// </summary>
internal static class AdminPageIds
{
	public const string Overview = "overview";

	public const string Services = "services";

	public const string Packages = "packages";

	public const string Cron = "cron";

	public const string Time = "time";

	public const string HostName = "hostname";

	public const string Network = "network";

	public const string Users = "users";

	public const string Disks = "disks";

	public const string Processes = "processes";

	public const string Logs = "logs";

	public const string Firewall = "firewall";

	public const string Security = "security";
}
