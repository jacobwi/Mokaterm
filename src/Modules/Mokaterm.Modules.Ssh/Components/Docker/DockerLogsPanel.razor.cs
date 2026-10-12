using Microsoft.AspNetCore.Components;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Tools;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// A container's log: the last lines with their timestamps, both streams in order, and a follow that polls for what
/// arrived since the newest line shown. Polling rather than <c>docker logs --follow</c>, because a follow is an exec
/// channel SSH.NET keeps reading into memory whether or not anyone draws it.
/// </summary>
public sealed partial class DockerLogsPanel : DockerManagerPageBase
{
	private static readonly int[] TailChoices = [100, 500, 2_000, 10_000];
	private static readonly TimeSpan FollowInterval = TimeSpan.FromSeconds(2);

	private readonly LogTail _tail = new();
	private int _lines = 500;
	private bool _follow;

	[Parameter, EditorRequired]
	public string ContainerId { get; set; } = "";

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

	private Task OnLinesChangedAsync(int lines)
	{
		_lines = lines;
		return ReloadAsync();
	}

	private void OnFollowingChanged(bool follow)
	{
		_follow = follow;
		if (follow)
		{
			StartPolling(FollowInterval, FetchNewAsync);
		}
		else
		{
			StopPolling();
		}
	}
}
