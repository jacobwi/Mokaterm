using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Moka.Red.Core.Enums;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// The character set a connection, or a protocol's defaults, names. Every protocol that carries one shows this field:
/// an empty box means <see cref="Default"/>, so clearing it leaves the box empty instead of filling it with the name
/// that was just saved, and a name no provider knows stays in the box with its error rather than being saved.
/// </summary>
public sealed partial class CharacterSetSetting : DraftSettingBase<string>
{
	/// <summary>The character set an empty box stands for. Also the placeholder unless one is given.</summary>
	[Parameter, EditorRequired]
	public string Default { get; set; } = "";

	/// <summary>Resolves a name, such as <c>TerminalEncodings.TryGet</c>.</summary>
	[Parameter, EditorRequired]
	public TryGetEncoding TryGet { get; set; } = default!;

	/// <summary>Text under the box. A row inside a settings page leaves it empty and carries the description itself.</summary>
	[Parameter]
	public string? HelperText { get; set; }

	[Parameter]
	public MokaSize Size { get; set; } = MokaSize.Md;

	/// <summary>
	/// What the box holds and what it refuses. Internal for the tests: a static render delivers no keystroke, so the
	/// rule about keeping what is being typed can only be exercised by driving the field itself.
	/// </summary>
	internal (string Text, string? Error) Box => (Text, Error);

	/// <summary>Types into the box, for the tests.</summary>
	internal Task TypeAsync(string? text) => OnTextChangedAsync(text);

	/// <summary>Hands the field its parameters again, for the tests, the way a re-render does.</summary>
	internal void Reapply() => OnParametersSet();

	protected override bool TryParse(string text, [MaybeNullWhen(false)] out string value, [NotNullWhen(false)] out string? error)
	{
		string trimmed = text.Trim();
		if (trimmed.Length == 0)
		{
			value = Default;
			error = null;
			return true;
		}

		if (!TryGet(trimmed, out _))
		{
			value = null;
			error = "Unknown character set.";
			return false;
		}

		value = trimmed;
		error = null;
		return true;
	}

	protected override string Format(string value) => value;
}
