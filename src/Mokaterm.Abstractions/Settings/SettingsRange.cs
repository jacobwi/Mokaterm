namespace Mokaterm.Abstractions.Settings;

/// <summary>Clamping for the numbers in a settings section, where the file on disk can hold anything.</summary>
public static class SettingsRange
{
	/// <summary>
	/// <paramref name="value"/> inside the range, or <paramref name="fallback"/> when it is not a number at all:
	/// <see cref="Math.Clamp(double, double, double)"/> passes NaN straight through, and a hand-edited settings.json can
	/// hold one.
	/// </summary>
	public static double Clamp(double value, double min, double max, double fallback) =>
		double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
