using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// The registries this login's docker is logged in to, from its client config (root's while the manager runs through
/// sudo, which the chips say), with where each one's credentials are kept, and the logins saved in the vault for this
/// login. Log in runs <c>docker login --password-stdin</c> with the password on stdin; log out is confirmed first. No
/// credential is ever read back: the config's <c>auth</c> values are blanked on the server and dropped here.
/// </summary>
public sealed partial class DockerRegistriesPage : DockerManagerPageBase
{
	private DockerClientConfig? _config;
	private IReadOnlyList<DockerSavedLogin> _saved = [];
	private List<RegistryRow> _rows = [];
	private string _query = "";
	private string? _selected;
	private string? _savedProblem;
	private string? _warning;
	private bool _loggingIn;
	private string _registry = "";
	private string _user = "";
	private string _password = "";
	private string? _registryError;
	private string? _userError;
	private string? _passwordError;
	private bool _save;

	[Inject]
	private ICredentialStore Credentials { get; set; } = default!;

	private DockerSavedLogins SavedLogins => new(Credentials);

	/// <summary>A registry login is kept with a saved login; a quick connection has none to keep it with.</summary>
	private bool CanSave => !Session.IsTransient;

	private IReadOnlyList<RegistryRow> Shown => _query.Trim() is { Length: > 0 } needle
		? [.. _rows.Where(row => row.Display.Contains(needle, StringComparison.OrdinalIgnoreCase) || row.Key.Contains(needle, StringComparison.OrdinalIgnoreCase))]
		: _rows;

	private RegistryRow? SelectedRow => _selected is { } host ? _rows.FirstOrDefault(row => row.Host == host) : null;

	private string? Summary => _config is { } config
		? string.Create(
			CultureInfo.CurrentCulture,
			$"{DockerRegistryWording.Whose(config)}: {config.Path}, logged in to {config.Registries.Count(entry => entry.LoggedIn)} of {_rows.Count}")
		: null;

	protected override Task OnInitializedAsync() => RefreshAsync();

	private static string RowState(RegistryRow row) => row.Entry switch
	{
		{ LoggedIn: true, Helper: { } helper } => $"Logged in, kept by docker-credential-{helper}",
		{ LoggedIn: true, StoredInFile: true } => "Logged in, stored in config.json",
		{ LoggedIn: true } => "Logged in",
		{ Helper: { } helper } => $"Credentials come from docker-credential-{helper}",
		_ => "Not logged in",
	};

	private Task RefreshAsync() => LoadAsync(async token =>
	{
		if (Feature is not { } feature)
		{
			return;
		}

		_config = await feature.ReadClientConfigAsync(token);
		_savedProblem = null;
		if (CanSave)
		{
			try
			{
				_saved = await SavedLogins.ListAsync(Session.Connection.Id, token);
			}
			catch (VaultLockedException)
			{
				_saved = [];
				_savedProblem = "The vault is locked, so the saved registry logins are not listed.";
			}
		}

		_rows = Rows(_config, _saved);
	});

	/// <summary>The registries of the config and of the vault, one row per host, the config's order first.</summary>
	private static List<RegistryRow> Rows(DockerClientConfig config, IReadOnlyList<DockerSavedLogin> saved)
	{
		List<RegistryRow> rows = [];
		foreach (DockerRegistryEntry entry in config.Registries)
		{
			if (rows.Any(row => row.Host == entry.Host))
			{
				continue;
			}

			// Two keys can name one host (https://ghcr.io and ghcr.io); the one docker is logged in under wins.
			DockerRegistryEntry best = config.Find(entry.Key) ?? entry;
			rows.Add(new RegistryRow(best.Key, entry.Host, entry.Display, best, saved.FirstOrDefault(login => login.Host == entry.Host)));
		}

		foreach (DockerSavedLogin login in saved)
		{
			if (!rows.Any(row => row.Host == login.Host))
			{
				rows.Add(new RegistryRow(login.Registry, login.Host, login.Display, null, login));
			}
		}

		return rows;
	}

	private void OnQueryChanged(string value) => _query = value ?? "";

	private void Select(RegistryRow row)
	{
		_selected = row.Host;
		_loggingIn = false;
	}

	private void CloseDetail()
	{
		_selected = null;
		_loggingIn = false;
		_password = "";
	}

	/// <summary>Opens the login form, filled in with the row's registry and saved user when there is a row.</summary>
	private void StartLogin(RegistryRow? row)
	{
		_loggingIn = true;
		_registry = row is null || DockerRegistryNames.IsDockerHub(row.Key) ? "" : row.Key;
		_user = row?.Saved?.User ?? "";
		_password = "";
		_registryError = _userError = _passwordError = null;
		_save = false;
	}

