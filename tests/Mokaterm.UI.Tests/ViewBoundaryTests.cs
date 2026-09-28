using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Presentation;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

public sealed class ViewBoundaryTests
{
	[Fact]
	public void Describe_MessageThatCarriesAJsStack_KeepsTheFirstLine() =>
		Assert.Equal(
			"Could not find 'mokaterm.attach'.",
			ViewBoundary.Describe(new InvalidOperationException(
				"Could not find 'mokaterm.attach'.\nError: Could not find 'mokaterm.attach'.\n    at https://localhost/_framework/blazor.web.js:1:734")));

	[Fact]
	public void Describe_BlankMessage_NamesTheExceptionInstead() =>
		Assert.Equal(nameof(InvalidOperationException), ViewBoundary.Describe(new InvalidOperationException("  ")));

	[Fact]
	public async Task Render_WithoutAFailure_AddsNothingAroundTheView()
	{
		string html = await RenderAsync(builder => builder.AddMarkupContent(0, "<p>terminal</p>"));

		Assert.Equal("<p>terminal</p>", html);
	}

	[Fact]
	public async Task Render_ViewThatThrows_ShowsTheErrorInItsPlace()
	{
		string html = await RenderAsync(Throwing("The stream is closed.\n   at Terminal.Attach()"), "This tab", "The connection may still be open.");

		Assert.Contains("This tab stopped working", html, StringComparison.Ordinal);
		Assert.Contains("The stream is closed.", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Terminal.Attach", html, StringComparison.Ordinal);
		Assert.Contains("The connection may still be open.", html, StringComparison.Ordinal);
		Assert.Contains("Try again", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ErrorMessage_IsTextNotMarkup()
	{
		string html = await RenderAsync(Throwing("<img src=x onerror=alert(1)>"));

		Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
		Assert.Contains("&lt;img", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_NotifyOnly_ReportsTheFailureAndPutsTheContentBack()
	{
		// An overlay whose first use fails, the way a menu command throws, and which works once it is recreated.
		UserInteractionService interaction = new();
		Starts starts = new();
		int cleanups = 0;
		RenderFragment content = builder =>
		{
			builder.OpenComponent<FailsFirstTime>(0);
			builder.AddComponentParameter(1, nameof(FailsFirstTime.Starts), starts);
			builder.CloseComponent();
		};

		string html = await StaticRender.RenderAsync<ViewBoundary>(
			services =>
			{
				services.AddMokaRed();
				services.AddSingleton<IUserInteraction>(interaction);
			},
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				[nameof(ViewBoundary.ChildContent)] = content,
				[nameof(ViewBoundary.Name)] = "A menu command",
				[nameof(ViewBoundary.NotifyOnly)] = true,
				[nameof(ViewBoundary.OnFailed)] = (Action)(() => cleanups++),
			});

		Assert.Equal("menu host", html);
		Assert.Equal(2, starts.Count);
		Assert.Equal(1, cleanups);
		PendingNotice notice = Assert.Single(interaction.TakeNotices());
		Assert.Equal(NoticeSeverity.Error, notice.Severity);
		Assert.Equal("A menu command stopped working", notice.Title);
		Assert.Equal("The item's action failed.", notice.Message);
	}

	private static RenderFragment Throwing(string message) => builder =>
	{
		builder.OpenComponent<Thrower>(0);
		builder.AddComponentParameter(1, nameof(Thrower.Message), message);
		builder.CloseComponent();
	};

	private static Task<string> RenderAsync(RenderFragment content, string name = "This view", string? hint = null) =>
		StaticRender.RenderAsync<ViewBoundary>(
			services =>
			{
				services.AddMokaRed();
				services.AddScoped<IUserInteraction, UserInteractionService>();
			},
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				[nameof(ViewBoundary.ChildContent)] = content,
				[nameof(ViewBoundary.Name)] = name,
				[nameof(ViewBoundary.Hint)] = hint,
			});

	private sealed class Starts
	{
		public int Count { get; set; }
	}

	/// <summary>Fails on its first start only; the copy the boundary creates afterwards renders.</summary>
	private sealed class FailsFirstTime : ComponentBase
	{
		[Parameter]
		public Starts Starts { get; set; } = new();

		protected override void OnInitialized()
		{
			if (Starts.Count++ == 0)
			{
				throw new InvalidOperationException("The item's action failed.");
			}
		}

		protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, "menu host");
	}

	/// <summary>A view that fails the moment it starts, like a terminal whose script did not load.</summary>
	private sealed class Thrower : ComponentBase
	{
		[Parameter]
		public string Message { get; set; } = "";

		protected override void OnInitialized() => throw new InvalidOperationException(Message);

		protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, "never shown");
	}
}
