namespace Mokaterm.UI.Common.Components;

/// <summary>How full something is, as <see cref="UsageBar"/> colours it.</summary>
public enum UsageLevel
{
	Normal,

	/// <summary>From <see cref="UsageBar.WarnPercent"/>: worth a look.</summary>
	Warn,

	/// <summary>From <see cref="UsageBar.FullPercent"/>: about to run out.</summary>
	Full,
}
