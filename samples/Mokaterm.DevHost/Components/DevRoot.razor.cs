using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.DevHost.Demo;
using Mokaterm.DevHost.Hosting;

namespace Mokaterm.DevHost.Components;

/// <summary>
/// Unlocks the throwaway vault with the generated password, then renders the real shell. <see cref="Locked"/> skips the
/// unlock to show the lock screen; <see cref="SessionCount"/> opens that many demo logins so the workspace starts busy.
/// </summary>
public sealed partial class DevRoot : ComponentBase, IDisposable
{
	private readonly CancellationTokenSource _disposeCts = new();
	private bool _ready;

	[Parameter]
	public bool Locked { get; set; }

	[Parameter]
	public int SessionCount { get; set; }

	[Inject]
	private IVault Vault { get; set; } = default!;

	[Inject]
	private DevMasterPassword MasterPassword { get; set; } = default!;

	[Inject]
	private IConnectionRepository Connections { get; set; } = default!;

	[Inject]
	private ISessionManager Sessions { get; set; } = default!;

	[Inject]
	private ILogger<DevRoot> Logger { get; set; } = default!;

	public void Dispose()
	{
		_disposeCts.Cancel();
		_disposeCts.Dispose();
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (!firstRender)
		{
			return;
		}

		CancellationToken token = _disposeCts.Token;
		try
		{
			await Vault.LoadAsync(token);
			if (!Locked && await UnlockAsync(token))
			{
				await OpenSessionsAsync(token);
			}
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			return;
		}
		catch (Exception ex)
		{
			// The shell still renders and shows whatever state the vault is in.
			Logger.LogError(ex, "The DevHost could not prepare the workspace");
		}

		_ready = true;
		StateHasChanged();
	}

	private async Task<bool> UnlockAsync(CancellationToken cancellationToken)
	{
		UnlockResult result = await Vault.UnlockAsync(MasterPassword.Value, cancellationToken);

		// Wrong guesses typed on the lock screen (locked=1) throttle every circuit; wait it out rather than show a lock
		// screen nobody can open.
		while (result.Status == UnlockStatus.Throttled)
		{
			Logger.LogInformation("Unlock attempts are throttled; retrying in {Delay}", result.RetryAfter);
			await Task.Delay(result.RetryAfter, cancellationToken);
			result = await Vault.UnlockAsync(MasterPassword.Value, cancellationToken);
		}

		if (!result.Succeeded)
		{
			Logger.LogWarning("The DevHost vault did not unlock: {Status}", result.Status);
		}

		return result.Succeeded;
	}

	private async Task OpenSessionsAsync(CancellationToken cancellationToken)
	{
		if (SessionCount <= 0)
		{
			return;
		}

		// Connections keep the order they were saved in, so these are the demo logins in seeding order.
		ConnectionCatalog catalog = await Connections.GetCatalogAsync(cancellationToken);
		IEnumerable<ConnectionProfile> logins = catalog.Connections
			.Where(login => login.ProtocolId == DemoProtocolProvider.ProtocolId)
			.Take(SessionCount);

		foreach (ConnectionProfile login in logins)
		{
			await Sessions.OpenAsync(login.Id, cancellationToken: cancellationToken);
		}
	}
}
