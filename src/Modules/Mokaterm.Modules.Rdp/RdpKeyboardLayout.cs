namespace Mokaterm.Modules.Rdp;

/// <summary>
/// A Windows keyboard layout identifier (the low word of a KLID) the session asks the server to load.
/// </summary>
/// <param name="Id">The layout id, for example 0x0409 for US English. Zero leaves the server's own choice alone.</param>
/// <param name="Name">What the connection editor shows.</param>
public sealed record RdpKeyboardLayout(int Id, string Name)
{
	/// <summary>Lets the server keep whatever layout the account is configured with.</summary>
	public const int ServerDefault = 0;

	/// <summary>Layouts the connection editor offers. Anything else can still be stored as a raw id.</summary>
	public static IReadOnlyList<RdpKeyboardLayout> Common { get; } =
	[
		new(ServerDefault, "Server default"),
		new(0x0409, "English (United States)"),
		new(0x0809, "English (United Kingdom)"),
		new(0x0407, "German (Germany)"),
		new(0x040C, "French (France)"),
		new(0x080C, "French (Belgium)"),
		new(0x0813, "Dutch (Belgium)"),
		new(0x0413, "Dutch (Netherlands)"),
		new(0x040A, "Spanish (Spain)"),
		new(0x0410, "Italian (Italy)"),
		new(0x0416, "Portuguese (Brazil)"),
		new(0x041D, "Swedish (Sweden)"),
		new(0x0414, "Norwegian (Norway)"),
		new(0x0406, "Danish (Denmark)"),
		new(0x040B, "Finnish (Finland)"),
		new(0x0405, "Czech (Czechia)"),
		new(0x0415, "Polish (Poland)"),
		new(0x0419, "Russian (Russia)"),
		new(0x0411, "Japanese (Japan)"),
		new(0x0412, "Korean (Korea)"),
		new(0x0804, "Chinese (Simplified)"),
		new(0x0404, "Chinese (Traditional)"),
	];

	/// <summary>The entry for <paramref name="id"/>, or one named after the raw id when it is not in the list.</summary>
	public static RdpKeyboardLayout For(int id) =>
		Common.FirstOrDefault(layout => layout.Id == id) ?? new RdpKeyboardLayout(id, $"Layout 0x{id:X4}");
}
