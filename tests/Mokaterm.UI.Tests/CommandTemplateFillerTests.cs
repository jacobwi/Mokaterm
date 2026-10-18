using Mokaterm.Abstractions.Commands;
using Mokaterm.UI.Commands;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The one question every typed or run command with placeholders asks: through the window's prompt queue, started
/// from the last answers for that command, and forgotten with the vault.
/// </summary>
public sealed class CommandTemplateFillerTests
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task NoPlaceholders_AsksNothing_AndReadsTheEscape()
	{
		using UserInteractionService prompts = new();
		using CommandTemplateFiller filler = new(prompts, new LockableVault());

		string? command = await filler.FillAsync(CommandTemplate.Parse("echo {{{{x}} --format '{{.Names}}'"), "Type", Ct).WaitAsync(Patience, Ct);

		Assert.Equal("echo {{x}} --format '{{.Names}}'", command);
		Assert.Null(prompts.Current);
	}

	[Fact]
	public async Task Placeholders_AreAskedForOnce_AndFilledWithWhatWasTyped()
	{
		using UserInteractionService prompts = new();
		using CommandTemplateFiller filler = new(prompts, new LockableVault());

		Task<string?> filling = filler.FillAsync(CommandTemplate.Parse("ssh -p {{port:22}} {{host}} 'ls {{host}}'"), "Run on 2 logins", Ct);
		PlaceholderRequest request = await AskedAsync(prompts);

		Assert.Equal(["port", "host"], request.Prompt.Template.Placeholders.Select(placeholder => placeholder.Name));
		Assert.Empty(request.Prompt.Values);
		Assert.Equal("Run on 2 logins", request.Prompt.ConfirmText);

		prompts.Complete(request, Values(("port", "2222"), ("host", "db 1")));

		Assert.Equal("ssh -p 2222 db 1 'ls db 1'", await filling.WaitAsync(Patience, Ct));
		Assert.Null(prompts.Current);
	}

	[Fact]
	public async Task Cancel_GivesNothingToType()
	{
		using UserInteractionService prompts = new();
		using CommandTemplateFiller filler = new(prompts, new LockableVault());

		Task<string?> filling = filler.FillAsync(CommandTemplate.Parse("rm -r {{dir}}"), "Run", Ct);
		prompts.Complete(await AskedAsync(prompts), null);

		Assert.Null(await filling.WaitAsync(Patience, Ct));
	}

	[Fact]
	public async Task AskingAgain_StartsFromTheLastAnswerForThatCommand_Only()
	{
		using UserInteractionService prompts = new();
		using CommandTemplateFiller filler = new(prompts, new LockableVault());
		CommandTemplate tail = CommandTemplate.Parse("tail -n {{lines:50}} {{file}}");

		Task<string?> first = filler.FillAsync(tail, "Type", Ct);
		prompts.Complete(await AskedAsync(prompts), Values(("lines", "200"), ("file", "/var/log/syslog")));
		await first.WaitAsync(Patience, Ct);

		Task<string?> again = filler.FillAsync(tail, "Type", Ct);
		PlaceholderRequest second = await AskedAsync(prompts);
		Assert.Equal("200", second.Prompt.Values["lines"]);
		Assert.Equal("/var/log/syslog", second.Prompt.Values["file"]);
		prompts.Complete(second, null);
		await again.WaitAsync(Patience, Ct);

		Task<string?> other = filler.FillAsync(CommandTemplate.Parse("head -n {{lines:50}} {{file}}"), "Type", Ct);
		PlaceholderRequest third = await AskedAsync(prompts);
		Assert.Empty(third.Prompt.Values);
		prompts.Complete(third, null);
		await other.WaitAsync(Patience, Ct);
	}

	[Fact]
	public async Task LockingTheVault_CancelsTheQuestion_AndForgetsEveryAnswer()
	{
		using UserInteractionService prompts = new();
		LockableVault vault = new();
		using CommandTemplateFiller filler = new(prompts, vault);
		CommandTemplate login = CommandTemplate.Parse("mysql -u {{user}} -p{{password}}");

		Task<string?> first = filler.FillAsync(login, "Type", Ct);
		prompts.Complete(await AskedAsync(prompts), Values(("user", "root"), ("password", "hunter2")));
		await first.WaitAsync(Patience, Ct);

		Task<string?> open = filler.FillAsync(login, "Type", Ct);
		await AskedAsync(prompts);
		vault.Lock();

		Assert.Null(await open.WaitAsync(Patience, Ct));
		Assert.Null(prompts.Current);

		Task<string?> afterLock = filler.FillAsync(login, "Type", Ct);
		PlaceholderRequest fresh = await AskedAsync(prompts);
		Assert.Empty(fresh.Prompt.Values);
		prompts.Complete(fresh, null);
		await afterLock.WaitAsync(Patience, Ct);
	}

	[Fact]
	public async Task ThePromptNeverPrintsWhatItStartsFrom()
	{
		using UserInteractionService prompts = new();
		using CommandTemplateFiller filler = new(prompts, new LockableVault());
		CommandTemplate login = CommandTemplate.Parse("curl -u admin:{{password}} api");

		Task<string?> first = filler.FillAsync(login, "Run", Ct);
		prompts.Complete(await AskedAsync(prompts), Values(("password", "hunter2")));
		await first.WaitAsync(Patience, Ct);

		Task<string?> again = filler.FillAsync(login, "Run", Ct);
		PlaceholderRequest request = await AskedAsync(prompts);
		Assert.DoesNotContain("hunter2", request.Prompt.ToString(), StringComparison.Ordinal);
		prompts.Complete(request, null);
		await again.WaitAsync(Patience, Ct);
	}

	private static async Task<PlaceholderRequest> AskedAsync(UserInteractionService prompts)
	{
		await Eventually.TrueAsync(() => prompts.Current is PlaceholderRequest, "the placeholder question is queued");
		return (PlaceholderRequest)prompts.Current!;
	}

	private static Dictionary<string, string> Values(params (string Name, string Value)[] values) =>
		values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);
}
