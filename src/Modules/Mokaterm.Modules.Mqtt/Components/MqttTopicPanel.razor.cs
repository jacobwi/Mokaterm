using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Mokaterm.Modules.Mqtt.Components;

/// <summary>
/// The topic tree, flattened into rows by the store. Rows carry their own click and keys rather than going through
/// a Moka list, because a row is both a selection and an expander and Enter must only ever do one of them.
/// </summary>
public sealed partial class MqttTopicPanel : ComponentBase
{
	/// <summary>Pixels a level of nesting adds. Small: MQTT topics get deep.</summary>
	private const int IndentPixels = 10;

	[Parameter, EditorRequired]
	public IReadOnlyList<MqttTopicRow> Rows { get; set; } = [];

	[Parameter]
	public string? SelectedTopic { get; set; }

	[Parameter]
	public EventCallback<string> SelectedTopicChanged { get; set; }

	/// <summary>Raised when a row's expander was clicked. The parent owns which topics are open.</summary>
	[Parameter]
	public EventCallback<string> OnToggle { get; set; }

	[Parameter]
	public string Search { get; set; } = "";

	[Parameter]
	public EventCallback<string> SearchChanged { get; set; }

	/// <summary>Whether the session has received anything at all, which decides what an empty tree says.</summary>
	[Parameter]
	public bool HasMessages { get; set; }

	private string EmptyText => Search.Length > 0
		? "No topic matches that."
		: HasMessages
			? "Nothing kept. The caps in Settings, MQTT may be smaller than this broker's traffic."
			: "Nothing yet. A broker sends only what a subscription asks for.";

	private string RowClass(MqttTopicRow row) => row.Path == SelectedTopic
		? "mt-mqtt-topics-row mt-mqtt-topics-row--selected"
		: "mt-mqtt-topics-row";

	// Null leaves the attribute off, which is what a row with nothing under it should say.
	private static string? AriaExpanded(MqttTopicRow row) => row.HasChildren ? row.IsExpanded ? "true" : "false" : null;

	private static string IndentStyle(MqttTopicRow row) =>
		string.Create(CultureInfo.InvariantCulture, $"width:{row.Depth * IndentPixels}px");

	/// <summary>The count a row shows: its own messages, or everything below it for a level that carries none.</summary>
	private static string CountText(MqttTopicRow row) => row.Received > 0
		? row.Received.ToString(CultureInfo.CurrentCulture)
		: row.ReceivedWithChildren > 0
			? string.Create(CultureInfo.CurrentCulture, $"({row.ReceivedWithChildren})")
			: "";

	private static string CountTitle(MqttTopicRow row) => row.Received > 0
		? string.Create(CultureInfo.CurrentCulture, $"{row.Received} messages on this topic, {row.Kept} kept")
		: string.Create(CultureInfo.CurrentCulture, $"{row.ReceivedWithChildren} messages under this level");

	private Task OnRowClick(MqttTopicRow row) => SelectAsync(row);

	private Task OnRowKeyDown(KeyboardEventArgs args, MqttTopicRow row) => args.Key switch
	{
		"Enter" or " " => SelectAsync(row),
		"ArrowRight" when row.HasChildren && !row.IsExpanded => OnToggle.InvokeAsync(row.Path),
		"ArrowLeft" when row.HasChildren && row.IsExpanded => OnToggle.InvokeAsync(row.Path),
		_ => Task.CompletedTask,
	};

	/// <summary>
	/// A click selects the topic, and on a level with children it opens or closes it as well: an interior level of a
	/// topic is often a topic in its own right, so neither action alone would do.
	/// </summary>
	private async Task SelectAsync(MqttTopicRow row)
	{
		await SelectedTopicChanged.InvokeAsync(row.Path);
		if (row.HasChildren)
		{
			await OnToggle.InvokeAsync(row.Path);
		}
	}

	private Task OnSearchChanged(string value) => SearchChanged.InvokeAsync(value);
}
