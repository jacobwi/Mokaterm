using Microsoft.AspNetCore.Components;
using Moka.Red.Feedback.Toast;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.UI.Interaction;

/// <summary>
/// Renders queued <see cref="IUserInteraction"/> prompts one at a time and turns notices into toasts. While the vault
/// is not unlocked both wait, so hostnames and fingerprints never show over the lock screen.
/// </summary>
public sealed partial class InteractionHost : ComponentBase, IDisposable
{
	private InteractionRequest? _request;

	[Inject]
	private UserInteractionService Service { get; set; } = default!;

	[Inject]
	private IVault Vault { get; set; } = default!;

	[Inject]
	private IMokaToastService Toasts { get; set; } = default!;

	public void Dispose()
	{
		Service.Changed -= OnChanged;
		Vault.StatusChanged -= OnVaultStatusChanged;
	}

	protected override void OnInitialized()
	{
		Service.Changed += OnChanged;
		Vault.StatusChanged += OnVaultStatusChanged;
		Refresh();
	}

	private void OnChanged() => _ = InvokeAsync(() =>
	{
		Refresh();
		StateHasChanged();
	});

	private void OnVaultStatusChanged(VaultStatus status) => OnChanged();

	private void Refresh()
	{
		bool unlocked = Vault.Status == VaultStatus.Unlocked;
		_request = unlocked ? Service.Current : null;
		if (!unlocked)
		{
			return;
		}

		foreach (PendingNotice notice in Service.TakeNotices())
		{
			Toasts.Show(notice.Message, ToSeverity(notice.Severity), options =>
			{
				options.Title = notice.Title;
				if (notice.Severity == NoticeSeverity.Error)
				{
					options.DurationMs = 8000;
				}
			});
		}
	}

	private static MokaToastSeverity ToSeverity(NoticeSeverity severity) => severity switch
	{
		NoticeSeverity.Success => MokaToastSeverity.Success,
		NoticeSeverity.Warning => MokaToastSeverity.Warning,
		NoticeSeverity.Error => MokaToastSeverity.Error,
		_ => MokaToastSeverity.Info,
	};
}
