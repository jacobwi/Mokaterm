using System.Net.Security;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Security;
using MQTTnet;

namespace Mokaterm.Modules.Mqtt.Protocol;

/// <summary>
/// Decides which broker certificates a session accepts. MQTTnet's <c>CertificateValidationHandler</c> is
/// synchronous, and asking the user is not, so a verdict that takes longer than a handshake can wait for refuses
/// that handshake and the connect loop reconnects once the answer is in. Refusing is what keeps the login safe: the
/// CONNECT packet with the user name and password is only written after TLS has come up.
/// </summary>
internal sealed class MqttCertificateTrust : IDisposable
{
	/// <summary>
	/// How long a handshake waits for the verifier. Long enough for a known fingerprint to come back from the vault,
	/// short enough that a prompt does not hold MQTTnet's connect thread.
	/// </summary>
	public static readonly TimeSpan DefaultDecisionWait = TimeSpan.FromSeconds(2);

	private readonly IHostIdentityVerifier _verifier;
	private readonly string _host;
	private readonly int _port;
	private readonly TimeSpan _decisionWait;
	private readonly ILogger _logger;
	private readonly CancellationTokenSource _lifetime = new();
	private readonly Lock _gate = new();
	private readonly HashSet<string> _approved = new(StringComparer.Ordinal);
	private readonly Dictionary<string, MqttCertificateDecision> _decisions = new(StringComparer.Ordinal);
	private MqttCertificateDecision? _lastRefused;
	private int _refusals;
	private int _disposed;

	public MqttCertificateTrust(IHostIdentityVerifier verifier, string host, int port, ILogger logger, TimeSpan? decisionWait = null)
	{
		_verifier = verifier;
		_host = host;
		_port = port;
		_logger = logger;
		_decisionWait = decisionWait ?? DefaultDecisionWait;
	}

	/// <summary>Counts refused handshakes, so a failed connect can tell whether a certificate was the reason.</summary>
	public int RefusalCount => Volatile.Read(ref _refusals);

	/// <summary>The decision behind the most recent refusal.</summary>
	public MqttCertificateDecision? LastRefused
	{
		get
		{
			lock (_gate)
			{
				return _lastRefused;
			}
		}
	}

	/// <summary>Accepts a fingerprint from here on, after the user approved it while a handshake was refused.</summary>
	public void Approve(string fingerprint)
	{
		lock (_gate)
		{
			_approved.Add(fingerprint);
		}
	}

	/// <summary>The handler MQTTnet calls during the TLS handshake. Anything it does not already trust is refused.</summary>
	public bool Validate(MqttClientCertificateValidationEventArgs args)
	{
		ArgumentNullException.ThrowIfNull(args);
		if (args.Certificate is null || Volatile.Read(ref _disposed) != 0)
		{
			return false;
		}

		HostIdentity identity;
		try
		{
			identity = TlsHostIdentity.Create(_host, _port, args.Certificate, args.SslPolicyErrors);
		}
		catch (CryptographicException ex)
		{
			_logger.LogWarning("Could not read the broker's certificate: {Error}", LogSafe.Describe(ex));
			return Refuse(null);
		}

		MqttCertificateDecision decision;
		lock (_gate)
		{
			if (_approved.Contains(identity.Fingerprint))
			{
				return true;
			}

			if (!_decisions.TryGetValue(identity.Fingerprint, out MqttCertificateDecision? existing))
			{
				existing = new MqttCertificateDecision(identity, VerifyAsync(identity));
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

		// Cancels a prompt still open for this connection.
		_lifetime.Cancel();
		_lifetime.Dispose();
	}

	private bool Refuse(MqttCertificateDecision? decision)
	{
		lock (_gate)
		{
			_lastRefused = decision;
		}

		Interlocked.Increment(ref _refusals);
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

		// Off the handshake's thread: MQTTnet is inside its own connect when this starts, and the verifier may need
		// the renderer to put a dialog on screen.
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
				_logger.LogWarning("Verifying the broker's certificate failed: {Error}", LogSafe.Describe(ex));
				return false;
			}
		}, CancellationToken.None);
	}
}
