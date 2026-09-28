using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The one character-set field, which four places used to carry a copy of. What is in the box is not simply the saved
/// name: an empty box means the default, so it has to survive that default being saved back into it, and a name nothing
/// resolves stays where it was typed.
/// </summary>
public sealed class CharacterSetSettingTests
{
	[Fact]
	public async Task ClearingTheBox_SavesTheDefaultAndLeavesTheBoxEmpty()
	{
		Field field = new("ibm437");

		Assert.Equal("ibm437", field.Setting.Box.Text);

		await field.Setting.TypeAsync("");

		// Empty means the default, which is what gets stored, while the box stays as the user left it.
		Assert.Equal(TerminalEncodings.Default, field.Saved);
		Assert.Equal("", field.Setting.Box.Text);
		Assert.Null(field.Setting.Box.Error);

		field.Reapply();

		Assert.Equal("", field.Setting.Box.Text);
	}

	[Fact]
	public async Task AHalfTypedName_StaysInTheBoxWithItsErrorAndSavesNothing()
	{
		Field field = new("utf-8");

		await field.Setting.TypeAsync("ibm43");

		Assert.Equal("ibm43", field.Setting.Box.Text);
		Assert.Equal("Unknown character set.", field.Setting.Box.Error);
		Assert.Equal("utf-8", field.Saved);

		// A re-render while the name is half typed must not put the saved one back over it.
		field.Reapply();

		Assert.Equal("ibm43", field.Setting.Box.Text);
		Assert.Equal("Unknown character set.", field.Setting.Box.Error);

		await field.Setting.TypeAsync("ibm437");

		Assert.Null(field.Setting.Box.Error);
		Assert.Equal("ibm437", field.Saved);
	}

	[Fact]
	public async Task AKnownName_IsSavedTrimmedWhileTheBoxKeepsWhatWasTyped()
	{
		Field field = new("utf-8");

		await field.Setting.TypeAsync("  IBM437  ");

		Assert.Null(field.Setting.Box.Error);
		Assert.Equal("IBM437", field.Saved);

		// The spaces are not saved, and a re-render does not take them out from under the cursor either.
		field.Reapply();

		Assert.Equal("  IBM437  ", field.Setting.Box.Text);
	}

	[Fact]
	public async Task ASavedNameFromElsewhere_ReplacesWhatTheBoxShows()
	{
		Field field = new("utf-8");
		await field.Setting.TypeAsync("");

		// A reset to defaults, or another window saving, names a different character set: the box follows it.
		field.Saved = "windows-1252";
		field.Reapply();

		Assert.Equal("windows-1252", field.Setting.Box.Text);
	}

	/// <summary>One field with the parent that stores what it reports, driven the way a render would.</summary>
	private sealed class Field
	{
		public Field(string saved)
		{
			Saved = saved;
			Reapply();
		}

		public CharacterSetSetting Setting { get; } = new();

		public string Saved { get; set; }

		public void Reapply()
		{
			ParameterView.FromDictionary(new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				[nameof(CharacterSetSetting.Value)] = Saved,
				[nameof(CharacterSetSetting.Default)] = TerminalEncodings.Default,
				[nameof(CharacterSetSetting.TryGet)] = (TryGetEncoding)TerminalEncodings.TryGet,
				[nameof(CharacterSetSetting.ValueChanged)] = new EventCallback<string>(null, (Action<string>)(value => Saved = value)),
			}).SetParameterProperties(Setting);

			Setting.Reapply();
		}
	}
}
