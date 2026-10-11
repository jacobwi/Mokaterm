using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// A project's merged log: every service's lines in time order, each container in its service's colour, narrowed to
/// one service on request. Following polls with <c>--since</c> the newest line shown; the Python compose has no
/// <c>--since</c>, so there it fetches the tail again.
/// </summary>
public sealed partial class ComposeLogsPanel : DockerPageBase
{
	private const string AllServices = "";
	private static readonly int[] TailChoices = [100, 500, 2_000];
	private static readonly TimeSpan FollowInterval = TimeSpan.FromSeconds(3);

	private readonly DockerLogTail _lines = new();
	private string _service = AllServices;
	private int _tail = 500;
	private bool _timestamps = true;
	private bool _follow;
	private string _query = "";
	private string? _appliedService;

	/// <summary>The service to start on, from a service row's logs button; null for all of them.</summary>
	[Parameter]
	public string? Service { get; set; }

	[CascadingParameter]
	private ComposeProjectContext? Context { get; set; }

	private IReadOnlyList<string> Choices => [AllServices, .. (Context?.Services ?? []).Select(service => service.Name)];

	private string EmptyText => IsFirstLoad ? "Reading the logs..." : "Nothing logged.";

	private string CountText
	{
		get
		{
			int count = _lines.Lines.Count;
			string text = string.Create(CultureInfo.CurrentCulture, $"{count:N0} {(count == 1 ? "line" : "lines")}");
			if (_query.Trim() is { Length: > 0 } needle)
			{
				int matching = _lines.Lines.Count(line => line.Text.Contains(needle, StringComparison.OrdinalIgnoreCase));
				text = string.Create(CultureInfo.CurrentCulture, $"{matching:N0} of {text} match");
			}

			return _follow ? text + ", following" : text;
		}
	}

	protected override Task OnParametersSetAsync()
	{
		// A service row's logs button changes the service while the tab is open.
		string wanted = Service ?? AllServices;
		if (_appliedService == wanted)
		{
			return Task.CompletedTask;
		}

		_appliedService = wanted;
		_service = wanted;
		return ReloadAsync();
	}

	private static string ChoiceLabel(string choice) => choice.Length == 0 ? "All services" : choice;

	private string? SourceClass(string source) => Context?.SourceClass(source);

	private Task ReloadAsync() => LoadAsync(async (client, token) =>
	{
		if (Context?.Target is { } target)
		{
			_lines.Reset(await client.ComposeLogsAsync(target, ServiceArgument, _tail, since: null, token));
		}
	});

	private Task FetchNewAsync()
	{
		if (Context?.Target is not { } target)
		{
			return Task.CompletedTask;
		}

		if (target.Tool.IsLegacy || _lines.Newest.Length == 0)
		{
			return ReloadAsync();
		}

		return LoadAsync(async (client, token) =>
			_lines.Append(await client.ComposeLogsAsync(target, ServiceArgument, DockerCommands.MaxTail, _lines.Newest, token)));
	}

	private string? ServiceArgument => _service.Length == 0 ? null : _service;

	private Task OnServiceChangedAsync(string? value)
	{
		_service = value ?? AllServices;
		return ReloadAsync();
	}

	private Task OnTailChangedAsync(string? value)
	{
		if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tail) && TailChoices.Contains(tail))
		{
			_tail = tail;
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
