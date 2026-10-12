using Mokaterm.Plugins.Sample.Subnets;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.Plugins.Sample;

/// <summary>The sample's settings page, built from the same shared fields as Mokaterm's own pages.</summary>
public sealed partial class SampleSettingsPage : SettingsSectionBase<SampleSettings>
{
	private Task SaveAsync(Func<SampleSettings, SampleSettings> change) => UpdateAsync(settings => change(settings).Clamped());

	// The calculator's own reading, so the field refuses exactly what the calculator could not show.
	private static string? ValidateSubnet(string value) =>
		value.Trim().Length > SampleSettings.MaxSubnetStartLength
			? $"Keep it under {SampleSettings.MaxSubnetStartLength} characters."
			: SubnetMath.TryCalculate(value, out _, out string? problem) ? null : problem;
}
