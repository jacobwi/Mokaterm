using Microsoft.Extensions.Logging;
using Moka.Red.ContextMenu;
using Moka.Red.Icons;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Presentation;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Connections;

/// <summary>Folder, host and login operations and their context menus, shared by the explorer, welcome view and commands.</summary>
internal sealed class ConnectionActions
{
	private readonly IConnectionRepository _repository;
	private readonly ICredentialStore _credentials;
	private readonly CatalogState _catalog;
	private readonly SessionActions _sessions;
	private readonly ShellState _shell;
	private readonly IProtocolRegistry _protocols;
	private readonly IUiContributions _contributions;
	private readonly IUserInteraction _interaction;
	private readonly IClipboardService _clipboard;
	private readonly ILogger<ConnectionActions> _logger;

	public ConnectionActions(
		IConnectionRepository repository,
		ICredentialStore credentials,
		CatalogState catalog,
		SessionActions sessions,
		ShellState shell,
		IProtocolRegistry protocols,
		IUiContributions contributions,
		IUserInteraction interaction,
		IClipboardService clipboard,
		ILogger<ConnectionActions> logger)
	{
		_repository = repository;
		_credentials = credentials;
		_catalog = catalog;
		_sessions = sessions;
		_shell = shell;
		_protocols = protocols;
		_contributions = contributions;
		_interaction = interaction;
		_clipboard = clipboard;
		_logger = logger;
	}

	public void NewHost(Guid? folderId = null) => _shell.OpenEditor(new HostEditorRequest(null, folderId));

	public void EditHost(Guid hostId) => _shell.OpenEditor(new HostEditorRequest(hostId));

	public void NewLogin(Guid? hostId = null) => _shell.OpenEditor(new ConnectionEditorRequest { HostId = hostId });

	public void EditLogin(Guid connectionId) => _shell.OpenEditor(new ConnectionEditorRequest { ConnectionId = connectionId });

	public void NewFolder(Guid? parentId = null) => _shell.OpenEditor(new FolderEditorRequest(null, parentId));

	public void RenameFolder(Guid folderId) => _shell.OpenEditor(new FolderEditorRequest(folderId));

	/// <summary>Opens the wizard that copies hosts out of OpenSSH, PuTTY or WinSCP.</summary>
	public void ImportConnections(Guid? folderId = null) => _shell.OpenEditor(new ImportEditorRequest(folderId));

	/// <summary>Opens the dialog that writes every saved host and login to a file, without any secret.</summary>
	public void ExportConnections() => _shell.OpenEditor(new ExportEditorRequest());

	/// <summary><c>user@address</c>, or the address alone when the login has no username.</summary>
	public static string UserAtHost(HostProfile host, ConnectionProfile login) =>
		string.IsNullOrWhiteSpace(login.Username) ? host.Address : $"{login.Username}@{host.Address}";

	public IReadOnlyList<MokaContextMenuItem> FolderMenu(ConnectionFolder folder) =>
	[
		new() { Text = "New host here", Icon = MokatermIcons.Server, OnClickSync = () => NewHost(folder.Id) },
		new() { Text = "New folder", Icon = MokatermIcons.FolderPlus, OnClickSync = () => NewFolder(folder.Id) },
		new() { Text = "Import hosts here", Icon = MokatermIcons.FileUpload, OnClickSync = () => ImportConnections(folder.Id) },
		new() { Text = "Rename", Icon = MokaIcons.Action.Edit, DividerBefore = true, OnClickSync = () => RenameFolder(folder.Id) },
		new() { Text = "Delete", Icon = MokaIcons.Action.Delete, OnClick = () => DeleteFolderAsync(folder) },
	];

	public IReadOnlyList<MokaContextMenuItem> HostMenu(HostProfile host)
	{
		ConnectionCatalog catalog = _catalog.Catalog;
		List<MokaContextMenuItem> connectItems =
		[
			.. catalog.ConnectionsOf(host.Id).Select(login => new MokaContextMenuItem
			{
				Text = $"{login.GetTitle(host)} ({_protocols.DisplayName(login.ProtocolId)})",
				Icon = _contributions.GetProtocolIcon(login.ProtocolId),
				OnClick = () => _sessions.OpenAsync(login.Id, reuseExisting: true),
			}),
		];

		return
		[
			new() { Text = "Connect", Icon = MokatermIcons.Connect, Children = connectItems, Disabled = connectItems.Count == 0 },
			new() { Text = "New login", Icon = MokatermIcons.User, OnClickSync = () => NewLogin(host.Id) },
			new() { Text = "Edit host", Icon = MokaIcons.Action.Edit, DividerBefore = true, OnClickSync = () => EditHost(host.Id) },
			new() { Text = "Duplicate host", Icon = MokaIcons.Content.Copy, OnClick = () => DuplicateHostAsync(host) },
			new() { Text = "Copy address", Icon = MokaIcons.Content.Link, OnClick = () => CopyAsync(host.Address) },
			new() { Text = "Move to folder", Icon = MokatermIcons.Folder, Children = MoveItems(host, catalog) },
			new() { Text = "Delete host", Icon = MokaIcons.Action.Delete, DividerBefore = true, OnClick = () => DeleteHostAsync(host) },
		];
	}

