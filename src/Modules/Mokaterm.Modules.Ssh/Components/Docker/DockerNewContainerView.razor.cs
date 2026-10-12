using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// A form for <c>docker run --detach</c>: image, name, published ports, mounts, environment (values masked until
/// shown), restart policy, network and command. Every value is checked against Docker's rules before the exact command
/// is shown for confirmation, and a container that came up is handed to the list to open.
/// </summary>
public sealed partial class DockerNewContainerView : DockerManagerPageBase
{
	private const string DefaultNetwork = "";

	private readonly List<string> _ports = [""];
	private readonly List<string> _mounts = [""];
	private readonly List<VariableRow> _environment = [new()];
	private IReadOnlyDictionary<string, string> _problems = new Dictionary<string, string>();
	private IReadOnlyList<string> _networkChoices = [DefaultNetwork];
	private string _image = "";
	private string _name = "";
	private string _network = DefaultNetwork;
	private string _command = "";
	private string _retries = "";
	private DockerRestartPolicy _restart;
	private bool _networksLoading = true;

	/// <summary>The image the form starts with, such as the one picked on the images page.</summary>
	[Parameter]
	public string Image { get; set; } = "";

	/// <summary>The names of the containers on the host, so a clash shows before docker refuses it.</summary>
	[Parameter]
	public IReadOnlyCollection<string> TakenNames { get; set; } = [];

	/// <summary>Raised with the new container's id once docker started it.</summary>
	[Parameter]
	public EventCallback<string> OnCreated { get; set; }

	[Parameter]
	public EventCallback OnCancel { get; set; }

	private string RestartNote => _restart switch
	{
		DockerRestartPolicy.OnFailure => "Started again after a non-zero exit, as often as the retries allow.",
		DockerRestartPolicy.Always => "Started again whenever it stops, and when the daemon starts.",
		DockerRestartPolicy.UnlessStopped => "Like always, unless it was stopped on purpose.",
		_ => "Never started again on its own.",
	};

	protected override async Task OnInitializedAsync()
	{
		_image = Image;
		await LoadAsync(async (client, token) =>
		{
			IReadOnlyList<DockerNetwork> networks = await client.ListNetworksAsync([], token);
			_networkChoices = [DefaultNetwork, .. networks.Select(network => network.Name).Where(name => name != "bridge").Order(StringComparer.OrdinalIgnoreCase)];
		});
		_networksLoading = false;
	}

	private static string NetworkLabel(string network) => network.Length == 0 ? "Default (bridge)" : network;

	private string? Problem(string field) => _problems.TryGetValue(field, out string? problem) ? problem : null;

	private void Set(ref string field, string? value, string problem)
	{
		field = value ?? "";
		Forget(problem);
	}

	private void SetRow(List<string> rows, int row, string? value, string problem)
	{
		rows[row] = value ?? "";
		Forget(problem);
	}

	/// <summary>Drops a row; its neighbours' problems no longer line up with their places, so they go too until the next try.</summary>
	private void RemoveRow(List<string> rows, int row)
	{
		rows.RemoveAt(row);
		_problems = new Dictionary<string, string>();
	}

	private void AddVariable() => _environment.Add(new VariableRow());

	private void SetVariable(int row, string? name = null, string? value = null)
	{
		if (name is not null)
		{
			_environment[row].Name = name;
		}

		if (value is not null)
		{
			_environment[row].Value = value;
		}

		Forget(DockerRunSpec.VariableField(row));
	}

	private void RemoveVariable(int row)
	{
		_environment.RemoveAt(row);
		_problems = new Dictionary<string, string>();
	}

	private void OnRestartChanged(string? value)
	{
		_restart = Enum.TryParse(value, out DockerRestartPolicy policy) ? policy : DockerRestartPolicy.No;
		Forget(DockerRunSpec.RestartField);
	}

	private void Forget(string field)
	{
		if (_problems.ContainsKey(field))
		{
			_problems = _problems.Where(entry => entry.Key != field).ToDictionary(StringComparer.Ordinal);
		}
	}

	private DockerRunSpec Spec() => new()
	{
		Image = _image,
		Name = _name,
		Ports = [.. _ports],
		Mounts = [.. _mounts],
		Environment = [.. _environment.Select(row => new DockerRunVariable(row.Name, row.Value))],
		Restart = _restart,
		MaxRetries = _retries.Trim().Length == 0
			? 0
			: int.TryParse(_retries.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int retries) ? retries : -1,
		Network = _network,
		Command = _command,
	};

	private async Task CreateAsync()
	{
		if (IsBusy)
		{
			return;
		}

		DockerRunSpec spec = Spec();
		_problems = spec.Problems(TakenNames);
		if (_problems.Count > 0)
		{
			return;
		}

		string title = spec.Name.Trim() is { Length: > 0 } name ? $"Create {name}" : $"Create a container from {spec.Image.Trim()}";
		DockerOutput? output = await RunChangeAsync(spec.ToCall(), title, spec.Summary(), "Create", destructive: false);

		// docker run prints the new id last on stdout; a pull before it reports on stderr.
		if (output is { Succeeded: true } && output.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is [.., var id])
		{
			await OnCreated.InvokeAsync(id);
		}
	}

	/// <summary>One variable as typed; a class, so the row keeps its place in the list while it is edited.</summary>
	private sealed class VariableRow
	{
		public string Name { get; set; } = "";

		public string Value { get; set; } = "";
	}
}