	private async Task LoginAsync()
	{
		if (IsWorking)
		{
			return;
		}

		string? registry;
		try
		{
			registry = DockerRegistryNames.ForLogin(_registry);
		}
		catch (ArgumentException ex)
		{
			_registryError = ex.Message;
			return;
		}

		string user = _user.Trim();
		_userError = DockerNames.RegistryUserProblem(user);
		_passwordError = _password.Length == 0 ? "Type the password or token." : null;
		if (_userError is not null || _passwordError is not null)
		{
			return;
		}

		using SecretBuffer password = SecretBuffer.FromString(_password);
		if (DockerRegistryCommands.PasswordProblem(password) is { } problem)
		{
			_passwordError = problem;
			return;
		}

		DockerOutput? output = await RunLoginAsync(registry, user, password);
		if (output is not { Succeeded: true })
		{
			return;
		}

		if (_save && CanSave)
		{
			await SaveAsync(registry, user, password);
		}

		// The field's string cannot be wiped; dropping it is what is left to do.
		_password = "";
		_loggingIn = false;
		_selected = DockerRegistryNames.Host(registry ?? DockerNames.DockerHubRegistry);
		await RefreshAsync();
	}

	/// <summary>Logs in with a login kept in the vault: one click, the password straight from the vault to docker's stdin.</summary>
	private async Task LoginSavedAsync(DockerSavedLogin saved)
	{
		if (IsWorking)
		{
			return;
		}

		SecretBuffer password;
		try
		{
			password = await SavedLogins.RevealAsync(saved, Lifetime);
		}
		catch (Exception ex) when (ex is KeyNotFoundException or VaultLockedException)
		{
			Interaction.Notify(NoticeSeverity.Warning, ex is VaultLockedException ? "Unlock the vault to use the saved login." : "The saved login is gone.");
			return;
		}
		catch (OperationCanceledException)
		{
			return;
		}

		using (password)
		{
			string? registry = DockerRegistryNames.IsDockerHub(saved.Registry) ? null : saved.Registry;
			if (await RunLoginAsync(registry, saved.User, password) is { Succeeded: true })
			{
				await RefreshAsync();
			}
		}
	}

	/// <summary>Runs the login and keeps what docker said about storing the password unencrypted, as docker said it.</summary>
	private async Task<DockerOutput?> RunLoginAsync(string? registry, string user, SecretBuffer password)
	{
		DockerCall call;
		try
		{
			call = DockerRegistryCommands.Login(registry, user, password);
		}
		catch (ArgumentException ex)
		{
			_userError = ex.Message;
			return null;
		}

		string name = DockerRegistryNames.Display(registry ?? DockerNames.DockerHubRegistry);
		DockerOutput? output = await RunChangeAsync(call, $"Log in to {name} as {user}", destructive: false);
		if (output is { Succeeded: true })
		{
			_warning = DockerRegistryCommands.StorageWarning(output) is { Length: > 0 } warning ? warning : null;
		}

		return output;
	}

	private async Task SaveAsync(string? registry, string user, SecretBuffer password)
	{
		try
		{
			await SavedLogins.SaveAsync(Session.Connection.Id, registry, user, password, Lifetime);
		}
		catch (Exception ex) when (ex is VaultLockedException or CredentialValidationException)
		{
			Interaction.Notify(NoticeSeverity.Warning, $"Logged in, but the login could not be saved in the vault: {ex.Message}");
		}
	}

	private async Task LogoutAsync(RegistryRow row)
	{
		DockerCall call;
		try
		{
			call = DockerRegistryCommands.Logout(row.Key);
		}
		catch (ArgumentException ex)
		{
			Interaction.Notify(NoticeSeverity.Warning, ex.Message, "Cannot log out");
			return;
		}

		string where = row.Entry?.Helper is { } helper ? $"from docker-credential-{helper}"
			: _config?.CredentialsStore is { } store ? $"from docker-credential-{store}"
			: "from config.json";
		string whose = _config is { } config ? DockerRegistryWording.Whose(config) : "docker";
		string saved = row.Saved is null ? "" : " The login saved in the vault stays, so logging in again is one click.";
		await RunChangeAsync(
			call,
			$"Log out of {row.Display}",
			$"{whose} removes its credentials for {row.Display} {where}. Pulls and pushes that need a login fail until it logs in again.{saved}",
			"Log out",
			RefreshAsync,
			destructive: true);
	}

	private async Task ForgetAsync(DockerSavedLogin login)
	{
		ConfirmResult answer = await Interaction.ConfirmAsync(
			new ConfirmPrompt
			{
				Title = $"Forget the saved login to {login.Display}",
				Message = $"The password saved in the vault for {login.User} at {login.Display} is deleted. docker's own login on the server is left as it is.",
				ConfirmText = "Forget",
				Destructive = true,
			},
			Lifetime);
		if (!answer.Confirmed || IsDisposed)
		{
			return;
		}

		try
		{
			await SavedLogins.ForgetAsync(login, Lifetime);
		}
		catch (VaultLockedException)
		{
			Interaction.Notify(NoticeSeverity.Warning, "Unlock the vault to forget the saved login.");
			return;
		}

		await RefreshAsync();
	}

	/// <summary>One registry on the page: what the client config says of it, and the login the vault keeps for it.</summary>
	private sealed record RegistryRow(string Key, string Host, string Display, DockerRegistryEntry? Entry, DockerSavedLogin? Saved);
}
