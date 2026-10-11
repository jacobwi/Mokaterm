using System.Globalization;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The default gestures. They are read from the defaults rather than the bindings in force, which are process wide and
/// which another test may have changed.
/// </summary>
public sealed class ShellShortcutsTests
{
	[Fact]
	public void DefaultBindings_EveryGestureIsAllowedAndBelongsToOneCommand()
	{
		IReadOnlyList<ShellShortcutBinding> defaults = ShellShortcuts.DefaultBindings;

		Assert.All(defaults, binding => Assert.True(ShellShortcuts.IsAllowed(new ShortcutGesture(binding.Code, binding.Shift)), binding.Id));
		Assert.Equal(defaults.Count, defaults.Select(binding => (binding.Code, binding.Shift)).Distinct().Count());
		Assert.Equal(defaults.Count, defaults.Select(binding => binding.Id).Distinct().Count());
	}

	[Fact]
	public void DefaultBindings_ADigitFocusesThatPaneAndShiftWithItMovesTheTabThere()
	{
		for (int pane = 0; pane < SessionWorkspace.MaxPanes; pane++)
		{
			string digit = "Digit" + (pane + 1).ToString(CultureInfo.InvariantCulture);
			ShellShortcutBinding focus = Assert.Single(ShellShortcuts.DefaultBindings, binding => binding.Id == ShellCommandIds.FocusPane[pane]);
			ShellShortcutBinding move = Assert.Single(ShellShortcuts.DefaultBindings, binding => binding.Id == ShellCommandIds.MoveToPane[pane]);

			Assert.Equal((digit, false), (focus.Code, focus.Shift));
			Assert.Equal((digit, true), (move.Code, move.Shift));
		}
	}
}
