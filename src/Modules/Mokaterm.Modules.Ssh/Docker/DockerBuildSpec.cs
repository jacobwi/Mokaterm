using System.Globalization;
using System.Text;
using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// An image build as the form fills it in, and the <c>docker buildx build</c> or <c>docker build</c> it becomes. Every
/// value passes Docker's own rule first (<see cref="DockerNames"/>, tags through <see cref="DockerImageName"/>), and a
/// problem is kept against its field, the way <see cref="DockerRunSpec"/> does. Blank rows are left out.
/// </summary>
internal sealed record DockerBuildSpec
{
	public const string ContextField = "context";

	public const string DockerfileField = "dockerfile";

	public const string TagsField = "tags";

	public const string TargetField = "target";

	public const string PlatformField = "platform";

	/// <summary>The name a Dockerfile has when the form leaves it empty, in the context folder.</summary>
	public const string DefaultDockerfile = "Dockerfile";

	/// <summary>The folder on the docker host that is sent to the builder, absolute.</summary>
	public required string Context { get; init; }

	/// <summary>Empty for <c>Dockerfile</c> in the context; a relative path is read from the context, an absolute one as it is.</summary>
	public string Dockerfile { get; init; } = "";

	/// <summary>Each <c>name[:tag]</c> the image gets.</summary>
	public IReadOnlyList<string> Tags { get; init; } = [];

	/// <summary>Each <c>--build-arg NAME=value</c>. Values are masked in what is shown.</summary>
	public IReadOnlyList<DockerRunVariable> Arguments { get; init; } = [];

	/// <summary>The stage to stop at in a multi-stage Dockerfile; empty builds the last one.</summary>
	public string Target { get; init; } = "";

	/// <summary>One platform such as <c>linux/arm64</c>; empty builds for the host's own.</summary>
	public string Platform { get; init; } = "";

	/// <summary><c>--no-cache</c>: every step runs again.</summary>
	public bool NoCache { get; init; }

	/// <summary><c>--pull</c>: the base images are pulled again even when the host has them.</summary>
	public bool Pull { get; init; }

	public static string TagField(int index) => string.Create(CultureInfo.InvariantCulture, $"tag:{index}");

	public static string ArgumentField(int index) => string.Create(CultureInfo.InvariantCulture, $"arg:{index}");

	/// <summary>The Dockerfile's path on the host, or null when the default in the context is meant.</summary>
	public string? DockerfilePath
	{
		get
		{
			string file = Dockerfile.Trim();
			if (file.Length == 0 || file == DefaultDockerfile)
			{
				return null;
			}

			return file.StartsWith('/') ? file : ContextFolder.TrimEnd('/') + "/" + file;
		}
	}

	/// <summary>The tags as typed, without blank rows.</summary>
	public IReadOnlyList<string> SetTags => [.. Tags.Select(tag => tag.Trim()).Where(tag => tag.Length > 0)];

	private string ContextFolder => Context.Trim() is { Length: > 1 } folder ? folder.TrimEnd('/') : Context.Trim();

	private IReadOnlyList<DockerRunVariable> SetArguments => [.. Arguments.Where(argument => argument.Name.Trim().Length > 0 || argument.Value.Length > 0)];

	/// <summary>Why each field cannot be used, by field; empty when the build can run.</summary>
	public IReadOnlyDictionary<string, string> Problems()
	{
		Dictionary<string, string> problems = new(StringComparer.Ordinal);
		if (DockerNames.PathProblem(Context.Trim(), "build context") is { } context)
		{
			problems[ContextField] = context;
		}

		string file = Dockerfile.Trim();
		string? fileProblem = file.Any(char.IsControl)
			? "The Dockerfile's path holds a control character."
			: DockerfilePath is { } path ? DockerNames.PathProblem(path, "Dockerfile") : null;
		if (fileProblem is not null)
		{
			problems[DockerfileField] = fileProblem;
		}

		HashSet<string> seenTags = new(StringComparer.Ordinal);
		for (int index = 0; index < Tags.Count; index++)
		{
			string tag = Tags[index].Trim();
			if (tag.Length == 0)
			{
				continue;
			}

			string? problem = DockerImageName.TagProblem(tag) ?? (seenTags.Add(DockerImageName.Key(tag)) ? null : $"{tag} is named twice.");
			if (problem is not null)
			{
				problems[TagField(index)] = problem;
			}
		}

		CheckArguments(problems);

		if (Target.Trim().Length > 0 && DockerNames.StageProblem(Target) is { } target)
		{
			problems[TargetField] = target;
		}

		if (Platform.Trim().Length > 0 && DockerNames.PlatformProblem(Platform) is { } platform)
		{
			problems[PlatformField] = platform;
		}

		return problems;
	}

