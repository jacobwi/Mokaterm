using System.Globalization;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Core.Security;

/// <summary>Applies <see cref="HostKeyPolicy"/> to what a server presents, asking the user when the policy says so.</summary>
internal sealed class HostIdentityVerifier : IHostIdentityVerifier
{
	private readonly IKnownHostsStore _knownHosts;
	private readonly ISettingsService _settings;
	private readonly IUserInteraction _interaction;
	private readonly Lock _sync = new();

	// "Accept once" answers, kept for this UI scope only. The key includes the fingerprint, so a different identity asks again.
	private readonly HashSet<HostIdentityKey> _acceptedOnce = [];

	// Prompts on screen, by identity. Sessions that reach the same new host together wait for the one answer instead of
	// stacking a second dialog for an identity the user is already deciding on.
	private readonly Dictionary<HostIdentityKey, Task<bool>> _openPrompts = [];

	public HostIdentityVerifier(IKnownHostsStore knownHosts, ISettingsService settings, IUserInteraction interaction)
	{
		_knownHosts = knownHosts;
		_settings = settings;
		_interaction = interaction;
	}

	public async ValueTask<bool> VerifyAsync(HostIdentity identity, CancellationToken cancellationToken = default)
	{
		HostIdentityKey key = HostIdentityKey.From(identity);
		lock (_sync)
		{
			if (_acceptedOnce.Contains(key))
			{
				return true;
			}
		}

		HostIdentityMatch match = await _knownHosts.MatchAsync(identity, cancellationToken);
		if (match == HostIdentityMatch.Trusted)
		{
			return true;
		}

		if (match == HostIdentityMatch.Unknown && identity is { Kind: HostIdentityKind.TlsCertificate, ChainTrusted: true })
		{
			return true;
		}

		HostKeyPolicy policy = _settings.Get<SecuritySettings>().HostKeyPolicy;
		string target = Describe(key);
		string what = key.Kind == HostIdentityKind.TlsCertificate ? "certificate" : "host key";
		if (match == HostIdentityMatch.Unknown)
		{
			switch (policy)
			{
				case HostKeyPolicy.Strict:
					_interaction.Notify(NoticeSeverity.Error, $"The {what} of {target} is not trusted, and the host key policy only allows known hosts.", "Connection refused");
					return false;

				case HostKeyPolicy.AcceptNew:
					await _knownHosts.TrustAsync(identity, cancellationToken);
					_interaction.Notify(NoticeSeverity.Info, $"Trusted the new {what} of {target} ({key.Algorithm} {key.Fingerprint}).", "New host trusted");
					return true;
			}

			return await AskAsync(identity, key, HostIdentityMatch.Unknown, previous: null, cancellationToken);
		}

		if (policy is HostKeyPolicy.Strict or HostKeyPolicy.AcceptNew)
		{
			_interaction.Notify(NoticeSeverity.Warning, $"The {what} of {target} has changed, so the connection was refused. If the server was reinstalled, remove its old entry under Known hosts.", "Host identity changed");
			return false;
		}

		IReadOnlyList<KnownHost> knownHosts = await _knownHosts.ListAsync(cancellationToken);
		KnownHost? previous = knownHosts.FirstOrDefault(entry => key.SameSlot(HostIdentityKey.From(entry)));
		return await AskAsync(identity, key, HostIdentityMatch.Changed, previous, cancellationToken);
	}

	private static string Describe(HostIdentityKey key) =>
		key.Host.Contains(':', StringComparison.Ordinal)
			? string.Create(CultureInfo.InvariantCulture, $"[{key.Host}]:{key.Port}")
			: string.Create(CultureInfo.InvariantCulture, $"{key.Host}:{key.Port}");

	private async Task<bool> AskAsync(HostIdentity identity, HostIdentityKey key, HostIdentityMatch match, KnownHost? previous, CancellationToken cancellationToken)
	{
		TaskCompletionSource<bool> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
		Task<bool>? open;
		lock (_sync)
		{
			if (!_openPrompts.TryGetValue(key, out open))
			{
				_openPrompts[key] = answer.Task;
			}
		}

		if (open is not null)
		{
			try
			{
				return await open.WaitAsync(cancellationToken);
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				// The session that asked gave up before the user answered, so this one asks for itself.
				return await VerifyAsync(identity, cancellationToken);
			}
		}

		try
		{
			bool trusted = await PromptAsync(identity, key, match, previous, cancellationToken);
			answer.SetResult(trusted);
			return trusted;
		}
		catch (OperationCanceledException)
		{
			answer.SetCanceled(cancellationToken);
			throw;
		}
		catch (Exception ex)
		{
			answer.SetException(ex);
			// Nobody may be waiting; observing it keeps the failure from surfacing again as an unobserved task exception.
			_ = answer.Task.Exception;
			throw;
		}
		finally
		{
			lock (_sync)
			{
				_ = _openPrompts.Remove(key);
			}
		}
	}

	private async Task<bool> PromptAsync(HostIdentity identity, HostIdentityKey key, HostIdentityMatch match, KnownHost? previous, CancellationToken cancellationToken)
	{
		HostTrustDecision decision = await _interaction.ConfirmHostIdentityAsync(
			new HostTrustPrompt { Identity = identity, Match = match, Previous = previous },
			cancellationToken);

		switch (decision)
		{
			case HostTrustDecision.AcceptOnce:
				lock (_sync)
				{
					_acceptedOnce.Add(key);
				}

				return true;

			case HostTrustDecision.AcceptAndSave:
				await _knownHosts.TrustAsync(identity, cancellationToken);
				return true;

			default:
				return false;
		}
	}
}
