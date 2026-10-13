using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Mokaterm.UI.Common.Interop;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// A row of tabs that switch the view below them: the Docker manager's container and project panels, the Plugins page.
/// The parent draws the view for <see cref="Active"/>; a panel can name its tab through <see cref="TabId"/>.
/// </summary>
public sealed partial class ViewTabs : ComponentBase
{
	private readonly Dictionary<string, ElementReference> _buttons = new(StringComparer.Ordinal);
	private string? _focus;

	/// <summary>Each tab's id and the text it shows.</summary>
	[Parameter, EditorRequired]
	public IReadOnlyList<(string Id, string Text)> Tabs { get; set; } = [];

	[Parameter]
	public string Active { get; set; } = "";

	[Parameter]
	public EventCallback<string> ActiveChanged { get; set; }

	[Parameter]
	public string Label { get; set; } = "Views";

	/// <summary>
	/// Prefixes the tab buttons' element ids, so a panel can point at its tab with <c>aria-labelledby</c>. Without it the
	/// buttons carry no id.
	/// </summary>
	[Parameter]
	public string? IdPrefix { get; set; }

	/// <summary>The element id of a tab's button, or null when no <see cref="IdPrefix"/> was given.</summary>
	public string? TabId(string id) => IdPrefix is null ? null : $"{IdPrefix}-{id}";

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (_focus is { } id && _buttons.TryGetValue(id, out ElementReference button))
		{
			_focus = null;
			try
			{
				await button.FocusAsync();
			}
			catch (Exception ex) when (JsModule.IsExpected(ex))
			{
				// The tab went away with the page; there is nothing to focus.
			}
		}
	}

	private Task SelectAsync(string id) => id == Active ? Task.CompletedTask : ActiveChanged.InvokeAsync(id);

	private Task OnKeyDownAsync(KeyboardEventArgs args, string id)
	{
		int index = Tabs.Select(tab => tab.Id).ToList().IndexOf(id);
		int next = args.Key switch
		{
			"ArrowRight" => (index + 1) % Tabs.Count,
			"ArrowLeft" => (index - 1 + Tabs.Count) % Tabs.Count,
			"Home" => 0,
			"End" => Tabs.Count - 1,
			_ => -1,
		};

		if (next < 0)
		{
			return Task.CompletedTask;
		}

		_focus = Tabs[next].Id;
		return SelectAsync(Tabs[next].Id);
	}
}
