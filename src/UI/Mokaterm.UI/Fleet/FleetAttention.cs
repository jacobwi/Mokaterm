using System.Globalization;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Fleet;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.UI.Fleet;

/// <summary>
/// What about a machine is worth a look, in words. The limits are the ones <see cref="UsageBar"/> colours by, so a row
/// listed here is also a row with a red bar in it.
/// </summary>
internal static class FleetAttention
{
	/// <summary>Every reason, most pressing first; empty for a machine that is fine or was never read.</summary>
	public static IReadOnlyList<string> Reasons(FleetRow row)
	{
		ArgumentNullException.ThrowIfNull(row);
		List<string> reasons = [];
		if (row.Result is { State: HostRunState.Failed or HostRunState.Skipped } result)
		{
			reasons.Add(result.Message ?? (result.State == HostRunState.Failed ? "The last read failed." : "The last read was skipped."));
		}

		if (row.Summary is not { } summary)
		{
			return reasons;
		}

		if (summary.FailedServices is > 0 and var failed)
		{
			reasons.Add(failed == 1 ? "1 failed service" : string.Create(CultureInfo.CurrentCulture, $"{failed} failed services"));
		}

		if (summary.RebootRequired == true)
		{
			reasons.Add("Reboot required");
		}

		if (summary.FullestDisk is { } disk && disk.UsedPercent >= UsageBar.FullPercent)
		{
			reasons.Add(string.Create(CultureInfo.CurrentCulture, $"{disk.MountPoint} is {disk.UsedPercent}% full"));
		}

		if (summary.MemoryPercent is { } memory && memory >= UsageBar.FullPercent)
		{
			reasons.Add(string.Create(CultureInfo.CurrentCulture, $"Memory {memory:0}% used"));
		}

		if (summary.LoadPercent is { } load && load >= 100)
		{
			reasons.Add(string.Create(CultureInfo.CurrentCulture, $"Load {summary.Load1:0.00} on {summary.CpuCount} CPUs"));
		}

		if (summary.SecurityUpdates is > 0 and var security)
		{
			reasons.Add(security == 1 ? "1 security update" : string.Create(CultureInfo.CurrentCulture, $"{security} security updates"));
		}

		return reasons;
	}
}
