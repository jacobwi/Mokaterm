using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Moka.Red.Core.Icons;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Security;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.Common.Icons;

namespace Mokaterm.UI.Settings.Pages.KnownHosts;

/// <summary>Trusted server identities from <see cref="IKnownHostsStore"/>, with filtering, copying and removal.</summary>
public sealed partial class KnownHostsPage : VaultListPageBase<KnownHost>
{
	[Inject]
	private IKnownHostsStore KnownHosts { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private TimeProvider Time { get; set; } = default!;

	[Inject]
	private ILogger<KnownHostsPage> Logger { get; set; } = default!;

	protected override string LoadErrorMessage => "Known hosts could not be loaded.";

	protected override void OnInitialized()
	{
		base.OnInitialized();
		KnownHosts.Changed += RequestReload;
	}

	protected override async ValueTask<IReadOnlyList<KnownHost>> LoadItemsAsync(CancellationToken cancellationToken)
	{
		IReadOnlyList<KnownHost> hosts = await KnownHosts.ListAsync(cancellationToken);

		// Same comparison MokaTable uses when it sorts the Host column itself.
		return [.. hosts.OrderBy(Endpoint, StringComparer.CurrentCulture)];
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			KnownHosts.Changed -= RequestReload;
		}

		base.Dispose(disposing);
	}

	/// <summary><c>host:port</c>, with IPv6 addresses in brackets.</summary>
	private static string Endpoint(KnownHost host) =>
		host.Host.Contains(':')
			? string.Create(CultureInfo.InvariantCulture, $"[{host.Host}]:{host.Port}")
			: string.Create(CultureInfo.InvariantCulture, $"{host.Host}:{host.Port}");

	// The store keeps one entry per host, port, kind and algorithm; the fingerprint is included so a
	// store bug surfaces as a duplicate row instead of MokaTable throwing on a repeated key.
	private static object IdentityKey(KnownHost host) => (host.Host, host.Port, host.Kind, host.Algorithm, host.Fingerprint);

	private static string KindText(HostIdentityKind kind) => kind == HostIdentityKind.SshHostKey ? "SSH key" : "Certificate";

	private static MokaIconDefinition KindIcon(HostIdentityKind kind) =>
		kind == HostIdentityKind.SshHostKey ? MokatermIcons.Key : MokatermIcons.ShieldCheck;

	private string LastSeenText(KnownHost host) =>
		host.LastSeenAt is { } lastSeen ? DisplayFormat.Relative(lastSeen, Time.GetUtcNow()) : "Never";

	private async Task RemoveAsync(KnownHost host)
	{
		string identity = host.Kind == HostIdentityKind.SshHostKey ? $"{host.Algorithm} host key" : "certificate";
		bool confirmed = (await Interaction.ConfirmAsync(new ConfirmPrompt
		{
			Title = "Remove known host",
			Message = $"Stop trusting the {identity} of {Endpoint(host)}? You will have to verify the server again the next time you connect.",
			ConfirmText = "Remove",
			Destructive = true,
		})).Confirmed;

		if (!confirmed)
		{
			return;
		}

		try
		{
			await KnownHosts.RemoveAsync(host);
			Interaction.Notify(NoticeSeverity.Success, $"Removed {Endpoint(host)} from known hosts.");
		}
		catch (VaultLockedException)
		{
			Interaction.Notify(NoticeSeverity.Warning, VaultFailure.Locked("remove known hosts"));
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "Removing a known host failed");
			Interaction.Notify(NoticeSeverity.Error, "The known host could not be removed.");
		}
	}
}
