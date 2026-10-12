using System.Globalization;

namespace Mokaterm.Modules.Ssh.Admin.Storage;

/// <summary>One row of an ATA disk's SMART attribute table.</summary>
/// <param name="Value">The normalized value, usually counting down from 100 or 200 as the disk wears.</param>
/// <param name="Threshold">At or below this the disk itself calls the attribute failed; 0 means it never does.</param>
/// <param name="Raw">The vendor's raw counter as smartctl prints it, such as <c>0</c> or <c>34 (Min/Max 18/45)</c>.</param>
/// <param name="WhenFailed">Empty when it never failed, else <c>FAILING_NOW</c> or <c>In_the_past</c>.</param>
internal sealed record SmartAttribute(int Id, string Name, int Value, int Worst, int Threshold, string Raw, string WhenFailed, bool Prefailure)
{
	/// <summary>The counters of damaged or unreadable sectors: any of them above zero is a disk to replace soon.</summary>
	private static readonly HashSet<int> SectorCounters = [5, 187, 196, 197, 198];

	/// <summary>The disk itself says this attribute failed, now or in the past.</summary>
	public bool Failed => WhenFailed.Length > 0 || (Threshold > 0 && Value <= Threshold);

	/// <summary>A sector counter that is not zero, which the disk does not call a failure until it is far worse.</summary>
	public bool Worrying => !Failed && SectorCounters.Contains(Id) && RawNumber is > 0;

	/// <summary>The leading number of <see cref="Raw"/>, or null when it does not start with one.</summary>
	public long? RawNumber
	{
		get
		{
			int end = 0;
			while (end < Raw.Length && char.IsAsciiDigit(Raw[end]))
			{
				end++;
			}

			return end > 0 && long.TryParse(Raw.AsSpan(0, end), NumberStyles.None, CultureInfo.InvariantCulture, out long number) ? number : null;
		}
	}
}
