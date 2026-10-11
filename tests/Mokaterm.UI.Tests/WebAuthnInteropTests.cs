using System.Runtime.InteropServices;
using Mokaterm.Abstractions.Security;
using Mokaterm.Maui.Platforms.Windows;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The desktop host's webauthn.dll structures against the offsets the C compiler gives <c>webauthn.h</c> on a 64-bit
/// Windows. The platform reads these by pointer, so a field moved by one slot would send it garbage that no other
/// test could catch without a security key.
/// </summary>
public sealed class WebAuthnInteropTests
{
	[Fact]
	public void Structures_MatchTheHeadersLayout()
	{
		Assert.SkipUnless(nint.Size == 8, "The offsets below are the 64-bit layout, which is what the desktop app ships.");

		Assert.Equal(24, Marshal.SizeOf<WebAuthnInterop.ClientData>());
		Assert.Equal(8, Offset<WebAuthnInterop.ClientData>(nameof(WebAuthnInterop.ClientData.Json)));
		Assert.Equal(16, Offset<WebAuthnInterop.ClientData>(nameof(WebAuthnInterop.ClientData.HashAlgorithmId)));

		Assert.Equal(24, Marshal.SizeOf<WebAuthnInterop.Credential>());
		Assert.Equal(16, Marshal.SizeOf<WebAuthnInterop.CredentialList>());
		Assert.Equal(24, Marshal.SizeOf<WebAuthnInterop.Extension>());
		Assert.Equal(16, Offset<WebAuthnInterop.Extension>(nameof(WebAuthnInterop.Extension.Value)));
		Assert.Equal(8, Marshal.SizeOf<WebAuthnInterop.CredProtectInput>());
		Assert.Equal(32, Marshal.SizeOf<WebAuthnInterop.RpEntity>());
		Assert.Equal(40, Marshal.SizeOf<WebAuthnInterop.UserEntity>());
		Assert.Equal(32, Offset<WebAuthnInterop.UserEntity>(nameof(WebAuthnInterop.UserEntity.DisplayName)));
		Assert.Equal(24, Marshal.SizeOf<WebAuthnInterop.CoseParameter>());
		Assert.Equal(16, Offset<WebAuthnInterop.CoseParameter>(nameof(WebAuthnInterop.CoseParameter.Algorithm)));

		// WEBAUTHN_AUTHENTICATOR_GET_ASSERTION_OPTIONS as far as version 3.
		Assert.Equal(8, Offset<WebAuthnInterop.GetAssertionOptions>(nameof(WebAuthnInterop.GetAssertionOptions.Credentials)));
		Assert.Equal(24, Offset<WebAuthnInterop.GetAssertionOptions>(nameof(WebAuthnInterop.GetAssertionOptions.Extensions)));
		Assert.Equal(40, Offset<WebAuthnInterop.GetAssertionOptions>(nameof(WebAuthnInterop.GetAssertionOptions.AuthenticatorAttachment)));
		Assert.Equal(44, Offset<WebAuthnInterop.GetAssertionOptions>(nameof(WebAuthnInterop.GetAssertionOptions.UserVerificationRequirement)));
		Assert.Equal(56, Offset<WebAuthnInterop.GetAssertionOptions>(nameof(WebAuthnInterop.GetAssertionOptions.U2fAppId)));
		Assert.Equal(72, Offset<WebAuthnInterop.GetAssertionOptions>(nameof(WebAuthnInterop.GetAssertionOptions.CancellationId)));
		Assert.Equal(80, Marshal.SizeOf<WebAuthnInterop.GetAssertionOptions>());

		// WEBAUTHN_AUTHENTICATOR_MAKE_CREDENTIAL_OPTIONS as far as version 2.
		Assert.Equal(44, Offset<WebAuthnInterop.MakeCredentialOptions>(nameof(WebAuthnInterop.MakeCredentialOptions.RequireResidentKey)));
		Assert.Equal(52, Offset<WebAuthnInterop.MakeCredentialOptions>(nameof(WebAuthnInterop.MakeCredentialOptions.AttestationConveyancePreference)));
		Assert.Equal(64, Offset<WebAuthnInterop.MakeCredentialOptions>(nameof(WebAuthnInterop.MakeCredentialOptions.CancellationId)));
		Assert.Equal(72, Marshal.SizeOf<WebAuthnInterop.MakeCredentialOptions>());

		// The version 1 heads of the two results, read through the pointer the platform hands back.
		Assert.Equal(8, Offset<WebAuthnInterop.Assertion>(nameof(WebAuthnInterop.Assertion.AuthenticatorData)));
		Assert.Equal(24, Offset<WebAuthnInterop.Assertion>(nameof(WebAuthnInterop.Assertion.Signature)));
		Assert.Equal(32, Offset<WebAuthnInterop.Assertion>(nameof(WebAuthnInterop.Assertion.Credential)));
		Assert.Equal(24, Offset<WebAuthnInterop.CredentialAttestation>(nameof(WebAuthnInterop.CredentialAttestation.AuthenticatorData)));
		Assert.Equal(80, Offset<WebAuthnInterop.CredentialAttestation>(nameof(WebAuthnInterop.CredentialAttestation.CredentialIdLength)));
		Assert.Equal(88, Offset<WebAuthnInterop.CredentialAttestation>(nameof(WebAuthnInterop.CredentialAttestation.CredentialId)));
	}

	[Theory]
	[InlineData(unchecked((int)0x80090036), SecurityKeyFailure.Cancelled)]
	[InlineData(unchecked((int)0x800704C7), SecurityKeyFailure.Cancelled)]
	[InlineData(unchecked((int)0x800705B4), SecurityKeyFailure.TimedOut)]
	[InlineData(unchecked((int)0x80090011), SecurityKeyFailure.NoCredential)]
	[InlineData(unchecked((int)0x80090035), SecurityKeyFailure.NoCredential)]
	[InlineData(unchecked((int)0x80090029), SecurityKeyFailure.NotSupported)]
	[InlineData(unchecked((int)0x80070032), SecurityKeyFailure.NotSupported)]
	[InlineData(unchecked((int)0x80090023), SecurityKeyFailure.NotSupported)]
	[InlineData(unchecked((int)0x80090027), SecurityKeyFailure.InvalidRequest)]
	[InlineData(unchecked((int)0x8009000F), SecurityKeyFailure.AlreadyRegistered)]
	[InlineData(unchecked((int)0x80004005), SecurityKeyFailure.Failed)]
	public void ToFailure_GroupsHresultsAsWebAuthNGetErrorNameDoes(int hresult, SecurityKeyFailure expected) =>
		Assert.Equal(expected, WebAuthnInterop.ToFailure(hresult));

	private static int Offset<T>(string field) => (int)Marshal.OffsetOf<T>(field);
}
