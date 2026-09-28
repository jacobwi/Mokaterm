using System.Globalization;
using Moka.Red.Core.Enums;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Modules.Ssh.Tunnels;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Ssh.Components;

/// <summary>
/// The forwarded ports of a live session: what is open, what failed and why, and a form for a tunnel that lasts only
/// as long as this session. Saved tunnels are edited in the connection, not here.
/// </summary>
public sealed partial class SshTunnelsView : SessionViewBase, IDisposable
{
	private IReadOnlyList<SshTunnelStatus> _tunnels = [];
	private ISessionHandle? _handle;
	private IProtocolSession? _attached;
	private ISshTunnelFeature? _feature;
	private SshTunnelDefinition? _draft;
	private string? _error;
	private bool _busy;

	private string CountLabel
	{
		get
		{
			int open = _tunnels.Count(status => status.State == SshTunnelState.Open);
			int failed = _tunnels.Count(status => status.State == SshTunnelState.Failed);
			string text = string.Create(CultureInfo.CurrentCulture, $"{open} of {_tunnels.Count} open");
			return failed == 0 ? text : string.Create(CultureInfo.CurrentCulture, $"{text}, {failed} failed");
		}
	}

	public void Dispose()
	{
		Detach();
		if (_handle is { } handle)
		{
			handle.Changed -= OnSessionChanged;
			_handle = null;
		}
	}

	protected override void OnParametersSet()
	{
		if (!ReferenceEquals(_handle, Session))
		{
			if (_handle is { } previous)
			{
				previous.Changed -= OnSessionChanged;
			}

			_handle = Session;
			_handle.Changed += OnSessionChanged;
		}

		Attach();
	}

	private static MokaColor StateColor(SshTunnelState state) => state switch
	{
		SshTunnelState.Open => MokaColor.Success,
		SshTunnelState.Opening => MokaColor.Warning,
		SshTunnelState.Failed => MokaColor.Error,
		_ => MokaColor.Surface,
	};

	private void Attach()
	{
		IProtocolSession? session = Session.Session;
		if (ReferenceEquals(session, _attached))
		{
			return;
		}

		Detach();
		_attached = session;
		_feature = session?.GetFeature<ISshTunnelFeature>();
		if (_feature is { } feature)
		{
			feature.Changed += OnTunnelsChanged;
			_tunnels = feature.Tunnels;
		}
		else
		{
			_tunnels = [];
		}
	}

	private void Detach()
	{
		if (_feature is { } feature)
		{
			feature.Changed -= OnTunnelsChanged;
		}

		_feature = null;
		_attached = null;
	}

	// The manager raises this from whichever thread opened or closed a port.
	private void OnTunnelsChanged() => _ = InvokeAsync(() =>
	{
		if (_feature is { } feature)
		{
			_tunnels = feature.Tunnels;
		}

		StateHasChanged();
	});

	private void OnSessionChanged() => _ = InvokeAsync(() =>
	{
		Attach();
		StateHasChanged();
	});

	private void ToggleDraft()
	{
		_draft = _draft is null ? SshTunnelDefinition.Create(SshTunnelKind.Local) : null;
		_error = null;
	}

	private void OnDraftChanged(SshTunnelDefinition tunnel) => _draft = tunnel;

	private async Task AddAsync()
	{
		if (_feature is not { } feature || _draft is not { } draft || draft.Validate() is not null)
		{
			return;
		}

		await RunAsync(async () =>
		{
			SshTunnelStatus status = await feature.AddAsync(draft);
			_draft = status.State == SshTunnelState.Failed ? draft : null;
			_error = status.Error;
		});
	}

	private Task OpenAsync(Guid tunnelId) => WithFeatureAsync(feature => feature.OpenAsync(tunnelId));

	private Task CloseAsync(Guid tunnelId) => WithFeatureAsync(feature => feature.CloseAsync(tunnelId));

	private Task RemoveAsync(Guid tunnelId)
	{
		if (_feature is not { } feature)
		{
			return Task.CompletedTask;
		}

		return RunAsync(async () =>
		{
			await feature.RemoveAsync(tunnelId);
			_error = null;
		});
	}

	private Task WithFeatureAsync(Func<ISshTunnelFeature, Task<SshTunnelStatus>> action)
	{
		if (_feature is not { } feature)
		{
			return Task.CompletedTask;
		}

		return RunAsync(async () => _error = (await action(feature)).Error);
	}

	private async Task RunAsync(Func<Task> action)
	{
		_busy = true;
		_error = null;
		try
		{
			await action();
		}
		catch (Exception ex) when (ex is ObjectDisposedException or KeyNotFoundException or ArgumentException)
		{
			_error = ex.Message;
		}
		finally
		{
			_busy = false;
			if (_feature is { } feature)
			{
				_tunnels = feature.Tunnels;
			}
		}
	}
}
