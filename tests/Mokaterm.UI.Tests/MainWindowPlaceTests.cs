using Mokaterm.Maui.Services;
using Xunit;

namespace Mokaterm.UI.Tests;

public sealed class MainWindowPlaceTests
{
	[Theory]
	[InlineData(100, 200, 1400, 900, false)]
	[InlineData(-1920, -40, 1920, 1080, true)]
	public void WhatIsWritten_ReadsBackTheSame(int x, int y, int width, int height, bool maximized)
	{
		MainWindowPlace place = new(x, y, width, height, maximized);
		Assert.Equal(place, MainWindowPlace.Read(place.Write()));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("100,200,1400,900")]
	[InlineData("100,200,1400,900,2")]
	[InlineData("100,200,0,900,0")]
	[InlineData("100,200,1400,-5,0")]
	[InlineData("100,200,99999,900,0")]
	[InlineData("a,200,1400,900,0")]
	[InlineData("100,200,1400,900,0,7")]
	public void AnythingElse_IsNoPlace(string? text) => Assert.Null(MainWindowPlace.Read(text));
}
