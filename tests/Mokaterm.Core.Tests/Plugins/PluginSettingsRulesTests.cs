using Mokaterm.Abstractions.Settings;
using Mokaterm.Core.Plugins;

namespace Mokaterm.Core.Tests.Plugins;

public sealed class PluginSettingsRulesTests
{
	private static readonly Dictionary<string, Type> Nothing = new(StringComparer.Ordinal);

	[Theory]
	[InlineData("plugin.moka.sample", true)]
	[InlineData("plugin.moka.sample.fail2ban", true)]
	[InlineData("plugin.moka.samples", false)]
	[InlineData("plugin.moka", false)]
	[InlineData("ssh", false)]
	[InlineData("terminal", false)]
	[InlineData("Plugin.moka.sample", false)]
	public void Belongs_OnlyTheNamesUnderThePluginsOwnId(string name, bool belongs)
	{
		Assert.Equal(belongs, PluginSettingsRules.Belongs("moka.sample", name));
	}

	[Fact]
	public void Problem_NoneForOwnKeysAndPages()
	{
		Assert.Null(PluginSettingsRules.Problem(
			"moka.sample",
			[new SettingsSectionType("plugin.moka.sample", typeof(OwnSettings))],
			["plugin.moka.sample"],
			Nothing));
	}

	[Fact]
	public void Problem_ABuiltInKey_IsRefused()
	{
		string? problem = PluginSettingsRules.Problem("moka.sample", [new SettingsSectionType("ssh", typeof(OwnSettings))], [], Nothing);

		Assert.Contains("\"ssh\"", problem, StringComparison.Ordinal);
		Assert.Contains("plugin.moka.sample", problem, StringComparison.Ordinal);
	}

	[Fact]
	public void Problem_ABuiltInPageId_IsRefused()
	{
		Assert.NotNull(PluginSettingsRules.Problem("moka.sample", [], ["terminal"], Nothing));
	}

	[Fact]
	public void Problem_AKeyAnotherTypeHolds_IsRefused_ButTheSameSectionTwiceIsOneSection()
	{
		Dictionary<string, Type> inUse = new(StringComparer.Ordinal) { ["plugin.moka.sample"] = typeof(OtherSettings) };

		Assert.NotNull(PluginSettingsRules.Problem("moka.sample", [new SettingsSectionType("plugin.moka.sample", typeof(OwnSettings))], [], inUse));
		Assert.Null(PluginSettingsRules.Problem("moka.sample", [new SettingsSectionType("plugin.moka.sample", typeof(OtherSettings))], [], inUse));
	}

	private sealed record OwnSettings : ISettingsSection
	{
		public static string SectionKey => "plugin.moka.sample";
	}

	private sealed record OtherSettings : ISettingsSection
	{
		public static string SectionKey => "plugin.moka.sample";
	}
}