	/// <summary>
	/// The call for <paramref name="builder"/>, under the server's own time limit (<see cref="DockerCall.BuildTimeout"/>),
	/// with each build argument's value masked in what is shown.
	/// </summary>
	/// <exception cref="ArgumentException">A field breaks its rule; <see cref="Problems"/> says which.</exception>
	public DockerCall ToCall(DockerBuilder builder)
	{
		if (Problems() is { Count: > 0 } problems)
		{
			throw new ArgumentException(problems.Values.First());
		}

		List<string> words = builder == DockerBuilder.BuildKit ? ["buildx", "build", "--progress=plain", "--load"] : ["build"];
		HashSet<int> secrets = [];
		if (Pull)
		{
			words.Add("--pull");
		}

		if (NoCache)
		{
			words.Add("--no-cache");
		}

		if (Platform.Trim() is { Length: > 0 } platform)
		{
			words.AddRange(["--platform", platform]);
		}

		if (Target.Trim() is { Length: > 0 } target)
		{
			words.AddRange(["--target", target]);
		}

		if (DockerfilePath is { } file)
		{
			words.AddRange(["--file", file]);
		}

		foreach (string tag in SetTags)
		{
			words.AddRange(["--tag", tag]);
		}

		foreach (DockerRunVariable argument in SetArguments)
		{
			words.Add("--build-arg");
			secrets.Add(words.Count);
			words.Add($"{argument.Name.Trim()}={argument.Value}");
		}

		words.Add(ContextFolder);
		DockerCall call = new()
		{
			Program = DockerProgram.Docker,
			Arguments = words,
			MaskedArguments = secrets,
			OutputLimitBytes = DockerCall.LiveOutputBytes,
		};
		return DockerTimeLimit.Wrap(call, DockerCall.BuildTimeout);
	}

	/// <summary>What the confirmation says before the exact command.</summary>
	public string Summary(DockerBuilder builder)
	{
		StringBuilder text = new($"Builds {ContextFolder} with {(builder == DockerBuilder.BuildKit ? "BuildKit" : "the classic builder")}");
		IReadOnlyList<string> tags = SetTags;
		text.Append(tags.Count == 0 ? ", untagged" : $", tagged {string.Join(", ", tags)}");
		text.Append(". It runs whatever the Dockerfile says, as the daemon does, and stops after ")
			.Append(DockerTimeLimit.Describe(DockerCall.BuildTimeout))
			.Append(" on the server even if this app is gone.");
		if (SetArguments.Count > 0)
		{
			text.Append(" Build argument values are hidden below, but a build argument stays readable in the image's history: a real secret belongs in a build secret.");
		}

		return text.ToString();
	}

	private void CheckArguments(Dictionary<string, string> problems)
	{
		HashSet<string> seen = new(StringComparer.Ordinal);
		for (int index = 0; index < Arguments.Count; index++)
		{
			DockerRunVariable argument = Arguments[index];
			string name = argument.Name.Trim();
			if (name.Length == 0 && argument.Value.Length == 0)
			{
				continue;
			}

			string? problem = DockerNames.VariableProblem(name)
				?? (argument.Value.Any(char.IsControl) ? $"The value of {name} holds a control character." : null)
				?? (seen.Add(name) ? null : $"{name} is set twice; docker would keep the last one.");
			if (problem is not null)
			{
				problems[ArgumentField(index)] = problem;
			}
		}
	}

	// Build argument values can carry tokens, and paths name the server's layout.
	private bool PrintMembers(StringBuilder builder)
	{
		builder
			.Append("Tags = ").Append(Tags.Count)
			.Append(", Arguments = ").Append(Arguments.Count)
			.Append(", NoCache = ").Append(NoCache)
			.Append(", Pull = ").Append(Pull);
		return true;
	}
}
