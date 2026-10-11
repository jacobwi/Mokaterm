using System.Runtime.InteropServices;

namespace Mokaterm.Maui.Platforms.Windows;

/// <summary>
/// The webauthn.dll exports security key logins use. Windows 10 1903 added the library; on an older Windows the first
/// call throws <see cref="DllNotFoundException"/>, which <see cref="WindowsSecurityKeyAuthenticator"/> turns into
/// "unavailable".
/// </summary>
internal static unsafe partial class WebAuthnNative
{
	private const string Library = "webauthn.dll";

	[LibraryImport(Library)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	public static partial uint WebAuthNGetApiVersionNumber();

	[LibraryImport(Library)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	public static partial int WebAuthNAuthenticatorGetAssertion(
		nint window,
		char* relyingPartyId,
		WebAuthnInterop.ClientData* clientData,
		WebAuthnInterop.GetAssertionOptions* options,
		nint* assertion);

	[LibraryImport(Library)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	public static partial int WebAuthNAuthenticatorMakeCredential(
		nint window,
		WebAuthnInterop.RpEntity* relyingParty,
		WebAuthnInterop.UserEntity* user,
		WebAuthnInterop.CoseParameterList* parameters,
		WebAuthnInterop.ClientData* clientData,
		WebAuthnInterop.MakeCredentialOptions* options,
		nint* attestation);

	[LibraryImport(Library)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	public static partial void WebAuthNFreeAssertion(nint assertion);

	[LibraryImport(Library)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	public static partial void WebAuthNFreeCredentialAttestation(nint attestation);

	[LibraryImport(Library)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	public static partial int WebAuthNGetCancellationId(Guid* cancellationId);

	[LibraryImport(Library)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	public static partial int WebAuthNCancelCurrentOperation(Guid* cancellationId);

	/// <summary>A static string owned by the library, such as <c>NotAllowedError</c>; never freed.</summary>
	[LibraryImport(Library)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	public static partial char* WebAuthNGetErrorName(int hresult);
}
