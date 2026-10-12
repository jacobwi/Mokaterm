using System.Globalization;
using System.Text;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.Credentials;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// A new container as the form fills it in, and the <c>docker run --detach</c> it becomes. Every value passes Docker's
/// own rule first (<see cref="DockerNames"/>), and a problem is kept against the field it belongs to so the form can show
/// it there. Blank rows in the lists are left out: the form keeps one to type into.
/// </summary>
internal sealed record DockerRunSpec
{
	public const string ImageField = "image";

	public const string NameField = "name";

	public const string NetworkField = "network";

	public const string RestartField = "restart";

	public const string CommandField = "command";

	public required string Image { get; init; }

	/// <summary>Empty lets docker pick a name.</summary>
	public string Name { get; init; } = "";

	/// <summary>Each <c>[ip:]host:container[/proto]</c>.</summary>
	public IReadOnlyList<string> Ports { get; init; } = [];

	/// <summary>Each <c>source:target[:options]</c>, or a container path alone for an anonymous volume.</summary>
	public IReadOnlyList<string> Mounts { get; init; } = [];

	public IReadOnlyList<DockerRunVariable> Environment { get; init; } = [];

	public DockerRestartPolicy Restart { get; init; }

	/// <summary>For <see cref="DockerRestartPolicy.OnFailure"/>: how many restarts at most, 0 for no limit.</summary>
	public int MaxRetries { get; init; }

	/// <summary>An existing network; empty leaves docker's default bridge.</summary>
	public string Network { get; init; } = "";

	/// <summary>The command and its arguments after the image, split the way a shell would but never run by one.</summary>
	public string Command { get; init; } = "";

	public static string PortField(int index) => string.Create(CultureInfo.InvariantCulture, $"port:{index}");

	public static string MountField(int index) => string.Create(CultureInfo.InvariantCulture, $"mount:{index}");

	public static string VariableField(int index) => string.Create(CultureInfo.InvariantCulture, $"env:{index}");

	/// <summary>Why each field cannot be used, by field; empty when the container can be made.</summary>
	/// <param name="takenNames">The names of the containers on the host, so a clash is caught before docker refuses it.</param>
	public IReadOnlyDictionary<string, string> Problems(IReadOnlyCollection<string>? takenNames = null)
	{
		Dictionary<string, string> problems = new(StringComparer.Ordinal);
		if (DockerNames.ImageProblem(Image.Trim()) is { } image)
		{
			problems[ImageField] = image;
		}

		string name = Name.Trim();
		if (name.Length > 0 && DockerNames.NewNameProblem(name, "container") is { } naming)
		{
			problems[NameField] = naming;
		}
		else if (name.Length > 0 && takenNames is not null && takenNames.Contains(name, StringComparer.Ordinal))
		{
			problems[NameField] = $"A container named {name} exists already.";
		}

		for (int index = 0; index < Ports.Count; index++)
		{
			if (Ports[index].Trim().Length > 0 && DockerNames.PortMappingProblem(Ports[index]) is { } port)
			{
				problems[PortField(index)] = port;
			}
		}

		for (int index = 0; index < Mounts.Count; index++)
		{
			if (Mounts[index].Trim().Length > 0 && DockerNames.MountProblem(Mounts[index]) is { } mount)
			{
				problems[MountField(index)] = mount;
			}
		}

		CheckEnvironment(problems);

		if (Restart == DockerRestartPolicy.OnFailure && DockerNames.RestartRetriesProblem(MaxRetries) is { } retries)
		{
			problems[RestartField] = retries;
		}

		string network = Network.Trim();
		if (network.Length > 0 && DockerNames.ExistingNameProblem(network, "network") is { } networking)
		{
			problems[NetworkField] = networking;
		}
		else if (network is "host" or "none" && PublishedPorts.Count > 0)
		{
			problems[NetworkField] = $"On the {network} network nothing is published, so the ports would be dropped. Remove them or pick another network.";
		}

		string command = Command.Trim();
		if (command.Any(char.IsControl))
		{
			problems[CommandField] = "The command has to be one line with no control characters.";
		}
		else if (command.Length > 0 && DockerExecWords.Split(command).Problem is { } words)
		{
			problems[CommandField] = words;
		}

		return problems;
	}

