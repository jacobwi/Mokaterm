using System.Globalization;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Maui.Platforms.Windows;

/// <summary>
/// Security keys through webauthn.dll, the only route to a FIDO device that works without administrator rights: since
/// Windows 10 1903 raw HID access to FIDO devices is reserved for elevated processes. Windows shows its own prompt,
/// parented to our window, and handles PINs, USB, NFC and Windows Hello itself.
/// </summary>
/// <remarks>
/// Both calls block until the user answers, so they run on a thread of their own and never on the UI thread or on
/// SSH.NET's. The client data is handed over as given: Windows hashes it and signs <c>authenticatorData || hash</c>,
/// which is what lets an SSH login pass the message itself and get OpenSSH's plain signature back.
/// </remarks>
internal sealed class WindowsSecurityKeyAuthenticator : ISecurityKeyAuthenticator
{
	private const int MaxResultBytes = 64 * 1024;
	private static readonly TimeSpan MinTimeout = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan MaxTimeout = TimeSpan.FromMinutes(10);

	private readonly ILogger<WindowsSecurityKeyAuthenticator> _logger;
	private readonly Lazy<uint> _apiVersion = new(ReadApiVersion);

	public WindowsSecurityKeyAuthenticator(ILogger<WindowsSecurityKeyAuthenticator> logger) => _logger = logger;

	private enum Operation
	{
		Sign,
		Create,
	}

	private delegate int NativeCall<T>(Guid cancellationId, out T? value)
		where T : class;

	public string Description => "Windows Hello and FIDO2 security keys";

	public bool IsAvailable => _apiVersion.Value >= 1;

	public async Task<SecurityKeyAssertion> GetAssertionAsync(SecurityKeyAssertionRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		EnsureAvailable();
		nint window = await WindowAsync();
		return await RunAsync<SecurityKeyAssertion>(
			(Guid cancellationId, out SecurityKeyAssertion? assertion) => GetAssertion(window, request, cancellationId, out assertion),
			Operation.Sign,
			null,
			cancellationToken);
	}

	public async Task<SecurityKeyCredential> MakeCredentialAsync(SecurityKeyCredentialRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		EnsureAvailable();
		if (request.Protection != SecurityKeyProtection.None && _apiVersion.Value < WebAuthnInterop.CredProtectApiVersion)
		{
			throw new SecurityKeyException(
				SecurityKeyFailure.NotSupported,
				"This version of Windows cannot protect a resident or verify-required key. Update Windows, or turn both options off.");
		}

		nint window = await WindowAsync();
		return await RunAsync<SecurityKeyCredential>(
			(Guid cancellationId, out SecurityKeyCredential? credential) => MakeCredential(window, request, cancellationId, out credential),
			Operation.Create,
			request.Algorithm,
			cancellationToken);
	}

	private static uint ReadApiVersion()
	{
		try
		{
			return WebAuthnNative.WebAuthNGetApiVersionNumber();
		}
		catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
		{
			return 0;
		}
	}

	private static uint Milliseconds(TimeSpan timeout) =>
		(uint)(timeout < MinTimeout ? MinTimeout : timeout > MaxTimeout ? MaxTimeout : timeout).TotalMilliseconds;

	private static unsafe byte[] Copy(nint pointer, uint length)
	{
		if (pointer == 0 || length is 0 or > MaxResultBytes)
		{
			throw new SecurityKeyException(SecurityKeyFailure.Failed, "Windows returned an empty or oversized answer from the security key.");
		}

		return new ReadOnlySpan<byte>((void*)pointer, (int)length).ToArray();
	}

	private static async Task<nint> WindowAsync()
	{
		try
		{
			// The prompt is modal to this window, which only its own thread can hand out.
			return await MainThread.InvokeOnMainThreadAsync(WindowHandles.Current);
		}
		catch (InvalidOperationException ex)
		{
			throw new SecurityKeyException(SecurityKeyFailure.Failed, "The window is not ready to show the security key prompt.", ex);
		}
	}

