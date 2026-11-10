using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Containers;
using Moka.Red.Primitives.DataList;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Components.Docker;

public sealed partial class DockerLimitsPanel : DockerManagerPageBase
{
	private DockerContainerDetails? _details;
	private IReadOnlyDictionary<string, string> _problems = new Dictionary<string, string>();
	private string _cpus = "";
	private string _memory = "";
	private string _swap = "";
	private string _retries = "";
	private DockerRestartPolicy _restart;

	[Parameter, EditorRequired]
	public string ContainerId { get; set; } = "";

	[Parameter]
	public EventCallback OnChanged { get; set; }

	private int HostCpus => Feature?.Status?.Info?.Cpus ?? 0;

	private string CpusHelp => HostCpus > 0
		? string.Create(CultureInfo.CurrentCulture, $"Such as 1.5, of the {HostCpus} this host has")
		: "Such as 1.5";

	protected override Task OnInitializedAsync() => LoadAsync();

	private static string? ComposeProject(DockerContainerDetails details) =>
		details.Labels.TryGetValue(DockerLabels.ComposeProject, out string? project) && project.Length > 0 ? project : null;

	private IReadOnlyList<MokaDataListItem> NowFacts(DockerContainerLimits limits)
	{
		string cpus = limits switch
		{
			{ Cpus: { } count } => DockerContainerLimits.CpusText(count) + (HostCpus > 0 ? string.Create(CultureInfo.CurrentCulture, $" of {HostCpus}") : ""),
			{ QuotaCpus: { } quota } => DockerContainerLimits.CpusText(quota) + " (through --cpu-quota)",
			_ => "no limit",
		};
		string swap = limits.MemorySwapBytes switch
		{
			-1 => "-1, no swap limit",
			> 0 => DockerContainerLimits.SizeText(limits.MemorySwapBytes) + " with memory",
			_ => "docker's default",
		};
		string restart = limits.RestartPolicy == DockerRestartPolicy.OnFailure && limits.RestartRetries > 0
			? string.Create(CultureInfo.InvariantCulture, $"on-failure:{limits.RestartRetries}")
			: DockerContainerLimits.PolicyWord(limits.RestartPolicy);
		return
		[
			new("CPUS", cpus),
			new("MEMORY", limits.MemoryBytes > 0 ? DockerContainerLimits.SizeText(limits.MemoryBytes) : "no limit"),
			new("SWAP", swap),
			new("RESTART", limits.AutoRemove ? restart + ", removed when it stops (--rm)" : restart),
		];
	}

	private Task LoadAsync() => LoadAsync(async (client, token) =>
	{
		_details = await client.InspectAsync(ContainerId, token);
		Reset();
	});

	private void Reset()
	{
		if (_details is not { } details)
		{
			return;
		}

		DockerUpdateSpec start = DockerUpdateSpec.From(details.Limits);
		_cpus = start.Cpus;
		_memory = start.Memory;
		_swap = start.MemorySwap;
		_restart = start.Restart;
		_retries = start.MaxRetries.ToString(CultureInfo.InvariantCulture);
		_problems = new Dictionary<string, string>();
	}

	private string? Problem(string field) => _problems.TryGetValue(field, out string? problem) ? problem : null;

	private void Set(ref string field, string? value, string problem)
	{
		field = value ?? "";
		if (_problems.ContainsKey(problem))
		{
			_problems = _problems.Where(entry => entry.Key != problem).ToDictionary(StringComparer.Ordinal);
		}
	}

	private void OnRestartChanged(string? value)
	{
		if (Enum.TryParse(value, out DockerRestartPolicy policy))
		{
			_restart = policy;
		}
	}

	private DockerUpdateSpec? Spec(DockerContainerDetails details)
	{
		int retries = 0;
		if (_restart == DockerRestartPolicy.OnFailure
			&& _retries.Trim().Length > 0
			&& !int.TryParse(_retries.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out retries))
		{
			_problems = new Dictionary<string, string> { [DockerUpdateSpec.RestartField] = "Retries are a whole number, 0 for no limit." };
			return null;
		}

		return new DockerUpdateSpec
		{
			Current = details.Limits,
			Cpus = _cpus,
			Memory = _memory,
			MemorySwap = _swap,
			Restart = _restart,
			MaxRetries = retries,
		};
	}

	private async Task ApplyAsync()
	{
		if (_details is not { } details || Spec(details) is not { } spec)
		{
			return;
		}

		_problems = spec.Problems(HostCpus);
		if (_problems.Count > 0)
		{
			return;
		}

		if (!spec.HasChanges)
		{
			Interaction.Notify(Mokaterm.Abstractions.Interaction.NoticeSeverity.Info, "Nothing changed, so there is nothing to run.");
			return;
		}

		string name = details.Name;
		DockerChangePlan plan = new(
			spec.ToCall(name, HostCpus),
			$"Change the limits of {name}",
			ConfirmText(spec, name, ComposeProject(details)),
			"Apply",
			Destructive: false);
		DockerOutput? output = await RunPlanAsync(plan);
		if (output is { Succeeded: true })
		{
			await LoadAsync();
			await OnChanged.InvokeAsync();
		}
	}

	private static string ConfirmText(DockerUpdateSpec spec, string name, string? project)
	{
		List<string> changes = [];
		if (spec.ChangesCpus)
		{
			changes.Add($"at most {spec.Cpus.Trim()} CPUs");
		}

		if (spec.ChangesMemory)
		{
			changes.Add($"at most {spec.Memory.Trim()} of memory");
		}

		if (spec.ChangesSwap)
		{
			changes.Add(spec.MemorySwap.Trim() == "-1" ? "no swap limit" : $"at most {spec.MemorySwap.Trim()} of memory and swap together");
		}

		if (spec.ChangesRestart)
		{
			changes.Add($"the restart policy {spec.RestartWord()}");
		}

		string text = $"{name} gets {string.Join(", ", changes)}, at once and without a restart. A running program that already uses more memory than the new limit may be killed for it.";
		return project is null
			? text
			: text + $" It belongs to the compose project {project}, so the compose file's values come back when compose makes it again.";
	}
}
