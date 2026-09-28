using System.Globalization;

namespace Mokaterm.DevHost.Demo;

/// <summary>Timestamps the way <c>ls</c>, syslog and a login banner print them: English names and a space-padded day.</summary>
internal static class DemoDates
{
	/// <summary><c>Sep 17 12:04:31</c></summary>
	public static string Syslog(DateTimeOffset time) => MonthDay(time) + time.ToString(" HH:mm:ss", CultureInfo.InvariantCulture);

	/// <summary><c>Sep  7 09:12</c> for recent times and <c>Mar  2  2026</c> for older ones, both the same width.</summary>
	public static string Listing(DateTimeOffset time, DateTimeOffset now)
	{
		bool recent = time > now.AddDays(-182) && time < now.AddHours(1);
		return MonthDay(time) + time.ToString(recent ? " HH:mm" : "  yyyy", CultureInfo.InvariantCulture);
	}

	/// <summary><c>Tue Sep 16 18:42:11 2026</c></summary>
	public static string Login(DateTimeOffset time) =>
		time.ToString("ddd ", CultureInfo.InvariantCulture) + MonthDay(time) + time.ToString(" HH:mm:ss yyyy", CultureInfo.InvariantCulture);

	private static string MonthDay(DateTimeOffset time) =>
		time.ToString("MMM ", CultureInfo.InvariantCulture) + time.Day.ToString(CultureInfo.InvariantCulture).PadLeft(2);
}
