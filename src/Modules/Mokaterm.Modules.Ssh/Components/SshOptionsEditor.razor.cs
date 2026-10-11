using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Ssh.Agent;
using Mokaterm.Modules.Ssh.Forwarding;
using Mokaterm.Modules.Ssh.Tunnels;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Ssh.Components;

/// <summary>The SSH part of the connection editor, shared by the ssh and sftp protocols.</summary>
public partial class SshOptionsEditor : ConnectionOptionsEditorBase<SshConnectionOptions>, IDisposable
{
	private readonly FieldDraft<string> _terminalType = new();
	private readonly FieldDraft<string> _keepAlive = new();
	private readonly FieldDraft<string> _display = new();
	private readonly CancellationTokenSource _disposal = new();
	private IReadOnlyList<ConnectionChoice> _connections = [];
	private IReadOnlyList<AgentChoice> _agentKeys = [];
	private string? _agentError;
	private bool _agentLoading;

	/// <summary>One saved login that can be picked as a jump host.</summary>
	public sealed record ConnectionChoice(Guid Id, string Label);

	/// <summary>One key an agent holds, or the "any key" entry.</summary>
	public sealed record AgentChoice(string? Fingerprint, string Label);

	[Inject]
	private ISettingsService SettingsService { get; set; } = default!;

	[Inject]
	private IConnectionRepository Connections { get; set; } = default!;

	[Inject]
	private IVault Vault { get; set; } = default!;

	[Inject]
	private IAppEnvironment AppEnvironment { get; set; } = default!;

	/// <summary>SFTP sessions have no shell, so the terminal options stay hidden for them.</summary>
	private bool IsTerminalProtocol => !string.Equals(ProtocolId, SshProtocolIds.Sftp, StringComparison.OrdinalIgnoreCase);

	/// <summary>On the web host the X server would be the server's own, so X11 forwarding is the desktop app's alone.</summary>
	private bool OffersX11 => AppEnvironment.Kind == HostKind.Desktop;

	private string AgentForwardingHelp => AppEnvironment.Kind == HostKind.Desktop
		? "Programs on the server can use your SSH agent, or this login's saved key when no agent runs."
		: "Programs on the server can use this login's saved key. The server's own agent is never forwarded.";

	private string X11Help => OffersX11
		? "Shows windows of programs on the server through an X server here, such as VcXsrv, Xming or X410."
		: "Needs the desktop app: on the web host the X server would have to run on the server.";

	private string DefaultTerminalType => SettingsService.Get<TerminalSettings>().TerminalType;

	private string DefaultKeepAlive => SettingsService.Get<SshSettings>().KeepAliveSeconds.ToString(CultureInfo.CurrentCulture);

	private string KeepAliveText => Current.KeepAliveSeconds?.ToString(CultureInfo.InvariantCulture) ?? "";

	private IReadOnlyList<ConnectionChoice> SelectedJumpHosts =>
		[.. Current.JumpConnectionIds.Select(id => _connections.FirstOrDefault(choice => choice.Id == id) ?? new ConnectionChoice(id, "A login that no longer exists"))];

	private IReadOnlyList<ConnectionChoice> AvailableJumpHosts =>
		Current.JumpConnectionIds.Count >= SshConnectionOptions.MaxJumpHosts
			? []
			: [.. _connections.Where(choice => !Current.JumpConnectionIds.Contains(choice.Id))];

	private string JumpHostPlaceholder => _connections.Count == 0
		? "No other SSH login is saved"
		: AvailableJumpHosts.Count == 0
			? $"At most {SshConnectionOptions.MaxJumpHosts} hops"
			: "Pick a saved SSH login";

	private IReadOnlyList<AgentChoice> AgentChoices => [new AgentChoice(null, "Any key the agent holds"), .. _agentKeys];

	private AgentChoice SelectedAgentChoice =>
		AgentChoices.FirstOrDefault(choice => choice.Fingerprint == Current.AgentIdentity)
		?? new AgentChoice(Current.AgentIdentity, $"A key the agent no longer holds ({Current.AgentIdentity})");

	private string AgentHelp => _agentLoading
		? "Reading the agent."
		: _agentKeys.Count == 0
			? "The agent holds no keys."
			: $"{_agentKeys.Count} key(s) in the agent.";

	public void Dispose()
	{
		_disposal.Cancel();
		_disposal.Dispose();
		GC.SuppressFinalize(this);
	}

	protected override SshConnectionOptions From(ProtocolOptions options) => SshConnectionOptions.FromOptions(options);

	protected override ProtocolOptions ApplyTo(SshConnectionOptions current, ProtocolOptions options) => current.ApplyTo(options);

	protected override async Task OnInitializedAsync()
	{
		await LoadConnectionsAsync();
		await ReadAgentAsync();
	}

