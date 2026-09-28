using Moka.Red.Core.Icons;

namespace Mokaterm.UI.Common.Components;

/// <summary>One choice in an <see cref="EnumSegmentedControl{TEnum}"/>.</summary>
public sealed record EnumOption<TEnum>(TEnum Value, string Text, MokaIconDefinition? Icon = null)
	where TEnum : struct, Enum;
