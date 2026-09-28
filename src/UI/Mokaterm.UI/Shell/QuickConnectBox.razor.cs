using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Presentation;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Shell;

/// <summary>
/// Opens an unsaved session from <c>user@host:port</c> or <c>scheme://user@host:port</c>. The scheme wins over the
/// protocol picker. Saving the connection is offered later from the tab menu.
/// </summary>
public sealed partial class QuickConnectBox : ComponentBase, IDisposable
{
	private const string PreferredProtocolId = "ssh";

	private ElementReference _root;
	private IReadOnlyList<string> _protocolIds = [];
	private string _protocolId = PreferredProtocolId;
	private string _target = "";

	[Inject]
	private IProtocolRegistry Protocols { get; set; } = default!;

	[Inject]
	private SessionActions Sessions { get; set; } = default!;

	[Inject]
	private ShellState Shell { get; set; } = default!;

	[Inject]
	private FormInterop Forms { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private TimeProvider Time { get; set; } = default!;

	public void Dispose() => Shell.QuickConnectFocusRequested -= OnFocusRequested;

	protected override void OnInitialized()
	{
		_protocolIds = [.. Protocols.Protocols.Select(protocol => protocol.Id)];
		if (!_protocolIds.Contains(PreferredProtocolId, StringComparer.OrdinalIgnoreCase) && _protocolIds.Count > 0)
		{
			_protocolId = _protocolIds[0];
		}

		Shell.QuickConnectFocusRequested += OnFocusRequested;
	}

	private string ProtocolLabel(string protocolId) => Protocols.DisplayName(protocolId).ToUpperInvariant();

	private void OnKeyDown(KeyboardEventArgs e)
	{
		if (e.Key == "Enter")
		{
			Connect();
		}
		else if (e.Key == "Escape")
		{
			_target = "";
		}
	}

	private void Connect()
	{
		string text = _target.Trim();
		if (text.Length == 0)
		{
			return;
		}

		if (!QuickConnectTarget.TryParse(text, out QuickConnectTarget? target))
		{
			Interaction.Notify(NoticeSeverity.Warning, "Type a target such as user@host, host:2222 or ssh://user@host.", "Quick connect");
			return;
		}

		string protocolId = target.ProtocolId ?? _protocolId;
		if (Protocols.FindDescriptor(protocolId) is not { } protocol)
		{
			Interaction.Notify(NoticeSeverity.Warning, $"No installed module handles {protocolId}.", "Quick connect");
			return;
		}

		DateTimeOffset now = Time.GetUtcNow();
		HostProfile host = new()
		{
			Id = Guid.NewGuid(),
			Address = target.Address,
			CreatedAt = now,
			UpdatedAt = now,
		};

		ConnectionProfile connection = new()
		{
			Id = Guid.NewGuid(),
			HostId = host.Id,
			ProtocolId = protocol.Id,
			Port = target.Port,
			Username = target.Username,
			AuthenticationMethod = protocol.AuthenticationMethods.Count > 0 ? protocol.AuthenticationMethods[0] : AuthenticationMethod.Password,
			CreatedAt = now,
			UpdatedAt = now,
		};

		Sessions.OpenTransient(host, connection);
		_target = "";
	}

	private void OnFocusRequested() => _ = InvokeAsync(() => Forms.FocusPreferredAsync(_root, selectText: true).AsTask());
}
