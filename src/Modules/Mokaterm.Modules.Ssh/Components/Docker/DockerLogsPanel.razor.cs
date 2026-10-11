using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// A container's log: the last lines with their timestamps, both streams in order, and a follow that polls for what
/// arrived since the newest line shown. Polling rather than <c>docker logs --follow</c>, because a follow is an exec
/// channel SSH.NET keeps reading into memory whether or not anyone draws it.
/// </summary>
public sealed partial class DockerLogsPanel : DockerPageBase
{
	private static readonly int[] TailChoices = [100, 500, 2_000, 10_000];
	private static readonly TimeSpan FollowInterval = TimeSpan.FromSeconds(2);

	private readonly DockerLogTail _tail = new();
	private int _lines = 500;
	private bool _timestamps = true;
	private bool _follow;
	private string _query = "";

	[Parameter, EditorRequired]
	public string ContainerId { get; set; } = "";

	private string CountText
	{
		get
		{
			int count = _tail.Lines.Count;
			string text = string.Create(CultureInfo.CurrentCulture, $"{count:N0} {(count == 1 ? "line" : "lines")}");
			if (_query.Trim() is { Length: > 0 } needle)
			{
				int matching = _tail.Lines.Count(line => line.Text.Contains(needle, StringComparison.OrdinalIgnoreCase));
				text = string.Create(CultureInfo.CurrentCulture, $"{matching:N0} of {text} match");
			}

			return _follow ? text + ", following" : text;
		}
	}

	private string EmptyText => IsFirstLoad ? "Reading the log..." : "The container has logged nothing.";

	protected override Task OnInitializedAsync() => ReloadAsync();

	private Task ReloadAsync() => LoadAsync(async (client, token) =>
		_tail.Reset(await client.LogsAsync(ContainerId, _lines, since: null, token)));

	private Task FetchNewAsync()
	{
		if (_tail.Newest.Length == 0)
		{
			return ReloadAsync();
		}

		return LoadAsync(async (client, token) =>
			_tail.Append(await client.LogsAsync(ContainerId, DockerCommands.MaxTail, _tail.Newest, token)));
	}

	private Task OnLinesChangedAsync(string? value)
	{
		if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int lines) && TailChoices.Contains(lines))
		{
			_lines = lines;
		}

		return ReloadAsync();
	}

	private void OnTimestampsChanged(bool value) => _timestamps = value;

	private void OnQueryChanged(string value) => _query = value ?? "";

	private void ToggleFollow()
	{
		_follow = !_follow;
		if (_follow)
		{
			StartPolling(FollowInterval, FetchNewAsync);
		}
		else
		{
			StopPolling();
		}
	}
}
