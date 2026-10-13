using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.QuickConnect;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The rows under the quick connect box as a screen reader and the box see them: a listbox of options with ids the box
/// can point at, each named for what it is, none of them a tab stop, and the remove button out of the tree.
/// </summary>
public sealed partial class QuickConnectListTests
{
	private const string ListId = "qc-list";

	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

	private static readonly HostProfile Web = new()
	{
		Id = Guid.NewGuid(),
		Name = "web-prod",
		Address = "10.0.0.4",
		Environment = HostEnvironment.Production,
	};

	private static readonly ConnectionProfile WebRoot = new() { Id = Guid.NewGuid(), HostId = Web.Id, ProtocolId = "ssh", Username = "root" };

	private static readonly QuickConnectRecent Pi = new()
	{
		ProtocolId = "ssh",
		Username = "pi",
		Address = "raspberrypi.local",
		Port = 2222,
		ConnectedAt = DateTimeOffset.UnixEpoch,
	};

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Render_IsAListboxOfNamedOptions()
	{
		string html = await RenderAsync(activeIndex: 1);

		Assert.Matches("<div[^>]*class=\"mt-qc-list\"[^>]*id=\"qc-list\"[^>]*role=\"listbox\"[^>]*aria-label=\"Quick connect suggestions\"", html);
		Assert.Equal(
			[
				("qc-list-0", "false", "web-prod, root@10.0.0.4, saved SSH login"),
				("qc-list-1", "true", "pi@raspberrypi.local:2222, recent SSH connection"),
				("qc-list-2", "false", "COM3, USB adapter, Serial"),
				("qc-list-3", "false", "Connect and save as a login"),
				("qc-list-4", "false", "Clear history"),
			],
			Options(html));
	}

	// The box keeps the focus: no row is a tab stop, and the mouse-only remove button is hidden from the tree. Its name
	// and key are in Moka's tooltip, and its empty title keeps the row's own title from showing over that.
	[Fact]
	public async Task Render_NothingInTheListTakesTheFocus()
	{
		const string tooltip = "Remove from history (Shift+Delete)";
		string html = await RenderAsync(activeIndex: -1);

		Assert.DoesNotMatch("tabindex=\"0\"", html);
		Match remove = Assert.Single(RemoveButton().Matches(html));
		Assert.Contains("aria-hidden=\"true\"", remove.Value, StringComparison.Ordinal);
		Assert.Contains("tabindex=\"-1\"", remove.Value, StringComparison.Ordinal);
		Assert.Contains("title=\"\"", remove.Value, StringComparison.Ordinal);
		Assert.Contains(remove.Value, Tooltips.ControlFor(html, tooltip), StringComparison.Ordinal);
		Assert.Equal("bottom-end", Tooltips.PositionOf(html, tooltip));
	}

	[Fact]
	public async Task Render_ShowsWhatEachRowIs()
	{
		string html = WebUtility.HtmlDecode(await RenderAsync(activeIndex: 0));

		// The machine is drawn the way the tabs draw it, with the production warning beside it.
		Assert.Contains("mt-label-host", html, StringComparison.Ordinal);
		Assert.Contains("PRODUCTION", html, StringComparison.Ordinal);
		Assert.Contains(":2222", html, StringComparison.Ordinal);
		Assert.Contains("USB adapter", html, StringComparison.Ordinal);
		Assert.Contains("Connect and save...", html, StringComparison.Ordinal);
		Assert.Matches("<kbd[^>]*>Ctrl</kbd>[\\s\\S]*<kbd[^>]*>Enter</kbd>", html);
		Assert.Contains("mt-qc-row mt-qc-row--active", html, StringComparison.Ordinal);
	}

	private static List<(string Id, string Selected, string Name)> Options(string html) =>
	[
		.. Option().Matches(html).Select(match => (
			match.Groups["id"].Value,
			match.Groups["selected"].Value,
			WebUtility.HtmlDecode(match.Groups["name"].Value))),
	];

	private static Task<string> RenderAsync(int activeIndex)
	{
		IReadOnlyList<QuickConnectRow> rows =
		[
			QuickConnectRow.ForLogin(Web, WebRoot, QuickConnectProtocols.Ssh, QuickConnectRank.Prefix),
			QuickConnectRow.ForRecent(Pi, null, QuickConnectRank.Prefix),
			QuickConnectRow.ForDetected("serial", new QuickConnectSuggestion("COM3", "USB adapter"), QuickConnectRank.Prefix),
			QuickConnectRow.ConnectAndSave(),
			QuickConnectRow.ClearHistory(),
		];

		return StaticRender.RenderAsync<QuickConnectList>(
			services =>
			{
				services.AddMokaRed();
				services.AddMokatermUiCommon();
				services.AddSingleton<IProtocolRegistry, QuickConnectProtocols>();
			},
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				[nameof(QuickConnectList.Id)] = ListId,
				[nameof(QuickConnectList.Rows)] = rows,
				[nameof(QuickConnectList.ActiveIndex)] = activeIndex,
			}).WaitAsync(Patience, Ct);
	}

	[GeneratedRegex("<div[^>]*id=\"(?<id>qc-list-\\d+)\"[^>]*role=\"option\"[^>]*aria-selected=\"(?<selected>true|false)\"[^>]*aria-label=\"(?<name>[^\"]*)\"")]
	private static partial Regex Option();

	[GeneratedRegex("<button[^>]*class=\"mt-qc-remove\"[^>]*>")]
	private static partial Regex RemoveButton();
}
