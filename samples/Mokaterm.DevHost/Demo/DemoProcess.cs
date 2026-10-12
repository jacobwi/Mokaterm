namespace Mokaterm.DevHost.Demo;

/// <summary>One process on the demo machine, with how busy it is as a share of one CPU that swings over time.</summary>
/// <param name="Busy">The average share of one CPU, 0 to 1.</param>
/// <param name="Swing">How far the share swings either side of <paramref name="Busy"/>; never more than it.</param>
/// <param name="Period">Seconds per radian of the swing.</param>
internal sealed record DemoProcess(int Pid, int Parent, string User, int Nice, double Busy, double Swing, double Period, double Memory, long Rss, string State, string Command);
