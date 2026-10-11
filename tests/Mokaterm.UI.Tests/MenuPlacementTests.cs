using Microsoft.AspNetCore.Components.Web;
using Mokaterm.UI.Common.Interaction;

namespace Mokaterm.UI.Tests;

public sealed class MenuPlacementTests
{
	[Fact]
	public void EnterOnAButton_HasNoPointer_SoTheMenuGoesUnderTheFocusedElement()
	{
		// Chromium reports a click the keyboard caused at (0, 0) with no click count.
		MouseEventArgs keyboard = new() { ClientX = 0, ClientY = 0, Detail = 0 };

		Assert.False(MenuPlacement.HasPointer(keyboard));
	}

	[Theory]
	[InlineData(1200, 88)]
	[InlineData(0, 40)]
	[InlineData(40, 0)]
	public void AClickAnywhereElse_KeepsThePointer(double x, double y)
	{
		MouseEventArgs click = new() { ClientX = x, ClientY = y, Detail = 1 };

		Assert.True(MenuPlacement.HasPointer(click));
	}
}