	private async Task LoadConnectionsAsync()
	{
		// A locked vault has no catalog to read, and the editor is only reachable with it unlocked anyway.
		if (Vault.Status != VaultStatus.Unlocked)
		{
			return;
		}

		try
		{
			ConnectionCatalog catalog = await Connections.GetCatalogAsync(_disposal.Token);
			_connections =
			[
				.. catalog.Connections
					.Where(connection => string.Equals(connection.ProtocolId, SshProtocolIds.Ssh, StringComparison.OrdinalIgnoreCase))
					.Select(connection => new ConnectionChoice(
						connection.Id,
						catalog.FindHost(connection.HostId) is { } host ? connection.GetTitle(host) : connection.Label ?? connection.Id.ToString("D", CultureInfo.InvariantCulture)))
					.OrderBy(choice => choice.Label, StringComparer.CurrentCultureIgnoreCase),
			];
		}
		catch (OperationCanceledException)
		{
		}
		catch (VaultLockedException)
		{
		}
	}

	private async Task ReadAgentAsync()
	{
		_agentLoading = true;
		_agentError = null;
		try
		{
			SshAgentSnapshot? snapshot = await SshAgents.TryReadAsync(SettingsService.Get<SshSettings>().AgentEndpoint, _disposal.Token);
			if (snapshot is null)
			{
				_agentKeys = [];
				_agentError = "No SSH agent answered. Start ssh-agent or Pageant, or name one in the SSH settings.";
				return;
			}

			_agentKeys = [.. snapshot.Identities.Select(identity => new AgentChoice(identity.Fingerprint, $"{identity.Display} ({identity.Algorithm})"))];
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			_agentLoading = false;
		}
	}

	private Task SetStartupCommandAsync(string value) => ChangeAsync(Current with { StartupCommand = value });

	private Task SetInitialDirectoryAsync(string value) => ChangeAsync(Current with { InitialDirectory = value });

	private Task SetTerminalTypeAsync(string value)
	{
		if (value.Length > 0 && !SshConnectionOptions.IsValidTerminalType(value))
		{
			_terminalType.Reject(value, "Use letters, digits, dots, dashes, underscores or plus signs.");
			return Task.CompletedTask;
		}

		_terminalType.Clear();
		return ChangeAsync(Current with { TerminalType = value });
	}

	private Task SetKeepAliveAsync(string value)
	{
		if (value.Length == 0)
		{
			_keepAlive.Clear();
			return ChangeAsync(Current with { KeepAliveSeconds = null });
		}

		if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) || seconds > SshSettings.MaxKeepAliveSeconds)
		{
			_keepAlive.Reject(value, string.Create(CultureInfo.CurrentCulture, $"Enter whole seconds from 0 to {SshSettings.MaxKeepAliveSeconds}."));
			return Task.CompletedTask;
		}

		_keepAlive.Clear();
		return ChangeAsync(Current with { KeepAliveSeconds = seconds });
	}

	private Task SetElevationAsync(string value) =>
		Enum.TryParse(value, out SshElevationMode mode) && Enum.IsDefined(mode)
			? ChangeAsync(Current with { Elevation = mode })
			: Task.CompletedTask;

	private Task SetTunnelsAsync(IReadOnlyList<SshTunnelDefinition> tunnels) => ChangeAsync(Current with { Tunnels = tunnels });

	private Task SetProxyAsync(ProxyOptions proxy) => ChangeAsync(Current with { Proxy = proxy });

	private Task SetForwardAgentAsync(bool value) => ChangeAsync(Current with { Forwarding = Current.Forwarding with { ForwardAgent = value } });

	private Task SetForwardX11Async(bool value) => ChangeAsync(Current with { Forwarding = Current.Forwarding with { ForwardX11 = value } });

	private Task SetDisplayAsync(string value)
	{
		if (!X11Display.TryParse(value, out _, out string? error))
		{
			_display.Reject(value, error ?? "This is not an X display.");
			return Task.CompletedTask;
		}

		_display.Clear();
		return ChangeAsync(Current with { Forwarding = Current.Forwarding with { Display = value } });
	}

	private Task SetAgentIdentityAsync(AgentChoice? choice) => ChangeAsync(Current with { AgentIdentity = choice?.Fingerprint });

	private Task AddJumpHostAsync(ConnectionChoice? choice) =>
		choice is null || Current.JumpConnectionIds.Contains(choice.Id) || Current.JumpConnectionIds.Count >= SshConnectionOptions.MaxJumpHosts
			? Task.CompletedTask
			: ChangeAsync(Current with { JumpConnectionIds = [.. Current.JumpConnectionIds, choice.Id] });

	private Task RemoveJumpHostAsync(Guid connectionId) =>
		ChangeAsync(Current with { JumpConnectionIds = [.. Current.JumpConnectionIds.Where(id => id != connectionId)] });

	private Task MoveJumpHostAsync(int index, int offset)
	{
		List<Guid> hops = [.. Current.JumpConnectionIds];
		int target = index + offset;
		if (index < 0 || index >= hops.Count || target < 0 || target >= hops.Count)
		{
			return Task.CompletedTask;
		}

		(hops[index], hops[target]) = (hops[target], hops[index]);
		return ChangeAsync(Current with { JumpConnectionIds = hops });
	}
}
