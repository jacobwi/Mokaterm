using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Shell;

/// <summary>
/// Names the focused pane's active session in the window title, and nothing once the shell goes, which is what locking
/// the vault does. It draws nothing.
/// </summary>
public sealed class WindowTitleWatch : ComponentBase, IDisposable
{
	private ISessionHandle? _watched;
	private string? _shown;
	private bool _showing;
	private bool _disposed;

	[Inject]
	private SessionWorkspace Workspace { get; set; } = default!;

	[Inject]
	private IVault Vault { get; set; } = default!;

	[Inject]
	private IWindowTitle Title { get; set; } = default!;

	public void Dispose()
	{
		_disposed = true;
		Workspace.Changed -= OnChanged;
		Vault.StatusChanged -= OnVaultStatusChanged;
		Watch(null);
		Show(null);
	}

	protected override void OnInitialized()
	{
		Workspace.Changed += OnChanged;
		Vault.StatusChanged += OnVaultStatusChanged;
	}

	// After the first render rather than before it, so the page can take the call.
	protected override void OnAfterRender(bool firstRender)
	{
		if (firstRender)
		{
			Update();
		}
	}

	private void Update()
	{
		if (_disposed)
		{
			return;
		}

		ISessionHandle? active = Vault.Status == VaultStatus.Unlocked ? Workspace.Active : null;
		Watch(active);
		Show(WindowTitleText.Subject(active));
	}

	// The handle is watched as well, because editing its login, or saving a quick-connect tab as one, renames the tab
	// without the workspace changing.
	private void Watch(ISessionHandle? session)
	{
		if (ReferenceEquals(_watched, session))
		{
			return;
		}

		if (_watched is not null)
		{
			_watched.Changed -= OnChanged;
		}

		_watched = session;
		if (session is not null)
		{
			session.Changed += OnChanged;
		}
	}

	private void Show(string? subject)
	{
		if (_showing && string.Equals(subject, _shown, StringComparison.Ordinal))
		{
			return;
		}

		_showing = true;
		_shown = subject;
		_ = Title.ShowAsync(subject);
	}

	// Every one of these may fire on any thread.
	private void OnChanged() => _ = InvokeAsync(Update);

	private void OnVaultStatusChanged(VaultStatus _) => OnChanged();
}
