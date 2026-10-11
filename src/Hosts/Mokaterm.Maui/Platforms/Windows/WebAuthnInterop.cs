using System.Runtime.InteropServices;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Maui.Platforms.Windows;

/// <summary>
/// The structures and constants of Windows' <c>webauthn.h</c> that security key logins use, at the oldest structure
/// version that carries the fields we fill: the platform reads only as far as the version says. Plain data with no
/// Windows call in it, so <c>Mokaterm.UI.Tests</c> compiles it too and checks the layout against the C header.
/// </summary>
internal static class WebAuthnInterop
{
	public const uint ClientDataVersion = 1;
	public const uint CredentialVersion = 1;
	public const uint RpEntityVersion = 1;
	public const uint UserEntityVersion = 1;
	public const uint CoseParameterVersion = 1;

	/// <summary>Version 3 adds the cancellation id, and every webauthn.dll since API version 1 reads up to version 4.</summary>
	public const uint GetAssertionOptionsVersion = 3;

	/// <summary>Version 2 adds the cancellation id, and every webauthn.dll since API version 1 reads up to version 3.</summary>
	public const uint MakeCredentialOptionsVersion = 2;

	/// <summary>The credProtect extension arrived with API version 2.</summary>
	public const uint CredProtectApiVersion = 2;

	public const string PublicKeyCredentialType = "public-key";
	public const string HashAlgorithmSha256 = "SHA-256";
	public const string CredProtectExtension = "credProtect";

	public const int CoseEs256 = -7;
	public const int CoseEdDsa = -8;

	public const uint AttachmentAny = 0;
	public const uint AttachmentCrossPlatform = 2;

	public const uint UserVerificationRequired = 1;
	public const uint UserVerificationDiscouraged = 3;

	/// <summary>No attestation: OpenSSH keeps none by default, and asking for it makes Windows ask the user to share it.</summary>
	public const uint AttestationNone = 1;

	private const int NteExists = unchecked((int)0x8009000F);
	private const int NteNotFound = unchecked((int)0x80090011);
	private const int NteTokenKeysetStorageFull = unchecked((int)0x80090023);
	private const int NteInvalidParameter = unchecked((int)0x80090027);
	private const int NteNotSupported = unchecked((int)0x80090029);
	private const int NteDeviceNotFound = unchecked((int)0x80090035);
	private const int NteUserCancelled = unchecked((int)0x80090036);
	private const int ErrorNotSupported = unchecked((int)0x80070032);
	private const int InvalidArgument = unchecked((int)0x80070057);
	private const int ErrorCancelled = unchecked((int)0x800704C7);
	private const int ErrorTimeout = unchecked((int)0x800705B4);

	/// <summary>The failure an HRESULT stands for, grouped the way <c>WebAuthNGetErrorName</c> groups them.</summary>
	public static SecurityKeyFailure ToFailure(int hresult) => hresult switch
	{
		NteUserCancelled or ErrorCancelled => SecurityKeyFailure.Cancelled,
		ErrorTimeout => SecurityKeyFailure.TimedOut,
		NteNotFound or NteDeviceNotFound => SecurityKeyFailure.NoCredential,
		NteNotSupported or ErrorNotSupported or NteTokenKeysetStorageFull => SecurityKeyFailure.NotSupported,
		NteInvalidParameter or InvalidArgument => SecurityKeyFailure.InvalidRequest,
		NteExists => SecurityKeyFailure.AlreadyRegistered,
		_ => SecurityKeyFailure.Failed,
	};

	/// <summary>True for the one HRESULT that means the device has no room for another resident credential.</summary>
	public static bool IsStorageFull(int hresult) => hresult == NteTokenKeysetStorageFull;

	/// <summary><c>WEBAUTHN_CLIENT_DATA</c>.</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct ClientData
	{
		public uint Version;
		public uint JsonLength;
		public nint Json;
		public nint HashAlgorithmId;
	}

	/// <summary><c>WEBAUTHN_CREDENTIAL</c>.</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct Credential
	{
		public uint Version;
		public uint IdLength;
		public nint Id;
		public nint CredentialType;
	}

	/// <summary><c>WEBAUTHN_CREDENTIALS</c>.</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct CredentialList
	{
		public uint Count;
		public nint Items;
	}

	/// <summary><c>WEBAUTHN_EXTENSION</c>.</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct Extension
	{
		public nint Identifier;
		public uint Length;
		public nint Value;
	}

	/// <summary><c>WEBAUTHN_EXTENSIONS</c>.</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct ExtensionList
	{
		public uint Count;
		public nint Items;
	}

	/// <summary><c>WEBAUTHN_CRED_PROTECT_EXTENSION_IN</c>.</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct CredProtectInput
	{
		public uint CredProtect;
		public int RequireCredProtect;
	}

	/// <summary><c>WEBAUTHN_RP_ENTITY_INFORMATION</c>.</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct RpEntity
	{
		public uint Version;
		public nint Id;
		public nint Name;
		public nint Icon;
	}

	/// <summary><c>WEBAUTHN_USER_ENTITY_INFORMATION</c>.</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct UserEntity
	{
		public uint Version;
		public uint IdLength;
		public nint Id;
		public nint Name;
		public nint Icon;
		public nint DisplayName;
	}

	/// <summary><c>WEBAUTHN_COSE_CREDENTIAL_PARAMETER</c>.</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct CoseParameter
	{
		public uint Version;
		public nint CredentialType;
		public int Algorithm;
	}

	/// <summary><c>WEBAUTHN_COSE_CREDENTIAL_PARAMETERS</c>.</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct CoseParameterList
	{
		public uint Count;
		public nint Items;
	}

	/// <summary><c>WEBAUTHN_AUTHENTICATOR_GET_ASSERTION_OPTIONS</c> up to version 3.</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct GetAssertionOptions
	{
		public uint Version;
		public uint TimeoutMilliseconds;
		public CredentialList Credentials;
		public ExtensionList Extensions;
		public uint AuthenticatorAttachment;
		public uint UserVerificationRequirement;
		public uint Flags;
		public nint U2fAppId;
		public nint U2fAppIdUsed;
		public nint CancellationId;
	}

	/// <summary><c>WEBAUTHN_AUTHENTICATOR_MAKE_CREDENTIAL_OPTIONS</c> up to version 2.</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct MakeCredentialOptions
	{
		public uint Version;
		public uint TimeoutMilliseconds;
		public CredentialList ExcludeCredentials;
		public ExtensionList Extensions;
		public uint AuthenticatorAttachment;
		public int RequireResidentKey;
		public uint UserVerificationRequirement;
		public uint AttestationConveyancePreference;
		public uint Flags;
		public nint CancellationId;
	}

	/// <summary>The version 1 head of <c>WEBAUTHN_ASSERTION</c>, which is all this reads of a newer one.</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct Assertion
	{
		public uint Version;
		public uint AuthenticatorDataLength;
		public nint AuthenticatorData;
		public uint SignatureLength;
		public nint Signature;
		public Credential Credential;
		public uint UserIdLength;
		public nint UserId;
	}

	/// <summary>The version 1 head of <c>WEBAUTHN_CREDENTIAL_ATTESTATION</c>.</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct CredentialAttestation
	{
		public uint Version;
		public nint FormatType;
		public uint AuthenticatorDataLength;
		public nint AuthenticatorData;
		public uint AttestationLength;
		public nint Attestation;
		public uint AttestationDecodeType;
		public nint AttestationDecode;
		public uint AttestationObjectLength;
		public nint AttestationObject;
		public uint CredentialIdLength;
		public nint CredentialId;
	}
}
