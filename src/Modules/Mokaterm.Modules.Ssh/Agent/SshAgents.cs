namespace Mokaterm.Modules.Ssh.Agent;

/// <summary>An agent that answered, with the keys it holds.</summary>
internal sealed record SshAgentSnapshot(SshAgentAddress Address, IReadOnlyList<SshAgentIdentity> Identities);

/// <summary>
/// Picks one agent and reads its keys. The address is kept so every later signature goes back to the same agent even
/// when the machine runs more than one.
/// </summary>
internal static class SshAgents
{
	public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

	/// <summary>The first agent that answers and holds keys, or the first that answers at all.</summary>
	/// <exception cref="SshAgentException">No agent answered.</exception>
	public static SshAgentSnapshot Read(string? configured = null, TimeSpan? timeout = null)
	{
		TimeSpan wait = timeout ?? DefaultTimeout;
		IReadOnlyList<SshAgentAddress> addresses = SshAgentEndpoints.Discover(configured);
		SshAgentSnapshot? empty = null;
		Exception? last = null;
		foreach (SshAgentAddress address in addresses)
		{
			try
			{
				using SshAgentClient client = SshAgentClient.Connect(address, wait);
				IReadOnlyList<SshAgentIdentity> identities = client.ListIdentities();
				if (identities.Count > 0)
				{
					return new SshAgentSnapshot(address, identities);
				}

				empty ??= new SshAgentSnapshot(address, identities);
			}
			catch (SshAgentException ex)
			{
				last = ex;
			}
		}

		return empty ?? throw new SshAgentException(
			addresses.Count == 0
				? "No SSH agent was found. Start ssh-agent or Pageant, or name one in the SSH settings."
				: "No SSH agent answered.",
			last);
	}

	/// <summary>Reads the agent off the caller's thread, for UI code. Returns null instead of throwing when none answers.</summary>
	public static async Task<SshAgentSnapshot?> TryReadAsync(string? configured = null, CancellationToken cancellationToken = default)
	{
		try
		{
			return await Task.Run(() => Read(configured), cancellationToken);
		}
		catch (SshAgentException)
		{
			return null;
		}
	}

	/// <summary>
	/// The key source SSH.NET authenticates with. <paramref name="fingerprint"/> narrows it to one key; null offers
	/// every key the agent holds, which is what <c>ssh</c> does.
	/// </summary>
	/// <exception cref="SshAgentException">The agent holds no usable key, or not the one that was asked for.</exception>
	public static SshAgentKeySource CreateKeySource(SshAgentSnapshot snapshot, string? fingerprint, TimeSpan? timeout = null)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		TimeSpan wait = timeout ?? DefaultTimeout;
		IReadOnlyList<SshAgentIdentity> identities = Select(snapshot, fingerprint);
		SshAgentAddress address = snapshot.Address;
		SshAgentKeySource source = new(identities, () => SshAgentClient.Connect(address, wait));
		return source.HostKeyAlgorithms.Count > 0
			? source
			: throw new SshAgentException($"The SSH agent at {address.Display} holds no key this client can use.");
	}

	private static IReadOnlyList<SshAgentIdentity> Select(SshAgentSnapshot snapshot, string? fingerprint)
	{
		if (snapshot.Identities.Count == 0)
		{
			throw new SshAgentException($"The SSH agent at {snapshot.Address.Display} holds no keys. Add one with ssh-add.");
		}

		if (string.IsNullOrWhiteSpace(fingerprint))
		{
			return snapshot.Identities;
		}

		IReadOnlyList<SshAgentIdentity> chosen =
			[.. snapshot.Identities.Where(identity => string.Equals(identity.Fingerprint, fingerprint, StringComparison.Ordinal))];
		return chosen.Count > 0
			? chosen
			: throw new SshAgentException($"The SSH agent no longer holds the key this connection asks for ({fingerprint}).");
	}
}
