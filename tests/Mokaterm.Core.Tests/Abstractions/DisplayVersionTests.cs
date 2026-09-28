using System.Reflection;
using System.Reflection.Emit;
using Mokaterm.Abstractions.Platform;

namespace Mokaterm.Core.Tests.Abstractions;

public sealed class DisplayVersionTests
{
	[Theory]
	[InlineData("1.4.2+9f8e7d6c5b4a", "1.4.2")]
	[InlineData("1.4.2-beta.1+9f8e7d6c5b4a", "1.4.2-beta.1")]
	[InlineData("1.4.2-beta.1", "1.4.2-beta.1")]
	[InlineData("2.0.0", "2.0.0")]
	public void Of_DropsTheBuildMetadataTheSdkAppends(string informational, string expected) =>
		Assert.Equal(expected, DisplayVersion.Of(Assembly(informational, new Version(9, 9, 9, 9))));

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("+9f8e7d6c5b4a")]
	public void Of_WithoutAUsableInformationalVersion_UsesTheAssemblyVersion(string? informational) =>
		Assert.Equal("3.1.4", DisplayVersion.Of(Assembly(informational, new Version(3, 1, 4, 1))));

	[Fact]
	public void Of_AnAssemblyVersionWithTwoParts_StillReadsAsThree() =>
		Assert.Equal("3.1.0", DisplayVersion.Of(Assembly(null, new Version(3, 1))));

	private static AssemblyBuilder Assembly(string? informational, Version version)
	{
		CustomAttributeBuilder[] attributes = informational is null
			? []
			: [new CustomAttributeBuilder(typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!, [informational])];
		return AssemblyBuilder.DefineDynamicAssembly(
			new AssemblyName("DisplayVersionProbe" + Guid.NewGuid().ToString("N")) { Version = version },
			AssemblyBuilderAccess.Run,
			attributes);
	}
}
