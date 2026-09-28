using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Moka.Red.Core.Enums;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Security;
using Mokaterm.UI.Common.Commands;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.UI.Settings.Pages.Commands;

/// <summary>Every saved command: search, edit, delete, and where each one is offered.</summary>
public sealed partial class CommandsPage : VaultListPageBase<CommandSnippet>
{
	private Dictionary<Guid, string> _loginTitles = [];
	private CommandSnippet? _editing;
	private string? _editingHostLabel;
	private string? _editingConnectionLabel;
	private string _query = "";
	private bool _editorOpen;

	[Inject]
	private ICommandSnippetStore Store { get; set; } = default!;

	[Inject]
	private IConnectionRepository Connections { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private TimeProvider Time { get; set; } = default!;

	[Inject]
	private ILogger<CommandsPage> Logger { get; set; } = default!;

	protected override string LoadErrorMessage => "Saved commands could not be loaded.";

	// MokaTable notices a changed list by reference, so filtering builds a new one.
	private IReadOnlyList<CommandSnippet> Rows => CommandSnippetSearch.Filter(Items, _query);

	protected override void OnInitialized()
	{
		base.OnInitialized();
		Store.Changed += RequestReload;
	}

	protected override async ValueTask<IReadOnlyList<CommandSnippet>> LoadItemsAsync(CancellationToken cancellationToken)
	{
		IReadOnlyList<CommandSnippet> snippets = await Store.ListAsync(cancellationToken);
		ConnectionCatalog catalog = await Connections.GetCatalogAsync(cancellationToken);
		Dictionary<Guid, string> titles = [];
		foreach (ConnectionProfile login in catalog.Connections)
		{
			if (catalog.FindHost(login.HostId) is { } host)
			{
				titles[login.Id] = login.GetTitle(host);
			}
		}

		_loginTitles = titles;
		return [.. snippets.OrderByDescending(snippet => snippet.UseCount).ThenBy(snippet => snippet.Name, StringComparer.CurrentCultureIgnoreCase)];
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			Store.Changed -= RequestReload;
		}

		base.Dispose(disposing);
	}

	private static MokaColor ScopeColor(CommandSnippetScope scope) => scope switch
	{
		CommandSnippetScope.Connection => MokaColor.Primary,
		CommandSnippetScope.Host => MokaColor.Info,
		_ => MokaColor.Surface,
	};

	private string ScopeDetail(CommandSnippet snippet) => snippet.Scope switch
	{
		CommandSnippetScope.Connection => LoginTitle(snippet.ConnectionId),
		CommandSnippetScope.Host => snippet.Host ?? "unknown machine",
		_ => "every session",
	};

	private string LoginTitle(Guid? connectionId) =>
		connectionId is { } id && _loginTitles.TryGetValue(id, out string? title) ? title : "a login that no longer exists";

	private string UsedText(CommandSnippet snippet) => snippet.UseCount == 0
		? "never"
		: string.Create(CultureInfo.CurrentCulture, $"{snippet.UseCount}x {Relative(snippet.LastUsedAt)}");

	private static string UsedTitle(CommandSnippet snippet) =>
		snippet.LastUsedAt is { } lastUsed
			? $"Last used {DisplayFormat.Timestamp(lastUsed)}, saved {DisplayFormat.Timestamp(snippet.CreatedAt)}"
			: $"Saved {DisplayFormat.Timestamp(snippet.CreatedAt)}";

	private string Relative(DateTimeOffset? value) => value is { } moment ? DisplayFormat.Relative(moment, Time.GetUtcNow()) : "";

	private void OnQueryChanged(string query) => _query = query;

	private void AddCommand()
	{
		_editing = null;
		_editingHostLabel = null;
		_editingConnectionLabel = null;
		_editorOpen = true;
	}

	private void Edit(CommandSnippet snippet)
	{
		_editing = snippet;
		_editingHostLabel = snippet.Host;
		_editingConnectionLabel = snippet.ConnectionId is null ? null : LoginTitle(snippet.ConnectionId);
		_editorOpen = true;
	}

	private async Task DeleteAsync(CommandSnippet snippet)
	{
		bool confirmed = (await Interaction.ConfirmAsync(new ConfirmPrompt
		{
			Title = "Delete command",
			Message = $"Delete \"{snippet.Name}\"? It stops being offered in every session it applies to.",
			ConfirmText = "Delete",
			Destructive = true,
		})).Confirmed;

		if (!confirmed)
		{
			return;
		}

		try
		{
			await Store.DeleteAsync(snippet.Id);
			Interaction.Notify(NoticeSeverity.Success, $"Deleted \"{snippet.Name}\".");
		}
		catch (VaultLockedException)
		{
			Interaction.Notify(NoticeSeverity.Warning, VaultFailure.Locked("delete saved commands"));
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError(ex, "Deleting a saved command failed");
			Interaction.Notify(NoticeSeverity.Error, "The command could not be deleted.");
		}
	}
}
