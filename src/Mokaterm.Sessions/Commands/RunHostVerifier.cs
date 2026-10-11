using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Sessions.Commands;

/// <summary>
/// The scope's host verifier as one host of a command run sees it. The scope's verifier asks through the window's own
/// prompts, not through <see cref="HostPromptGate"/>, so when the run skips hosts that ask, an identity the policy would
/// ask about is caught here first, through <see cref="HostTrustRule"/>, and nothing opens. Everything else goes to the
/// scope's verifier, which keeps its notices and its "accept once" answers, with the host's clock stopped while it decides.
/// </summary>
internal sealed class RunHostVerifier : IHostIdentityVerifier
{
	private readonly IHostIdentityVerifier _inner;
	private readonly IKnownHostsStore _knownHosts;
	private readonly ISettingsService _settings;
	private readonly HostPromptGate _prompts;
	private readonly HostDeadline _deadline;

	public RunHostVerifier(IHostIdentityVerifier inner, IKnownHostsStore knownHosts, ISettingsService settings, HostPromptGate prompts, HostDeadline deadline)
	{
		_inner = inner;
		_knownHosts = knownHosts;
		_settings = settings;
		_prompts = prompts;
		_deadline = deadline;
	}

	public async ValueTask<bool> VerifyAsync(HostIdentity identity, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(identity);
		if (_prompts.SkipsPrompts)
		{
			HostIdentityMatch match = await _knownHosts.MatchAsync(identity, cancellationToken);
			if (HostTrustRule.Decide(identity, match, _settings.Get<SecuritySettings>().HostKeyPolicy) == HostTrustAction.Ask)
			{
				_prompts.Skip(HostPromptGate.DescribeIdentity(identity.Kind, match));
				return false;
			}

			return await _inner.VerifyAsync(identity, cancellationToken);
		}

		using (_deadline.Pause())
		{
			return await _inner.VerifyAsync(identity, cancellationToken);
		}
	}
}
