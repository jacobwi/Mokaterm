using Mokaterm.Modules.Vnc.Protocol;

namespace Mokaterm.Modules.Vnc.Tests;

public sealed class VncSecuritySelectionTests
{
	[Fact]
	public void ChooseTopLevel_PrefersVeNCrypt_BecauseItIsTheOnlyWayToTls()
	{
		VncSecurityType chosen = VncSecuritySelection.ChooseTopLevel([1, 2, 19], VncEncryptionMode.Preferred);

		Assert.Equal(VncSecurityType.VeNCrypt, chosen);
	}

	[Fact]
	public void ChooseTopLevel_PrefersAPasswordOverNoAuthentication()
	{
		VncSecurityType chosen = VncSecuritySelection.ChooseTopLevel([1, 2], VncEncryptionMode.Preferred);

		Assert.Equal(VncSecurityType.VncAuth, chosen);
	}

	[Fact]
	public void ChooseTopLevel_None_WhenItIsAllThereIs()
	{
		VncSecurityType chosen = VncSecuritySelection.ChooseTopLevel([1], VncEncryptionMode.Preferred);

		Assert.Equal(VncSecurityType.None, chosen);
	}

	[Fact]
	public void ChooseTopLevel_SkipsTypesTheModuleCannotUse()
	{
		VncSecurityType chosen = VncSecuritySelection.ChooseTopLevel([5, 16, 30, 113], VncEncryptionMode.Preferred);

		Assert.Equal(VncSecurityType.Invalid, chosen);
	}

	[Fact]
	public void ChooseTopLevel_WithRequiredEncryption_RefusesWithoutVeNCrypt()
	{
		VncSecurityType chosen = VncSecuritySelection.ChooseTopLevel([1, 2], VncEncryptionMode.Required);

		Assert.Equal(VncSecurityType.Invalid, chosen);
	}

	[Fact]
	public void ChooseSubtype_PrefersACertificateOverAnonymousTls()
	{
		VncSecurityType chosen = VncSecuritySelection.ChooseSubtype(
			[(int)VncSecurityType.TlsVnc, (int)VncSecurityType.X509Vnc],
			VncEncryptionMode.Preferred,
			hasUsername: false);

		Assert.Equal(VncSecurityType.X509Vnc, chosen);
	}

	[Fact]
	public void ChooseSubtype_PrefersTlsOverPlainText()
	{
		VncSecurityType chosen = VncSecuritySelection.ChooseSubtype(
			[(int)VncSecurityType.VncAuth, (int)VncSecurityType.TlsNone],
			VncEncryptionMode.Preferred,
			hasUsername: false);

		Assert.Equal(VncSecurityType.TlsNone, chosen);
	}

	[Fact]
	public void ChooseSubtype_WithoutUsername_PrefersThePasswordChallenge()
	{
		VncSecurityType chosen = VncSecuritySelection.ChooseSubtype(
			[(int)VncSecurityType.X509Plain, (int)VncSecurityType.X509Vnc],
			VncEncryptionMode.Preferred,
			hasUsername: false);

		Assert.Equal(VncSecurityType.X509Vnc, chosen);
	}

	[Fact]
	public void ChooseSubtype_WithUsername_PrefersPlain()
	{
		VncSecurityType chosen = VncSecuritySelection.ChooseSubtype(
			[(int)VncSecurityType.X509Vnc, (int)VncSecurityType.X509Plain],
			VncEncryptionMode.Preferred,
			hasUsername: true);

		Assert.Equal(VncSecurityType.X509Plain, chosen);
	}

	[Fact]
	public void ChooseSubtype_WithRequiredEncryption_SkipsTheUnencryptedSubtypes()
	{
		VncSecurityType chosen = VncSecuritySelection.ChooseSubtype(
			[(int)VncSecurityType.Plain, (int)VncSecurityType.VncAuth],
			VncEncryptionMode.Required,
			hasUsername: true);

		Assert.Equal(VncSecurityType.Invalid, chosen);
	}

	[Fact]
	public void ChooseSubtype_IgnoresVeNCryptItself()
	{
		VncSecurityType chosen = VncSecuritySelection.ChooseSubtype(
			[(int)VncSecurityType.VeNCrypt],
			VncEncryptionMode.Preferred,
			hasUsername: false);

		Assert.Equal(VncSecurityType.Invalid, chosen);
	}

	[Theory]
	[InlineData(1, false, false, false)]
	[InlineData(2, false, false, true)]
	[InlineData(258, true, false, true)]
	[InlineData(262, true, true, false)]
	public void SecurityTypes_DescribeWhatEachTypeNeeds(int number, bool encrypted, bool certificate, bool password)
	{
		VncSecurityType type = (VncSecurityType)number;

		Assert.Equal(encrypted, VncSecurityTypes.IsEncrypted(type));
		Assert.Equal(certificate, VncSecurityTypes.UsesCertificate(type));
		Assert.Equal(password, VncSecurityTypes.NeedsPassword(type));
		Assert.False(string.IsNullOrWhiteSpace(VncSecurityTypes.Describe(type)));
	}
}