	private static unsafe int GetAssertion(nint window, SecurityKeyAssertionRequest request, Guid cancellationId, out SecurityKeyAssertion? assertion)
	{
		assertion = null;
		fixed (char* relyingPartyId = request.RelyingPartyId)
		fixed (char* credentialType = WebAuthnInterop.PublicKeyCredentialType)
		fixed (char* hashAlgorithm = WebAuthnInterop.HashAlgorithmSha256)
		fixed (byte* clientDataBytes = request.ClientData)
		fixed (byte* credentialId = request.CredentialId)
		{
			WebAuthnInterop.Credential credential = new()
			{
				Version = WebAuthnInterop.CredentialVersion,
				IdLength = (uint)request.CredentialId.Length,
				Id = (nint)credentialId,
				CredentialType = (nint)credentialType,
			};

			WebAuthnInterop.ClientData clientData = new()
			{
				Version = WebAuthnInterop.ClientDataVersion,
				JsonLength = (uint)request.ClientData.Length,
				Json = (nint)clientDataBytes,
				HashAlgorithmId = (nint)hashAlgorithm,
			};

			WebAuthnInterop.GetAssertionOptions options = new()
			{
				Version = WebAuthnInterop.GetAssertionOptionsVersion,
				TimeoutMilliseconds = Milliseconds(request.Timeout),
				Credentials = new WebAuthnInterop.CredentialList { Count = 1, Items = (nint)(&credential) },
				AuthenticatorAttachment = WebAuthnInterop.AttachmentAny,

				// Windows asks for the PIN by default; a key that was not made verify-required should only need a touch.
				UserVerificationRequirement = request.RequireUserVerification
					? WebAuthnInterop.UserVerificationRequired
					: WebAuthnInterop.UserVerificationDiscouraged,
				CancellationId = (nint)(&cancellationId),
			};

			nint result = 0;
			int hresult = WebAuthnNative.WebAuthNAuthenticatorGetAssertion(window, relyingPartyId, &clientData, &options, &result);
			if (hresult != 0)
			{
				return hresult;
			}

			try
			{
				WebAuthnInterop.Assertion* answer = (WebAuthnInterop.Assertion*)result;
				assertion = new SecurityKeyAssertion(
					Copy(answer->AuthenticatorData, answer->AuthenticatorDataLength),
					Copy(answer->Signature, answer->SignatureLength));
				return 0;
			}
			finally
			{
				WebAuthnNative.WebAuthNFreeAssertion(result);
			}
		}
	}

	private static unsafe int MakeCredential(nint window, SecurityKeyCredentialRequest request, Guid cancellationId, out SecurityKeyCredential? credential)
	{
		credential = null;
		fixed (char* relyingPartyId = request.RelyingPartyId)
		fixed (char* userName = request.UserName)
		fixed (char* credentialType = WebAuthnInterop.PublicKeyCredentialType)
		fixed (char* hashAlgorithm = WebAuthnInterop.HashAlgorithmSha256)
		fixed (char* credProtect = WebAuthnInterop.CredProtectExtension)
		fixed (byte* userId = request.UserId)
		fixed (byte* challenge = request.Challenge)
		{
			WebAuthnInterop.RpEntity relyingParty = new()
			{
				Version = WebAuthnInterop.RpEntityVersion,
				Id = (nint)relyingPartyId,
				Name = (nint)relyingPartyId,
			};

			WebAuthnInterop.UserEntity user = new()
			{
				Version = WebAuthnInterop.UserEntityVersion,
				IdLength = (uint)request.UserId.Length,
				Id = (nint)userId,
				Name = (nint)userName,
				DisplayName = (nint)userName,
			};

			WebAuthnInterop.CoseParameter parameter = new()
			{
				Version = WebAuthnInterop.CoseParameterVersion,
				CredentialType = (nint)credentialType,
				Algorithm = request.Algorithm == SecurityKeyAlgorithm.Ed25519 ? WebAuthnInterop.CoseEdDsa : WebAuthnInterop.CoseEs256,
			};

			WebAuthnInterop.CoseParameterList parameters = new() { Count = 1, Items = (nint)(&parameter) };
			WebAuthnInterop.ClientData clientData = new()
			{
				Version = WebAuthnInterop.ClientDataVersion,
				JsonLength = (uint)request.Challenge.Length,
				Json = (nint)challenge,
				HashAlgorithmId = (nint)hashAlgorithm,
			};

			WebAuthnInterop.CredProtectInput protection = new()
			{
				CredProtect = (uint)request.Protection,

				// Refuse a key that cannot protect the credential rather than make an unprotected one, as OpenSSH does.
				RequireCredProtect = 1,
			};

			WebAuthnInterop.Extension extension = new()
			{
				Identifier = (nint)credProtect,
				Length = (uint)sizeof(WebAuthnInterop.CredProtectInput),
				Value = (nint)(&protection),
			};

			WebAuthnInterop.MakeCredentialOptions options = new()
			{
				Version = WebAuthnInterop.MakeCredentialOptionsVersion,
				TimeoutMilliseconds = Milliseconds(request.Timeout),
				Extensions = request.Protection == SecurityKeyProtection.None
					? default
					: new WebAuthnInterop.ExtensionList { Count = 1, Items = (nint)(&extension) },

				// Windows Hello makes P-256 keys and no EdDSA ones, so an Ed25519 key can only come from a roaming key.
				AuthenticatorAttachment = request.Algorithm == SecurityKeyAlgorithm.Ed25519
					? WebAuthnInterop.AttachmentCrossPlatform
					: WebAuthnInterop.AttachmentAny,
				RequireResidentKey = request.ResidentKey ? 1 : 0,
				UserVerificationRequirement = request.RequireUserVerification
					? WebAuthnInterop.UserVerificationRequired
					: WebAuthnInterop.UserVerificationDiscouraged,
				AttestationConveyancePreference = WebAuthnInterop.AttestationNone,
				CancellationId = (nint)(&cancellationId),
			};

			nint result = 0;
			int hresult = WebAuthnNative.WebAuthNAuthenticatorMakeCredential(window, &relyingParty, &user, &parameters, &clientData, &options, &result);
			if (hresult != 0)
			{
				return hresult;
			}

			try
			{
				WebAuthnInterop.CredentialAttestation* attestation = (WebAuthnInterop.CredentialAttestation*)result;
				credential = new SecurityKeyCredential(
					Copy(attestation->AuthenticatorData, attestation->AuthenticatorDataLength),
					Copy(attestation->CredentialId, attestation->CredentialIdLength));
				return 0;
			}
			finally
			{
				WebAuthnNative.WebAuthNFreeCredentialAttestation(result);
			}
		}
	}

