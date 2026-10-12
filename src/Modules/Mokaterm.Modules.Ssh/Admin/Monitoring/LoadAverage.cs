using System.Globalization;

namespace Mokaterm.Modules.Ssh.Admin.Monitoring;

/// <summary>The run queue averaged over one, five and fifteen minutes, from <c>/proc/loadavg</c>.</summary>
internal sealed record LoadAverage(double One, double Five, double Fifteen)
{
	/// <summary><c>0.42 0.31 0.27 2/318 48211</c>; null when the first three are not numbers.</summary>
	public static LoadAverage? Parse(string? text)
	{
		string[] words = (AdminSections.Lines(text).FirstOrDefault() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
		return words.Length >= 3
			&& double.TryParse(words[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double one)
			&& double.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double five)
			&& double.TryParse(words[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double fifteen)
				? new LoadAverage(one, five, fifteen)
				: null;
	}
}
