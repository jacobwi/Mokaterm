namespace Mokaterm.Abstractions.Presentation;

/// <summary>Which spellings of a hex color a stored value or a field takes.</summary>
[Flags]
public enum HexColorForms
{
	None = 0,

	/// <summary><c>#rrggbb</c>, which every color slot takes.</summary>
	Opaque = 1,

	/// <summary><c>#rgb</c>, which <see cref="HexColor.Normalize"/> expands to <see cref="Opaque"/> before it is stored.</summary>
	Compact = 2,

	/// <summary><c>#rrggbbaa</c>, for a color drawn over whatever is behind it.</summary>
	Alpha = 4,

	/// <summary>An accent or a host color: <c>#rgb</c> or <c>#rrggbb</c>, and never transparent.</summary>
	Accent = Opaque | Compact,

	/// <summary>A terminal theme slot that blends, which is the only place alpha belongs.</summary>
	ThemeSlot = Opaque | Alpha,
}