	private static unsafe int NewCancellationId(out Guid id)
	{
		id = Guid.Empty;
		fixed (Guid* pointer = &id)
		{
			return WebAuthnNative.WebAuthNGetCancellationId(pointer);
		}
	}

	private static unsafe void Cancel(Guid id) => _ = WebAuthnNative.WebAuthNCancelCurrentOperation(&id);

	private static unsafe string ErrorName(int hresult)
	{
		try
		{
			char* name = WebAuthnNative.WebAuthNGetErrorName(hresult);
			return name is null ? "UnknownError" : new string(name);
		}
		catch (EntryPointNotFoundException)
		{
			return "UnknownError";
		}
	}

	private void EnsureAvailable()
	{
		if (!IsAvailable)
		{
			throw new SecurityKeyException(SecurityKeyFailure.Unavailable, "Security keys need Windows 10 version 1903 or later.");
		}
	}

	private async Task<T> RunAsync<T>(NativeCall<T> call, Operation operation, SecurityKeyAlgorithm? algorithm, CancellationToken cancellationToken)
		where T : class
	{
		cancellationToken.ThrowIfCancellationRequested();
		int hresult = NewCancellationId(out Guid cancellationId);
		T? value = null;
		if (hresult == 0)
		{
			using CancellationTokenRegistration registration = cancellationToken.Register(() => Cancel(cancellationId));
			(hresult, value) = await Task.Factory.StartNew(
				() => (call(cancellationId, out T? answer), answer),
				CancellationToken.None,
				TaskCreationOptions.LongRunning,
				TaskScheduler.Default);
		}

		if (hresult == 0 && value is not null)
		{
			return value;
		}

		cancellationToken.ThrowIfCancellationRequested();
		SecurityKeyFailure failure = WebAuthnInterop.ToFailure(hresult);
		string name = ErrorName(hresult);
		_logger.LogWarning("WebAuthn {Operation} failed with {Name} (0x{HResult})", operation, name, hresult.ToString("X8", CultureInfo.InvariantCulture));
		throw new SecurityKeyException(failure, Describe(failure, operation, algorithm, hresult, name));
	}

	private static string Describe(SecurityKeyFailure failure, Operation operation, SecurityKeyAlgorithm? algorithm, int hresult, string name)
	{
		bool create = operation == Operation.Create;
		return failure switch
		{
			SecurityKeyFailure.Cancelled => create ? "The security key prompt was closed, so no key was made." : "The security key prompt was closed.",
			SecurityKeyFailure.TimedOut => create ? "The security key was not touched in time, so no key was made." : "The security key was not touched in time.",
			SecurityKeyFailure.NoCredential => create ? "No security key was found. Plug one in and try again." : "No security key that holds this credential was found.",
			SecurityKeyFailure.NotSupported when WebAuthnInterop.IsStorageFull(hresult) => "The security key has no room left for another resident key.",
			SecurityKeyFailure.NotSupported when create && algorithm == SecurityKeyAlgorithm.Ed25519 => "This security key cannot make Ed25519 keys. Make an ECDSA one instead.",
			SecurityKeyFailure.NotSupported => create ? "This authenticator cannot make this kind of key." : "The security key cannot sign with this credential.",
			SecurityKeyFailure.InvalidRequest => create ? "Windows refused to make the key." : "Windows refused the security key request.",
			SecurityKeyFailure.AlreadyRegistered => "This security key already holds that credential.",
			_ => string.Create(CultureInfo.InvariantCulture, $"Windows could not use the security key ({name}, 0x{hresult:X8})."),
		};
	}
}