	public IReadOnlyList<MokaContextMenuItem> LoginMenu(HostProfile host, ConnectionProfile login)
	{
		List<MokaContextMenuItem> items =
		[
			new() { Text = "Connect", Icon = MokatermIcons.Connect, OnClick = () => _sessions.OpenAsync(login.Id, reuseExisting: true) },
			new() { Text = "Connect in new tab", Icon = MokaIcons.Action.Add, OnClick = () => _sessions.OpenAsync(login.Id) },
		];

		if (_protocols.FindFileBrowserVariant(login.ProtocolId) is { } variant)
		{
			items.Add(new MokaContextMenuItem
			{
				Text = $"Open {variant.DisplayName} browser",
				Icon = MokatermIcons.FolderOpen,
				OnClick = () => _sessions.OpenAsync(login.Id, variant.Id, reuseExisting: true),
			});
		}

		items.Add(new MokaContextMenuItem { Text = "Edit", Icon = MokaIcons.Action.Edit, DividerBefore = true, OnClickSync = () => EditLogin(login.Id) });
		items.Add(new MokaContextMenuItem { Text = "Duplicate", Icon = MokaIcons.Content.Copy, OnClick = () => DuplicateLoginAsync(login) });
		items.Add(new MokaContextMenuItem { Text = "Copy user@host", Icon = MokaIcons.Content.Link, OnClick = () => CopyAsync(UserAtHost(host, login)) });
		items.Add(new MokaContextMenuItem { Text = "Favorite", Checked = login.IsFavorite, OnClick = () => ToggleFavoriteAsync(login) });
		items.Add(new MokaContextMenuItem { Text = "Delete", Icon = MokaIcons.Action.Delete, DividerBefore = true, OnClick = () => DeleteLoginAsync(host, login) });
		return items;
	}

	public IReadOnlyList<MokaContextMenuItem> EmptyAreaMenu() =>
	[
		new() { Text = "New host", Icon = MokatermIcons.Server, Shortcut = ShellShortcuts.LabelFor(ShellCommandIds.NewHost), OnClickSync = () => NewHost() },
		new() { Text = "New folder", Icon = MokatermIcons.FolderPlus, OnClickSync = () => NewFolder() },
		new() { Text = "Import saved hosts", Icon = MokatermIcons.FileUpload, OnClickSync = () => ImportConnections() },
		new() { Text = "Export saved hosts", Icon = MokatermIcons.FileDownload, OnClickSync = ExportConnections },
		new() { Text = "Quick connect", Icon = MokatermIcons.QuickConnect, Shortcut = ShellShortcuts.LabelFor(ShellCommandIds.QuickConnect), OnClickSync = _shell.FocusQuickConnect },
	];

	public async Task MoveHostAsync(Guid hostId, Guid? folderId)
	{
		if (_catalog.Catalog.FindHost(hostId) is not { } host || host.FolderId == folderId)
		{
			return;
		}

		await RunAsync("move the host", () => _repository.SaveHostAsync(host with { FolderId = folderId }));
	}

	/// <summary>Keeps a terminal theme with the login, so every session it opens starts with it.</summary>
	public Task SetTerminalThemeAsync(ConnectionProfile login, string? themeId)
	{
		TerminalProfileOverrides overrides = (login.Terminal ?? new TerminalProfileOverrides()) with { ThemeId = themeId };
		return RunAsync(
			"save the terminal theme",
			() => _repository.SaveConnectionAsync(login with { Terminal = overrides.IsEmpty ? null : overrides }));
	}

	public Task ToggleFavoriteAsync(ConnectionProfile login) =>
		RunAsync("update the login", () => _repository.SaveConnectionAsync(login with { IsFavorite = !login.IsFavorite }));

	public async Task DeleteFolderAsync(ConnectionFolder folder)
	{
		string destination = folder.ParentId is { } parentId && _catalog.Catalog.FindFolder(parentId) is { } parent
			? parent.Name
			: "the top level";

		bool confirmed = (await _interaction.ConfirmAsync(new ConfirmPrompt
		{
			Title = "Delete folder",
			Message = $"Delete the folder {folder.Name}? Its hosts and subfolders move to {destination}.",
			ConfirmText = "Delete",
			Destructive = true,
		})).Confirmed;

		if (confirmed)
		{
			await RunAsync("delete the folder", () => _repository.DeleteFolderAsync(folder.Id));
		}
	}

