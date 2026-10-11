using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// Log lines in a monospace box: stderr in its own colour, timestamps optional, and a search that keeps only the lines
/// that match and marks each match. At most <see cref="MaxShown"/> lines are drawn, the newest ones, because every line
/// is an element the circuit has to send and diff.
/// </summary>
public sealed partial class DockerLogView : ComponentBase
{
	public const int MaxShown = 3_000;

	private IReadOnlyList<DockerLogLine> _shown = [];
	private int _hidden;

	[Parameter, EditorRequired]
	public IReadOnlyList<DockerLogLine> Lines { get; set; } = [];

	/// <summary>Text to look for, case-insensitively; empty shows every line.</summary>
	[Parameter]
	public string Query { get; set; } = "";

	[Parameter]
	public bool ShowTimestamps { get; set; }

	/// <summary>What the box says while it has no lines at all.</summary>
	[Parameter]
	public string EmptyText { get; set; } = "Nothing logged yet.";

	[Parameter]
	public string Label { get; set; } = "Log";

	/// <summary>
	/// True to take the height left in a column that does not scroll itself, so the box scrolls on its own and stays at
	/// its newest line; false for a box inside a page that scrolls, which gets a fixed height instead.
	/// </summary>
	[Parameter]
	public bool Fill { get; set; } = true;

	/// <summary>The colour class for a merged log's source (its container), so each service reads in its own colour.</summary>
	[Parameter]
	public Func<string, string?>? SourceClass { get; set; }

	private IReadOnlyList<DockerLogLine> Shown => _shown;

	private int Hidden => _hidden;

	private string HiddenText => string.Create(
		CultureInfo.CurrentCulture,
		$"{_hidden:N0} older {(_hidden == 1 ? "line is" : "lines are")} not drawn; narrow the search to see them.");

	private string Needle => Query.Trim();

	protected override void OnParametersSet()
	{
		string needle = Needle;
		List<DockerLogLine> matching = needle.Length == 0
			? [.. Lines]
			: [.. Lines.Where(line => line.Text.Contains(needle, StringComparison.OrdinalIgnoreCase))];

		_hidden = Math.Max(0, matching.Count - MaxShown);
		_shown = _hidden > 0 ? matching.GetRange(_hidden, MaxShown) : matching;
	}

	/// <summary><c>2024-01-02T03:04:05.123456789Z</c> as <c>01-02 03:04:05.123</c>: enough to read, short enough to scan.</summary>
	private static string ShortTime(string timestamp) =>
		timestamp.Length >= 23 && timestamp[19] == '.' ? $"{timestamp[5..10]} {timestamp[11..23]}"
		: timestamp.Length >= 19 ? $"{timestamp[5..10]} {timestamp[11..19]}"
		: timestamp;

	private string SourceCss(string source) =>
		SourceClass?.Invoke(source) is { } css ? $"mt-dk-log-source {css}" : "mt-dk-log-source";

	/// <summary>The text cut into what matches the search and what does not, in order.</summary>
	private IEnumerable<(string Text, bool Match)> Pieces(string text)
	{
		string needle = Needle;
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
}
