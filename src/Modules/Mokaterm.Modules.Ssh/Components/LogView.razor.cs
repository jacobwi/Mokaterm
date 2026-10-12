using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Modules.Ssh.Tools;

namespace Mokaterm.Modules.Ssh.Components;

/// <summary>
/// The one log view of the session tools: stderr in its own colour, a merged log's sources in theirs, timestamps on
/// request, and a search that keeps only the lines that match and marks each match. At most <see cref="MaxShown"/>
/// lines are drawn, the newest ones, because every line is an element the circuit has to send and diff.
/// </summary>
/// <remarks>
/// The caller owns the reading: it fills the <see cref="Tail"/>, and when it offers <see cref="FollowingChanged"/> it
/// also polls while <see cref="Following"/> is on and appends what arrived. The view only draws and asks.
/// </remarks>
public sealed partial class LogView : ComponentBase
{
	public const int MaxShown = 3_000;

	/// <summary>How many hues a merged log's sources cycle through: the shell's eight host colours.</summary>
	public const int SourceHues = 8;

	private IReadOnlyList<LogLine> _shown = [];
	private int _hidden;
	private int _matching;
	private bool _showTimestamps = true;
	private string _query = "";
	private string _needle = "";
	private LogTail? _filteredTail;
	private int _filteredVersion = -1;
	private string? _filteredNeedle;

	[Parameter, EditorRequired]
	public LogTail Tail { get; set; } = default!;

	/// <summary>What the box says while it has no lines at all.</summary>
	[Parameter]
	public string EmptyText { get; set; } = "Nothing logged yet.";

	/// <summary>The box's accessible name, such as "Container log".</summary>
	[Parameter]
	public string Label { get; set; } = "Log";

	[Parameter]
	public string SearchPlaceholder { get; set; } = "Search the log";

	/// <summary>
	/// True to take the height left in a column that does not scroll itself, so the box scrolls on its own and stays at
	/// its newest line; false for a box inside a page that scrolls, which gets a fixed height instead.
	/// </summary>
	[Parameter]
	public bool Fill { get; set; } = true;

	/// <summary>The caller's own pickers at the start of the toolbar: a service, a unit, a file.</summary>
	[Parameter]
	public RenderFragment? Controls { get; set; }

	/// <summary>Between the toolbar and the lines: a load error, a note that the output was cut.</summary>
	[Parameter]
	public RenderFragment? Status { get; set; }

	/// <summary>How many lines from the end the caller can read; null or empty offers no choice.</summary>
	[Parameter]
	public IReadOnlyList<int>? LineChoices { get; set; }

	[Parameter]
	public int LineCount { get; set; }

	[Parameter]
	public EventCallback<int> LineCountChanged { get; set; }

	/// <summary>Whether the caller is polling for new lines. The follow button shows only with <see cref="FollowingChanged"/>.</summary>
	[Parameter]
	public bool Following { get; set; }

	[Parameter]
	public EventCallback<bool> FollowingChanged { get; set; }

	/// <summary>False while there is nothing to follow, such as a project with no compose to ask.</summary>
	[Parameter]
	public bool CanFollow { get; set; } = true;

	/// <summary>The follow button's tooltip while it is off, which says how often it polls.</summary>
	[Parameter]
	public string FollowTitle { get; set; } = "Fetch new lines every few seconds";

	/// <summary>Reads the tail again; the refresh button shows only when set.</summary>
	[Parameter]
	public EventCallback OnRefresh { get; set; }

	[Parameter]
	public bool RefreshDisabled { get; set; }

	[Parameter]
	public string RefreshTitle { get; set; } = "Fetch the tail again";

	/// <summary>
	/// Which of the <see cref="SourceHues"/> colours a merged log's source is drawn in, or null for the plain one. A
	/// project gives each service its own, so a container reads in its service's colour.
	/// </summary>
	[Parameter]
	public Func<string, int?>? SourceHue { get; set; }

