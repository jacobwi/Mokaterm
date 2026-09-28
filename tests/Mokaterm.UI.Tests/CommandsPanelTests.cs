using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Storage;
using Mokaterm.UI.Commands;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

public sealed partial class CommandsPanelTests
{
	[Fact]
	public async Task Render_StoreFails_SaysSoInsteadOfClaimingThereAreNoCommands()
	{
		string html = await RenderAsync(new BrokenSnippetStore());

		Assert.Contains("The saved commands could not be loaded.", html, StringComparison.Ordinal);
		Assert.DoesNotContain("No commands yet", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_SavedCommands_RowsStayOutOfTheTabOrder()
	{
		string html = await RenderAsync(new FixedSnippetStore([Snippet("uptime"), Snippet("df -h")]));

		// The search box moves through the rows. Moka.Red makes a clickable list row a tab stop, so without the
		// panel's own tabindex every saved command would be one more stop between the search box and Manage.
		Assert.Equal(2, RowOutOfTabOrder().Count(html));
		Assert.DoesNotMatch(RowTabStop(), html);
	}

	private static Task<string> RenderAsync(ICommandSnippetStore store) =>
		StaticRender.RenderAsync<CommandsPanel>(
			services =>
			{
				services.AddMokaRed();
				services.AddSingleton(TimeProvider.System);
				services.AddSingleton<IAppDataStore, MemoryAppDataStore>();
				services.AddSingleton(store);
				services.AddScoped<IUserInteraction, UserInteractionService>();
				services.AddScoped<UiStateStore>();
				services.AddScoped<ShellState>();
				services.AddScoped<ShellInterop>();
				services.AddScoped<FormInterop>();

				// A saved command also types into the sessions that type together, so the actions need that group.
				services.AddScoped<ISessionManager, ManualSessionManager>();
				services.AddScoped<SessionWorkspace>();
				services.AddScoped<InputBroadcast>();
				services.AddScoped<CommandSnippetActions>();
			},
			new Dictionary<string, object?>(StringComparer.Ordinal) { [nameof(CommandsPanel.Session)] = new FakeSession() });

	private static CommandSnippet Snippet(string command) => new()
	{
		Id = Guid.NewGuid(),
		Name = command,
		Command = command,
		Scope = CommandSnippetScope.Global,
	};

	[GeneratedRegex("<div class=\"moka-list-item[^\"]*\"[^>]*tabindex=\"-1\"")]
	private static partial Regex RowOutOfTabOrder();

	[GeneratedRegex("<div class=\"moka-list-item[^\"]*\"[^>]*tabindex=\"0\"")]
	private static partial Regex RowTabStop();

	/// <summary>A store that offers the same commands to every session.</summary>
	private sealed class FixedSnippetStore(IReadOnlyList<CommandSnippet> snippets) : ICommandSnippetStore
	{
		public event Action? Changed
		{
			add { }
			remove { }
		}

		public ValueTask<IReadOnlyList<CommandSnippet>> ListAsync(CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(snippets);

		public ValueTask<IReadOnlyList<CommandSnippet>> QueryAsync(CommandSnippetTarget target, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(snippets);

		public Task<CommandSnippet> SaveAsync(CommandSnippet snippet, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public Task DeleteAsync(Guid snippetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task MarkUsedAsync(Guid snippetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
	}

	/// <summary>A store whose vault document cannot be read, the way a damaged or half-written file fails.</summary>
	private sealed class BrokenSnippetStore : ICommandSnippetStore
	{
		public event Action? Changed
		{
			add { }
			remove { }
		}

		public ValueTask<IReadOnlyList<CommandSnippet>> ListAsync(CancellationToken cancellationToken = default) =>
			ValueTask.FromException<IReadOnlyList<CommandSnippet>>(new IOException("The commands document could not be read."));

		public ValueTask<IReadOnlyList<CommandSnippet>> QueryAsync(CommandSnippetTarget target, CancellationToken cancellationToken = default) =>
			ValueTask.FromException<IReadOnlyList<CommandSnippet>>(new IOException("The commands document could not be read."));

		public Task<CommandSnippet> SaveAsync(CommandSnippet snippet, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public Task DeleteAsync(Guid snippetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task MarkUsedAsync(Guid snippetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
	}
}
