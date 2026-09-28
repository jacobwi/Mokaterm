using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentFTP;
using FluentFTP.Client.BaseClient;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Modules.Ftp.Connection;

/// <summary>
/// Decides which FTPS certificates a session accepts, for its control and data connections alike. Approved fingerprints
/// are accepted at once; anything else goes to <see cref="IHostIdentityVerifier"/>. A verdict that takes longer than
/// the handshake can wait for (the user is being asked) refuses that handshake, and the connect loop reconnects once
/// the answer is in.
/// </summary>
internal sealed class FtpCertificateTrust : IDisposable
{
	public static readonly TimeSpan DefaultDecisionWait = TimeSpan.FromSeconds(2);

	private readonly IHostIdentityVerifier _verifier;
	private readonly string _host;
	private readonly int _port;
	private readonly TimeSpan _decisionWait;
	private readonly ILogger _logger;
	private readonly CancellationTokenSource _lifetime = new();
	private readonly Lock _gate = new();
	private readonly HashSet<string> _approved = new(StringComparer.Ordinal);
	private readonly Dictionary<string, FtpCertificateDecision> _decisions = new(StringComparer.Ordinal);
	private FtpCertificateDecision? _lastRefused;
	private string? _approvedFingerprint;
	private int _refusals;
	private int _disposed;

	public FtpCertificateTrust(IHostIdentityVerifier verifier, string host, int port, ILogger logger, TimeSpan? decisionWait = null)
	{
		_verifier = verifier;
		_host = host;
		_port = port;
		_logger = logger;
		_decisionWait = decisionWait ?? DefaultDecisionWait;
	}

	/// <summary>The most recently accepted certificate fingerprint, or null while none was presented.</summary>
	public string? ApprovedFingerprint
	{
		get
		{
			lock (_gate)
			{
				return _approvedFingerprint;
			}
		}
	}

	/// <summary>Counts refused handshakes, so a failed connect can tell whether a certificate was the reason.</summary>
	public int RefusalCount => Volatile.Read(ref _refusals);

	/// <summary>The decision behind the most recent refusal.</summary>
	public FtpCertificateDecision? LastRefused
	{
		get
		{
			lock (_gate)
			{
				return _lastRefused;
			}
		}
	}

	/// <summary>Accepts a fingerprint from now on, typically after the user approved it while a handshake was refused.</summary>
	public void Approve(string fingerprint)
	{
		lock (_gate)
		{
			_approved.Add(fingerprint);
			_approvedFingerprint = fingerprint;
		}
	}

	/// <summary>Handler for FluentFTP's certificate event. FluentFTP pre-sets Accept from the policy errors, so always overwrite it.</summary>
	public void OnValidateCertificate(BaseFtpClient control, FtpSslValidationEventArgs e)
	{
		ArgumentNullException.ThrowIfNull(e);
		e.Accept = Validate(e.Certificate, e.PolicyErrors);
	}

	public bool Validate(X509Certificate? certificate, SslPolicyErrors policyErrors)
	{
		if (certificate is null || Volatile.Read(ref _disposed) != 0)
		{
			return false;
		}

		HostIdentity identity;
		try
		{
			identity = TlsHostIdentity.Create(_host, _port, certificate, policyErrors);
		}
		catch (CryptographicException ex)
		{
			_logger.LogWarning("Could not read the FTPS server certificate: {Error}", LogSafe.Describe(ex));
			return Refuse(null);
		}

		FtpCertificateDecision decision;
		lock (_gate)
		{
			if (_approved.Contains(identity.Fingerprint))
			{
				return true;
			}

			if (!_decisions.TryGetValue(identity.Fingerprint, out FtpCertificateDecision? existing))
			{
				existing = new FtpCertificateDecision(identity, VerifyAsync(identity));
				_decisions[identity.Fingerprint] = existing;
			}

			decision = existing;
		}

		if (!decision.Wait(_decisionWait) || !decision.IsApproved)
		{
			return Refuse(decision);
		}

		Approve(identity.Fingerprint);
		return true;
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		// Cancels any prompt still open for this session.
		_lifetime.Cancel();
		_lifetime.Dispose();
	}

	private bool Refuse(FtpCertificateDecision? decision)
	{
		lock (_gate)
		{
			_lastRefused = decision;
			_refusals++;
		}

		return false;
	}

	private Task<bool> VerifyAsync(HostIdentity identity)
	{
		CancellationToken cancellationToken;
		try
		{
			cancellationToken = _lifetime.Token;
		}
		catch (ObjectDisposedException)
		{
			return Task.FromResult(false);
		}

		// Run off the caller's context: the handshake thread blocks while it waits, and the verifier must not need it.
		return Task.Run(async () =>
		{
			try
			{
				return await _verifier.VerifyAsync(identity, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return false;
			}
			catch (Exception ex)
			{
				_logger.LogWarning("Verifying the FTPS server certificate failed: {Error}", LogSafe.Describe(ex));
				return false;
			}
		}, CancellationToken.None);
	}
}
