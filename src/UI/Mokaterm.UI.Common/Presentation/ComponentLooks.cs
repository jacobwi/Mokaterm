using Moka.Red.Core.Theming;
using Mokaterm.Abstractions.Settings;

namespace Mokaterm.UI.Common.Presentation;

/// <summary>
/// The one mapping between the stored component styles in <see cref="AppearanceSettings"/> and Moka.Red's looks. Each
/// stored value carries its look's own name, so a value Moka does not know falls back to its standard look.
/// </summary>
public static class ComponentLooks
{
	/// <summary>The toast look for <paramref name="style"/>.</summary>
	public static MokaToastLook Of(ToastStyle style) => Convert(style, MokaToastLook.Standard);

	/// <summary>The toast stacking for <paramref name="stack"/>.</summary>
	public static MokaToastStacking Of(ToastStack stack) => Convert(stack, MokaToastStacking.List);

	/// <summary>The popup look for <paramref name="style"/>.</summary>
	public static MokaPopupLook Of(PopupStyle style) => Convert(style, MokaPopupLook.Standard);

	/// <summary>The list look for <paramref name="style"/>.</summary>
	public static MokaListLook Of(ListStyle style) => Convert(style, MokaListLook.Standard);

	/// <summary>The button look for <paramref name="style"/>.</summary>
	public static MokaButtonLook Of(ButtonStyle style) => Convert(style, MokaButtonLook.Standard);

	/// <summary>The stored style for a toast look.</summary>
	public static ToastStyle From(MokaToastLook look) => Convert(look, ToastStyle.Standard);

	/// <summary>The stored stacking for a toast stacking.</summary>
	public static ToastStack From(MokaToastStacking stacking) => Convert(stacking, ToastStack.List);

	/// <summary>The stored style for a popup look.</summary>
	public static PopupStyle From(MokaPopupLook look) => Convert(look, PopupStyle.Standard);

	/// <summary>The stored style for a list look.</summary>
	public static ListStyle From(MokaListLook look) => Convert(look, ListStyle.Standard);

	/// <summary>The stored style for a button look.</summary>
	public static ButtonStyle From(MokaButtonLook look) => Convert(look, ButtonStyle.Standard);

	private static TTo Convert<TFrom, TTo>(TFrom value, TTo fallback)
		where TFrom : struct, Enum
		where TTo : struct, Enum =>
		Enum.IsDefined(value) && Enum.TryParse(value.ToString(), out TTo converted) ? converted : fallback;
}
