using Microsoft.AspNetCore.Components;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// A form for an image build: the context folder on the docker host (typed or chosen in the file browser), the
/// Dockerfile, tags, build arguments (values masked until shown), a target stage, a platform, no-cache and pull. Every
/// value is checked against Docker's rules before the exact command is shown for confirmation; once confirmed the build
/// runs in the session's background and the page shows its output.
/// </summary>
public sealed partial class DockerBuildView : DockerManagerPageBase
{
	private readonly List<string> _tags = [""];
	private readonly List<ArgumentRow> _arguments = [new()];
	private IReadOnlyDictionary<string, string> _problems = new Dictionary<string, string>();
	private DockerBuilder? _builder;
	private string _context = "";
	private string _dockerfile = "";
	private string _target = "";
	private string _platform = "";
	private string? _startProblem;
	private bool _noCache;
	private bool _pull;
	private bool _starting;

	/// <summary>Raised once the build was confirmed and started.</summary>
	[Parameter]
	public EventCallback OnStarted { get; set; }

	[Parameter]
	public EventCallback OnCancel { get; set; }

	protected override async Task OnInitializedAsync()
	{
		if (Feature is { } feature)
		{
			try
			{
				_builder = await feature.BuilderAsync(Lifetime);
			}
			catch (OperationCanceledException)
			{
				// The page went away; the build would have asked again.
			}
		}
	}

	private string? Problem(string field) => _problems.TryGetValue(field, out string? problem) ? problem : null;

	private void Set(ref string field, string? value, string problem)
	{
		field = value ?? "";
		Forget(problem);
	}

	private void SetTag(int row, string? value)
	{
		_tags[row] = value ?? "";
		Forget(DockerBuildSpec.TagField(row));
	}

	/// <summary>Drops a row; its neighbours' problems no longer line up with their places, so they go too until the next try.</summary>
	private void RemoveTag(int row)
	{
		_tags.RemoveAt(row);
		_problems = new Dictionary<string, string>();
	}

	private void AddArgument() => _arguments.Add(new ArgumentRow());

	private void SetArgument(int row, string? name = null, string? value = null)
	{
		if (name is not null)
		{
			_arguments[row].Name = name;
		}

		if (value is not null)
		{
			_arguments[row].Value = value;
		}

		Forget(DockerBuildSpec.ArgumentField(row));
	}

	private void RemoveArgument(int row)
	{
		_arguments.RemoveAt(row);
		_problems = new Dictionary<string, string>();
	}

	private void Forget(string field)
	{
		_startProblem = null;
		if (_problems.ContainsKey(field))
		{
			_problems = _problems.Where(entry => entry.Key != field).ToDictionary(StringComparer.Ordinal);
		}
	}

	private async Task PickContextAsync()
	{
		string? folder = await PickHostFolderAsync("Choose the build context", _context.Trim());
		if (folder is not null && !IsDisposed)
		{
			Set(ref _context, folder, DockerBuildSpec.ContextField);
		}
	}

	private DockerBuildSpec Spec() => new()
	{
		Context = _context,
		Dockerfile = _dockerfile,
		Tags = [.. _tags],
		Arguments = [.. _arguments.Select(row => new DockerRunVariable(row.Name, row.Value))],
		Target = _target,
		Platform = _platform,
		NoCache = _noCache,
		Pull = _pull,
	};

	private async Task BuildAsync()
	{
		if (_starting || Feature is not { } feature)
		{
			return;
		}

		DockerBuildSpec spec = Spec();
		_problems = spec.Problems();
		if (_problems.Count > 0)
		{
			return;
		}

		_starting = true;
		try
		{
			DockerBuilder builder = _builder ??= await feature.BuilderAsync(Lifetime);
			IReadOnlyList<string> tags = spec.SetTags;
			DockerLivePlan plan = new(
				[spec.ToCall(builder)],
				tags.Count > 0 ? $"Build {tags[0]}" : "Build an untagged image",
				spec.Summary(builder),
				"Build",
				DockerLivePlan.ImagesOwner,
				ErrorIsProgress: builder == DockerBuilder.BuildKit)
			{
				Refreshes = tags,
			};

			if (await StartLiveAsync(plan) is not null)
			{
				await OnStarted.InvokeAsync();
			}
		}
		catch (Exception ex) when (Describe(ex) is { } problem)
		{
			_startProblem = problem;
		}
		catch (OperationCanceledException)
		{
			// The page went away.
		}
		finally
		{
			_starting = false;
		}
	}

	/// <summary>One argument as typed; a class, so the row keeps its place in the list while it is edited.</summary>
	private sealed class ArgumentRow
	{
		public string Name { get; set; } = "";

		public string Value { get; set; } = "";
	}
}
