using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using Microsoft.AspNetCore.Components;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// A number typed into a text field. Saved only while it parses and lies within <see cref="Min"/> and <see cref="Max"/>;
/// MokaNumericField neither enforces its range nor keeps half-typed input, so this parses the text itself.
/// </summary>
public sealed partial class NumberSetting<TValue> : DraftSettingBase<TValue>
	where TValue : struct, INumber<TValue>
{
	private const string DefaultWidthStyle = "width:88px";

	private static readonly bool IsWholeNumber = typeof(TValue).GetInterfaces()
		.Any(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IBinaryInteger<>));

	[Parameter, EditorRequired]
	public TValue Min { get; set; }

	[Parameter, EditorRequired]
	public TValue Max { get; set; }

	/// <summary>Unit shown after the field, such as <c>s</c> or <c>KB</c>.</summary>
	[Parameter]
	public string? Suffix { get; set; }

	private static string InputMode => IsWholeNumber ? "numeric" : "decimal";

	protected override bool TryParse(string text, [MaybeNullWhen(false)] out TValue value, [NotNullWhen(false)] out string? error)
	{
		if (TValue.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out value) && value >= Min && value <= Max)
		{
			error = null;
			return true;
		}

		error = IsWholeNumber
			? string.Create(CultureInfo.CurrentCulture, $"Enter a whole number from {Min} to {Max}.")
			: string.Create(CultureInfo.CurrentCulture, $"Enter a number from {Min} to {Max}.");
		return false;
	}

	protected override string Format(TValue value) => value.ToString(null, CultureInfo.CurrentCulture);
}
