using Mokaterm.Abstractions.Security;
using Renci.SshNet.Common;

namespace Mokaterm.Modules.Ssh.Connection;

/// <summary>A host key the user is still being asked about.</summary>
internal sealed record PendingHostKey(Task<bool> Verification, string Fingerprint);

/// <summary>
/// Answers SSH.NET's host key question for one connection attempt. SSH.NET asks on its message loop and needs the answer
/// before the key exchange can go on, so the verifier gets a short window: known hosts answer at once. A verification still
/// running after that is a user prompt; the key is refused for now and the connector reconnects once the user trusted
/// that exact key.
/// </summary>
internal sealed class HostKeyCheck
{
	private static readonly TimeSpan DecisionWindow = TimeSpan.FromSeconds(2);

	private readonly string _host;
	private readonly int _port;
	private readonly IHostIdentityVerifier? _verifier;
	private readonly IProgress<string>? _status;
	private readonly CancellationToken _cancellationToken;
	private string? _trustedFingerprint;
	private bool _pinned;

	/// <param name="verifier">Null for companion connections, which accept nothing but <paramref name="trustedFingerprint"/>.</param>
	/// <param name="trustedFingerprint">A key the user already approved during this connect.</param>
	public HostKeyCheck(string host, int port, IHostIdentityVerifier? verifier, string? trustedFingerprint, IProgress<string>? status, CancellationToken cancellationToken)
	{
		_host = host;
		_port = port;
		_verifier = verifier;
		_trustedFingerprint = trustedFingerprint;
		_status = status;
		_cancellationToken = cancellationToken;
	}

	/// <summary>The fingerprint of the key this connection trusted.</summary>
	public string? AcceptedFingerprint { get; private set; }

	/// <summary>A verification still waiting for the user, with the fingerprint it is about.</summary>
	public PendingHostKey? Pending { get; private set; }

	/// <summary>The verifier said no.</summary>
	public bool Rejected { get; private set; }

	/// <summary>A key other than the pinned one was presented.</summary>
	public bool KeyChanged { get; private set; }

	/// <summary>Verification itself failed, for example because the known hosts could not be read.</summary>
	public Exception? Error { get; private set; }

	/// <summary>Once connected, trusts only the accepted key, so a re-key cannot switch to another one.</summary>
	public void Pin()
	{
		_trustedFingerprint = AcceptedFingerprint;
		_pinned = true;
	}

	public void OnHostKeyReceived(object? sender, HostKeyEventArgs e)
	{
		ArgumentNullException.ThrowIfNull(e);
		e.CanTrust = Decide(e);
	}

	private bool Decide(HostKeyEventArgs e)
	{
		HostIdentity identity;
		try
		{
			identity = SshHostKeys.CreateIdentity(_host, _port, e);
		}
		catch (Exception ex)
		{
			Error = ex;
			return false;
		}

		if (_trustedFingerprint is not null && string.Equals(identity.Fingerprint, _trustedFingerprint, StringComparison.Ordinal))
		{
			return Accept(identity);
		}

		if (_pinned || _verifier is null)
		{
			KeyChanged = true;
			return false;
		}

		_status?.Report("Verifying host key");
		Task<bool> verification;
		try
		{
			verification = _verifier.VerifyAsync(identity, _cancellationToken).AsTask();
		}
		catch (Exception ex)
		{
			Error = ex;
			return false;
		}

		// Blocking here is deliberate and bounded: this handler runs on SSH.NET's own thread, which waits for the answer.
		if (!verification.IsCompleted && !((IAsyncResult)verification).AsyncWaitHandle.WaitOne(DecisionWindow))
		{
			Pending = new PendingHostKey(verification, identity.Fingerprint);
			return false;
		}

		if (!verification.IsCompletedSuccessfully)
		{
			Error = verification.Exception?.GetBaseException() ?? new OperationCanceledException(_cancellationToken);
			return false;
		}

		if (verification.GetAwaiter().GetResult())
		{
			return Accept(identity);
		}

		Rejected = true;
		return false;
	}

	private bool Accept(HostIdentity identity)
	{
		AcceptedFingerprint = identity.Fingerprint;
		if (!_pinned)
		{
			_status?.Report("Authenticating");
		}

		return true;
	}
}
