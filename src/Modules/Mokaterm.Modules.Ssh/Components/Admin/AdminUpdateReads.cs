using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Packages;
using Mokaterm.Modules.Ssh.Admin.Reboot;

namespace Mokaterm.Modules.Ssh.Components.Admin;

/// <summary>
/// The update facts more than one page shows (the overview's cards, the packages page, the security page): which
/// pending updates fix security problems, and whether the machine wants a reboot. Each answers in words when it cannot
/// say, so a card shows why instead of a blank.
/// </summary>
internal static class AdminUpdateReads
{
	/// <summary>The pending security updates, or null with the reason they are not known.</summary>
	public static async Task<(IReadOnlyList<PackageUpdate>? Updates, string? Note)> SecurityUpdatesAsync(
		SystemAdminFeature feature,
		PackageCommands? packages,
		string packagesMissing,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(feature);
		if (packages is null)
		{
			return (null, packagesMissing);
		}

		if (packages.ListSecurityUpdates() is not { } script)
		{
			return (null, $"{packages.DisplayName} keeps no list of which updates fix security problems.");
		}

		try
		{
			AdminOutcome outcome = await feature.ReadAsync(script, cancellationToken);
			return outcome.Succeeded ? (packages.ParseSecurityUpdates(outcome.Output), null) : (null, outcome.Problem());
		}
		catch (AdminException ex)
		{
			return (null, ex.Message);
		}
	}

	/// <summary>Whether a reboot is due; unknown, never an exception, when it cannot be read.</summary>
	public static async Task<RebootStatus> RebootAsync(SystemAdminFeature feature, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(feature);
		try
		{
			return RebootCommands.Parse((await feature.ReadAsync(RebootCommands.Status(), cancellationToken)).Output);
		}
		catch (AdminException)
		{
			return RebootStatus.Unknown;
		}
	}

	/// <summary>The reboot line's words, such as "reboot required by linux-image-generic, libc6".</summary>
	public static string RebootText(RebootStatus status)
	{
		ArgumentNullException.ThrowIfNull(status);
		return status.Required switch
		{
			true when status.Packages.Count > 0 => $"required by {string.Join(", ", status.Packages.Take(4))}{(status.Packages.Count > 4 ? ", ..." : "")}",
			true => $"required ({status.Source})",
			false => "not needed",
			_ => "nothing on the machine says",
		};
	}
}