	public async Task DeleteHostAsync(HostProfile host)
	{
		List<ConnectionProfile> logins = [.. _catalog.Catalog.ConnectionsOf(host.Id)];
		string message = logins.Count == 0
			? $"Delete the host {host.DisplayName}?"
			: $"Delete the host {host.DisplayName} and its logins: {string.Join(", ", logins.Select(login => $"{login.GetTitle(host)} ({_protocols.DisplayName(login.ProtocolId)})"))}? Passwords and keys saved only for these logins are deleted too.";

		bool confirmed = (await _interaction.ConfirmAsync(new ConfirmPrompt
		{
			Title = "Delete host",
			Message = message,
			ConfirmText = "Delete",
			Destructive = true,
		})).Confirmed;

		if (confirmed)
		{
			await RunAsync("delete the host", () => _repository.DeleteHostAsync(host.Id));
		}
	}

	public async Task DeleteLoginAsync(HostProfile host, ConnectionProfile login)
	{
		bool confirmed = (await _interaction.ConfirmAsync(new ConfirmPrompt
		{
			Title = "Delete login",
			Message = $"Delete the login {login.GetTitle(host)} ({_protocols.DisplayName(login.ProtocolId)})? A password or key saved only for this login is deleted too.",
			ConfirmText = "Delete",
			Destructive = true,
		})).Confirmed;

		if (confirmed)
		{
			await RunAsync("delete the login", () => _repository.DeleteConnectionAsync(login.Id));
		}
	}

	/// <summary>Copies the host and all of its logins, then opens the copy in the host editor.</summary>
	public async Task DuplicateHostAsync(HostProfile host)
	{
		HostProfile copy = host with { Id = Guid.NewGuid(), Name = $"{host.DisplayName} (copy)" };
		List<ConnectionProfile> logins = [.. _catalog.Catalog.ConnectionsOf(host.Id)];
		bool saved = await RunAsync("duplicate the host", async () =>
		{
			await _repository.SaveHostAsync(copy);
			foreach (ConnectionProfile login in logins)
			{
				await CopyLoginAsync(login, copy.Id);
			}
		});

		if (saved)
		{
			EditHost(copy.Id);
		}
	}

	/// <summary>Copies a login on the same host, then opens the copy in the connection editor.</summary>
	public async Task DuplicateLoginAsync(ConnectionProfile login)
	{
		Guid copyId = Guid.Empty;
		bool saved = await RunAsync("duplicate the login", async () => copyId = await CopyLoginAsync(login, login.HostId));
		if (saved)
		{
			EditLogin(copyId);
		}
	}

	public async Task CopyAsync(string text)
	{
		try
		{
			await _clipboard.WriteTextAsync(text);
			_interaction.Notify(NoticeSeverity.Success, "Copied to the clipboard.");
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "Copying to the clipboard failed.");
			_interaction.Notify(NoticeSeverity.Warning, "The clipboard is not available.");
		}
	}

	private List<MokaContextMenuItem> MoveItems(HostProfile host, ConnectionCatalog catalog)
	{
		bool inValidFolder = host.FolderId is { } currentId && catalog.FindFolder(currentId) is not null;
		List<MokaContextMenuItem> items =
		[
			new() { Text = "Top level", Checked = !inValidFolder, OnClick = () => MoveHostAsync(host.Id, null) },
		];

		foreach ((Guid folderId, string path) in FolderPaths.Sorted(FolderPaths.Build(catalog)))
		{
			items.Add(new MokaContextMenuItem { Text = path, Checked = host.FolderId == folderId, OnClick = () => MoveHostAsync(host.Id, folderId) });
		}

		return items;
	}

	/// <summary>
	/// Saves a copy of <paramref name="login"/> on <paramref name="hostId"/>. A private credential is copied too, because
	/// it belongs to one login and is deleted with it; shared keychain credentials are referenced as they are.
	/// </summary>
	private async Task<Guid> CopyLoginAsync(ConnectionProfile login, Guid hostId)
	{
		Guid copyId = Guid.NewGuid();
		Guid? credentialId = login.CredentialId;
		if (credentialId is { } id && await _credentials.FindAsync(id) is { IsShared: false } credential)
		{
			CredentialSecretInput input;
			using (CredentialSecret secret = await _credentials.RevealAsync(id))
			{
				// The store's editor contract takes strings; the copy is handed straight back to be encrypted.
				input = new CredentialSecretInput
				{
					Password = secret.Password?.RevealString(),
					PrivateKey = secret.PrivateKey?.RevealString(),
					Passphrase = secret.Passphrase?.RevealString(),
				};
			}

			CredentialInfo saved = await _credentials.SaveAsync(credential with { Id = Guid.NewGuid(), OwnerConnectionId = copyId }, input);
			credentialId = saved.Id;
		}

		await _repository.SaveConnectionAsync(login with
		{
			Id = copyId,
			HostId = hostId,
			CredentialId = credentialId,
			IsFavorite = false,
			LastConnectedAt = null,
		});

		return copyId;
	}

	private async Task<bool> RunAsync(string what, Func<Task> action)
	{
		try
		{
			await action();
			return true;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogError(ex, "Could not {Operation}.", what);
			string message = ex is CredentialValidationException ? ex.Message : $"Could not {what}: {ex.Message}";
			_interaction.Notify(NoticeSeverity.Error, message);
			return false;
		}
	}
}
