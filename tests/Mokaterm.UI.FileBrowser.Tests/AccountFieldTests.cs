using Mokaterm.UI.FileBrowser.Properties;

namespace Mokaterm.UI.FileBrowser.Tests;

public sealed class AccountFieldTests
{
	[Fact]
	public void SharedName_StartsInTheFieldAndIsNoChange()
	{
		AccountField field = new(["www-data", "www-data"]);

		Assert.False(field.IsMixed);
		Assert.Equal("www-data", field.Initial);
		Assert.Equal("www-data", field.Text);
		Assert.Null(field.Changed);
		Assert.Null(field.ToApply(includeUnchanged: false));
		Assert.Equal("www-data", field.ToApply(includeUnchanged: true));
	}

	[Fact]
	public void DifferentNames_StartEmptyAndKeepEachOne()
	{
		AccountField field = new(["root", "abc"]);

		Assert.True(field.IsMixed);
		Assert.Null(field.Initial);
		Assert.Equal("", field.Text);
		Assert.Null(field.ToApply(includeUnchanged: true));

		field.Text = " root ";
		Assert.Equal("root", field.Changed);
	}

	[Theory]
	[InlineData("www data")]
	[InlineData("abc:staff")]
	[InlineData("a/b")]
	public void InvalidNames_AreNeverApplied(string text)
	{
		AccountField field = new(["abc"]) { Text = text };

		Assert.False(field.IsValid);
		Assert.Null(field.Changed);
		Assert.Null(field.ToApply(includeUnchanged: true));
	}

	[Fact]
	public void ClearedField_KeepsTheName()
	{
		AccountField field = new(["abc"]) { Text = "   " };

		Assert.True(field.IsValid);
		Assert.True(field.IsEmpty);
		Assert.Null(field.ToApply(includeUnchanged: true));
	}
}