	/// <summary>The words after <c>docker</c>, with the index of every secret value.</summary>
	/// <exception cref="ArgumentException">A field breaks its rule; <see cref="Problems"/> says which.</exception>
	public (IReadOnlyList<string> Words, IReadOnlySet<int> Secrets) Arguments()
	{
		if (Problems() is { Count: > 0 } problems)
		{
			throw new ArgumentException(problems.Values.First());
		}

		List<string> words = ["run", "--detach"];
		HashSet<int> secrets = [];
		if (Name.Trim() is { Length: > 0 } name)
		{
			words.AddRange(["--name", name]);
		}

		if (RestartWord() is { } restart)
		{
			words.AddRange(["--restart", restart]);
		}

		if (Network.Trim() is { Length: > 0 } network)
		{
			words.AddRange(["--network", network]);
		}

		foreach (string port in PublishedPorts)
		{
			words.AddRange(["--publish", port]);
		}

		foreach (string mount in Mounts.Select(mount => mount.Trim()).Where(mount => mount.Length > 0))
		{
			words.AddRange(["--volume", mount]);
		}

		foreach (DockerRunVariable variable in SetVariables)
		{
			words.Add("--env");
			secrets.Add(words.Count);
			words.Add($"{variable.Name.Trim()}={variable.Value}");
		}

		words.Add(Image.Trim());
		if (Command.Trim() is { Length: > 0 } command)
		{
			words.AddRange(DockerExecWords.Split(command).Words);
		}

		return (words, secrets);
	}

	/// <summary>The call, with the time a pull may take: <c>docker run</c> pulls an image the host does not have.</summary>
	public DockerCall ToCall()
	{
		(IReadOnlyList<string> words, IReadOnlySet<int> secrets) = Arguments();
		return new DockerCall { Program = DockerProgram.Docker, Arguments = words, MaskedArguments = secrets }.AsChange(DockerCall.LongTimeout);
	}

	/// <summary>What the confirmation says before the exact command.</summary>
	public string Summary()
	{
		string name = Name.Trim() is { Length: > 0 } named ? named : "a container docker names itself";
		StringBuilder text = new($"Creates and starts {name} from {Image.Trim()}, pulling the image first when this host does not have it.");
		if (SetVariables.Count > 0)
		{
			text.Append(" Environment values are hidden below, and the command carries them as typed.");
		}

		return text.ToString();
	}

	private IReadOnlyList<string> PublishedPorts => [.. Ports.Select(port => port.Trim()).Where(port => port.Length > 0)];

	private IReadOnlyList<DockerRunVariable> SetVariables => [.. Environment.Where(variable => variable.Name.Trim().Length > 0 || variable.Value.Length > 0)];

	private void CheckEnvironment(Dictionary<string, string> problems)
	{
		HashSet<string> seen = new(StringComparer.Ordinal);
		for (int index = 0; index < Environment.Count; index++)
		{
			DockerRunVariable variable = Environment[index];
			string name = variable.Name.Trim();
			if (name.Length == 0 && variable.Value.Length == 0)
			{
				continue;
			}

			string? problem = DockerNames.VariableProblem(name)
				?? (variable.Value.Any(char.IsControl) ? $"The value of {name} holds a control character." : null)
				?? (seen.Add(name) ? null : $"{name} is set twice; docker would keep the last one.");
			if (problem is not null)
			{
				problems[VariableField(index)] = problem;
			}
		}
	}

	private string? RestartWord() => Restart switch
	{
		DockerRestartPolicy.Always => "always",
		DockerRestartPolicy.UnlessStopped => "unless-stopped",
		DockerRestartPolicy.OnFailure when MaxRetries > 0 => string.Create(CultureInfo.InvariantCulture, $"on-failure:{MaxRetries}"),
		DockerRestartPolicy.OnFailure => "on-failure",
		_ => null,
	};

	// The command and the environment values can carry tokens.
	private bool PrintMembers(StringBuilder builder)
	{
		builder
			.Append("Image = ").Append(Image)
			.Append(", Name = ").Append(Name)
			.Append(", Command = ").Append(SecretText.Describe(Command))
			.Append(", Environment = ").Append(Environment.Count).Append(" variables");
		return true;
	}
}