	private string CountText => Describe(Tail.Lines.Count, _matching, _needle.Length > 0, Following);

	private string HiddenText => string.Create(
		CultureInfo.CurrentCulture,
		$"{_hidden:N0} older {(_hidden == 1 ? "line is" : "lines are")} not drawn; narrow the search to see them.");

	/// <summary>The lines that match <paramref name="needle"/>, case aside; every line when it is empty.</summary>
	internal static List<LogLine> Matching(IReadOnlyList<LogLine> lines, string needle) =>
		needle.Length == 0
			? [.. lines]
			: [.. lines.Where(line => line.Text.Contains(needle, StringComparison.OrdinalIgnoreCase))];

	/// <summary>The text cut into what matches the search and what does not, in order.</summary>
	internal static IEnumerable<(string Text, bool Match)> Pieces(string text, string needle)
	{
		if (needle.Length == 0)
		{
			yield return (text, false);
			yield break;
		}

		int start = 0;
		while (start < text.Length)
		{
			int found = text.IndexOf(needle, start, StringComparison.OrdinalIgnoreCase);
			if (found < 0)
			{
				yield return (text[start..], false);
				yield break;
			}

			if (found > start)
			{
				yield return (text[start..found], false);
			}

			yield return (text.Substring(found, needle.Length), true);
			start = found + needle.Length;
		}
	}

	/// <summary>
	/// <c>2024-01-02T03:04:05.123456789Z</c> as <c>01-02 03:04:05.123</c> and <c>2024-01-02T03:04:05+0100</c> as
	/// <c>01-02 03:04:05</c>: enough to read, short enough to scan.
	/// </summary>
	internal static string ShortTime(string timestamp) =>
		timestamp.Length >= 23 && timestamp[19] == '.' ? $"{timestamp[5..10]} {timestamp[11..23]}"
		: timestamp.Length >= 19 ? $"{timestamp[5..10]} {timestamp[11..19]}"
		: timestamp;

	/// <summary>The line above the box: how many lines, how many match, and whether they are being followed.</summary>
	internal static string Describe(int total, int matching, bool searching, bool following)
	{
		string text = string.Create(CultureInfo.CurrentCulture, $"{total:N0} {(total == 1 ? "line" : "lines")}");
		if (searching)
		{
			text = string.Create(CultureInfo.CurrentCulture, $"{matching:N0} of {text} match");
		}

		return following ? text + ", following" : text;
	}

	protected override void OnParametersSet()
	{
		ArgumentNullException.ThrowIfNull(Tail);
		Filter();
	}

	/// <summary>Filters again only when the lines or the search changed: a parent renders far more often than a log does.</summary>
	private void Filter()
	{
		if (ReferenceEquals(_filteredTail, Tail) && _filteredVersion == Tail.Version && _filteredNeedle == _needle)
		{
			return;
		}

		List<LogLine> matching = Matching(Tail.Lines, _needle);
		_matching = matching.Count;
		_hidden = Math.Max(0, matching.Count - MaxShown);
		_shown = _hidden > 0 ? matching.GetRange(_hidden, MaxShown) : matching;
		_filteredTail = Tail;
		_filteredVersion = Tail.Version;
		_filteredNeedle = _needle;
	}

	private void OnQueryChanged(string value)
	{
		_query = value ?? "";
		_needle = _query.Trim();
		Filter();
	}

	private void OnShowTimestampsChanged(bool value) => _showTimestamps = value;

	private Task OnLineCountChangedAsync(string? value) =>
		int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) && LineChoices?.Contains(count) == true
			? LineCountChanged.InvokeAsync(count)
			: Task.CompletedTask;

	private Task ToggleFollowingAsync() => FollowingChanged.InvokeAsync(!Following);

	private string SourceCss(string source) =>
		SourceHue?.Invoke(source) is { } hue
			? string.Create(CultureInfo.InvariantCulture, $"mt-log-source mt-log-src-{((hue % SourceHues) + SourceHues) % SourceHues}")
			: "mt-log-source";
}
