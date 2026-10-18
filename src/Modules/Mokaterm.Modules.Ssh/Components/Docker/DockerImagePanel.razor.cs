using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// The selected image's panel on the images page: its id, size and age, the registry a push sends it to and whether this
/// docker is logged in there, and the image's actions: push (one tag, or every tag of its repository), tag, and a new
/// container. Pushes run in the session's background with their output on the page; a tag is a quick change.
/// </summary>
public sealed partial class DockerImagePanel : DockerManagerPageBase
{
	private string? _readFor;
	private string? _configProblem;
	private string _tag = "";
	private string? _tagError;
	private bool _tagging;

	/// <summary>The tag, or the short id of an untagged image.</summary>
	[Parameter, EditorRequired]
	public string Reference { get; set; } = "";

	[Parameter, EditorRequired]
	public string ImageId { get; set; } = "";

	/// <summary>The repository part of the tag, empty for an untagged image.</summary>
	[Parameter]
	public string Repository { get; set; } = "";

	/// <summary>True when a tag names it, which is what a push needs.</summary>
	[Parameter]
	public bool Tagged { get; set; }

	/// <summary>Docker's own size and age, such as "187MB" and "2 days ago".</summary>
	[Parameter]
	public string Details { get; set; } = "";

	/// <summary>Every local tag of the same repository, for a push of all of them.</summary>
	[Parameter]
	public IReadOnlyList<string> RepositoryTags { get; set; } = [];

	/// <summary>What docker image tag is given as its source: the tag, or the id of an untagged image.</summary>
	[Parameter]
	public string TagSource { get; set; } = "";

	[Parameter]
	public EventCallback OnClose { get; set; }

	/// <summary>Raised after a change, so the list reads the images again.</summary>
	[Parameter]
	public EventCallback OnChanged { get; set; }

	/// <summary>Raised once a push started, so the page shows its output.</summary>
	[Parameter]
	public EventCallback OnPushStarted { get; set; }

	private string Registry => DockerImageName.Parse(Reference)?.Registry ?? DockerImageName.DefaultRegistry;

	private string RegistryDisplay => DockerRegistryNames.IsDockerHub(Registry) ? $"{DockerRegistryNames.DockerHubName} (docker.io)" : Registry;

	protected override async Task OnParametersSetAsync()
	{
		if (!Tagged || string.Equals(_readFor, Reference, StringComparison.Ordinal) || Feature is not { } feature)
		{
			return;
		}

		_readFor = Reference;
		_configProblem = null;
		try
		{
			_ = await feature.ReadClientConfigAsync(Lifetime);
		}
		catch (Exception ex) when (Describe(ex) is { } problem)
		{
			_configProblem = $"Whether this docker is logged in there could not be read: {problem}";
		}
		catch (OperationCanceledException)
		{
			// The panel went away.
		}
	}

	private void ToggleTag()
	{
		_tagging = !_tagging;
		_tagError = null;
		if (_tagging && _tag.Length == 0 && Tagged)
		{
			_tag = Reference;
		}
	}

	private void OnTagChanged(string? value)
	{
		_tag = value ?? "";
		_tagError = null;
	}

	private async Task TagAsync()
	{
		string target = _tag.Trim();
		if (DockerImageName.TagProblem(target) is { } problem)
		{
			_tagError = problem;
			return;
		}

		if (DockerImageName.Key(target) == DockerImageName.Key(Reference))
		{
			_tagError = "That is the tag it has already.";
			return;
		}

		DockerOutput? output = await RunChangeAsync(
			DockerCommands.Tag(TagSource.Length > 0 ? TagSource : ImageId, target),
			$"Tag {Reference} as {target}",
			afterwards: () => OnChanged.InvokeAsync(),
			destructive: false);
		if (output is { Succeeded: true })
		{
			Feature?.ImageUpdates.Forget([target]);
			_tag = "";
			_tagging = false;
		}
	}

	private async Task PushAsync(bool allTags)
	{
		if (Feature is not { } feature)
		{
			return;
		}

		DockerClientConfig? config = feature.ClientConfig;
		string login = config is null
			? "Whether this docker is logged in there was not read."
			: DockerRegistryWording.LoginState(config, Registry);
		string what = allTags ? $"every local tag of {Repository} ({RepositoryTags.Count})" : Reference;
		DockerCall call;
		try
		{
			call = DockerCommands.Push(allTags ? Repository : Reference, allTags);
		}
		catch (ArgumentException ex)
		{
			Interaction.Notify(NoticeSeverity.Warning, ex.Message, "Cannot push");
			return;
		}

		DockerLivePlan plan = new(
			[call],
			allTags ? $"Push {Repository} (all tags)" : $"Push {Reference}",
			$"Sends {what} to {RegistryDisplay}. {login} Layers the registry has already are not sent again, and the server stops the push after {DockerTimeLimit.Describe(DockerCall.PushTimeout)}.",
			"Push",
			DockerLivePlan.ImagesOwner,
			ErrorIsProgress: false)
		{
			Refreshes = allTags ? RepositoryTags : [Reference],
		};

		if (await StartLiveAsync(plan) is not null)
		{
			await OnPushStarted.InvokeAsync();
		}
	}
}
