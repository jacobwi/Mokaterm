using Moka.Red.Core.Theming;
using Mokaterm.Abstractions.Settings;

namespace Mokaterm.UI.Common.Presentation;

/// <summary>The one mapping between the stored <see cref="StatusBarStyle"/> and Moka.Red's <see cref="MokaStatusBarLook"/>.</summary>
public static class StatusBarLooks
{
	/// <summary>The look that draws <paramref name="style"/>.</summary>
	public static MokaStatusBarLook Of(StatusBarStyle style) => style switch
	{
		StatusBarStyle.Accent => MokaStatusBarLook.Accent,
		StatusBarStyle.Hairline => MokaStatusBarLook.Hairline,
		StatusBarStyle.Floating => MokaStatusBarLook.Floating,
		StatusBarStyle.Chips => MokaStatusBarLook.Chips,
		StatusBarStyle.Segments => MokaStatusBarLook.Segments,
		StatusBarStyle.Terminal => MokaStatusBarLook.Terminal,
		_ => MokaStatusBarLook.Standard,
	};

	/// <summary>The style to store for <paramref name="look"/>.</summary>
	public static StatusBarStyle From(MokaStatusBarLook look) => look switch
	{
		MokaStatusBarLook.Accent => StatusBarStyle.Accent,
		MokaStatusBarLook.Hairline => StatusBarStyle.Hairline,
		MokaStatusBarLook.Floating => StatusBarStyle.Floating,
		MokaStatusBarLook.Chips => StatusBarStyle.Chips,
		MokaStatusBarLook.Segments => StatusBarStyle.Segments,
		MokaStatusBarLook.Terminal => StatusBarStyle.Terminal,
		_ => StatusBarStyle.Standard,
	};
}
