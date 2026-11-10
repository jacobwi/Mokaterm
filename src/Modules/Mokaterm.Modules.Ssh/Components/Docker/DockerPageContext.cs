using Moka.Red.Data.Table;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.ToolPages;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// What every page of the Docker manager gets from the manager, cascaded: how to open another page, and for a page from
/// another module the session's docker client behind <see cref="IDockerPageContext"/>. The manager's own pages also
/// use it for "New container" from an image, for the containers page opened on its restart loops or on one container,
/// and for the cleanup page opened on one kind, where a request for the page being opened waits here until that page
/// takes it, once.
/// </summary>
internal sealed class DockerPageContext : IDockerPageContext
{
	private readonly Action<string> _show;
	private readonly Func<DockerSessionFeature?> _feature;
	private readonly Dictionary<string, MokaTableLayout> _layouts = new(StringComparer.Ordinal);
	private string? _newContainerImage;
	private bool _buildImage;
	private bool _restartLoops;
	private string? _cleanupKind;
	private (string Id, string Tab)? _container;

	/// <param name="show">Shows a page by its id.</param>
	/// <param name="feature">The session's manager state at the moment of asking, which changes on a reconnect.</param>
	public DockerPageContext(Action<string> show, Func<DockerSessionFeature?> feature)
	{
		ArgumentNullException.ThrowIfNull(show);
		ArgumentNullException.ThrowIfNull(feature);
		_show = show;
		_feature = feature;
	}

	public string Host => _feature()?.Host ?? "";

	public bool IsElevated => _feature()?.IsElevated == true;

	public DockerComposeInfo? Compose => _feature()?.Compose is { } compose
		? new DockerComposeInfo(compose.Command, compose.Version, compose.Program == DockerProgram.ComposePlugin)
		: null;

	public void Navigate(string pageId)
	{
		ArgumentNullException.ThrowIfNull(pageId);
		_show(pageId);
	}

	/// <summary>Opens the containers page with the new container form filled in with <paramref name="image"/>.</summary>
	public void NewContainer(string image)
	{
		ArgumentNullException.ThrowIfNull(image);
		_newContainerImage = image;
		_show(DockerPageIds.Containers);
	}

	/// <summary>Opens the images page with the build form open, as the overview's "Build image" does.</summary>
	public void BuildImage()
	{
		_buildImage = true;
		_show(DockerPageIds.Images);
	}

	/// <summary>Whether the build form was asked for, once; later calls get false.</summary>
	public bool TakeBuildImage()
	{
		bool asked = _buildImage;
		_buildImage = false;
		return asked;
	}

	/// <summary>The image a new container was asked for, once; later calls get null.</summary>
	public string? TakeNewContainerImage()
	{
		string? image = _newContainerImage;
		_newContainerImage = null;
		return image;
	}

	/// <summary>Opens the cleanup page on one kind, such as <c>images</c>: what the images and volumes pages link to.</summary>
	public void ShowCleanup(string kind)
	{
		ArgumentNullException.ThrowIfNull(kind);
		_cleanupKind = kind;
		_show(DockerPageIds.Cleanup);
	}

	/// <summary>The kind the cleanup page was asked to open on, once; later calls get null.</summary>
	public string? TakeCleanupKind()
	{
		string? kind = _cleanupKind;
		_cleanupKind = null;
		return kind;
	}

	public void ShowRestartLoops()
	{
		_restartLoops = true;
		_show(DockerPageIds.Containers);
	}

	public bool TakeRestartLoops()
	{
		bool asked = _restartLoops;
		_restartLoops = false;
		return asked;
	}

	public void ShowContainer(string id, string tab)
	{
		ArgumentNullException.ThrowIfNull(id);
		ArgumentNullException.ThrowIfNull(tab);
		_container = (id, tab);
		_show(DockerPageIds.Containers);
	}

	public (string Id, string Tab)? TakeContainer()
	{
		(string Id, string Tab)? container = _container;
		_container = null;
		return container;
	}

	/// <summary>The columns the user hid or resized on a page, kept while the manager is open so the page comes back the same.</summary>
	public MokaTableLayout? LayoutOf(string pageId)
	{
		ArgumentNullException.ThrowIfNull(pageId);
		return _layouts.GetValueOrDefault(pageId);
	}

	public void KeepLayout(string pageId, MokaTableLayout layout)
	{
		ArgumentNullException.ThrowIfNull(pageId);
		ArgumentNullException.ThrowIfNull(layout);
		_layouts[pageId] = layout;
	}

	public string Display(DockerCommand command)
	{
		DockerSessionFeature feature = Feature();
		DockerClient client = feature.Client;
		return (client.IsElevated ? "sudo " : "") + DockerPageCalls.ToCall(command, feature.Compose).Display;
	}

	public Task<DockerOutput> RunAsync(DockerCommand command, CancellationToken cancellationToken)
	{
		DockerSessionFeature feature = Feature();
		return feature.Client.RunAsync(DockerPageCalls.ToCall(command, feature.Compose), cancellationToken);
	}

	public Task<string> ReadAsync(DockerCommand command, CancellationToken cancellationToken)
	{
		DockerSessionFeature feature = Feature();
		return feature.Client.ReadAsync(DockerPageCalls.ToCall(command, feature.Compose), cancellationToken);
	}

	public IDisposable BeginChange(IReadOnlyList<DockerCommand> commands)
	{
		ArgumentNullException.ThrowIfNull(commands);
		DockerSessionFeature feature = Feature();
		return feature.Changes.Begin([.. commands.Select(command => DockerPageCalls.ToCall(command, feature.Compose))]);
	}

	private DockerSessionFeature Feature() =>
		_feature() ?? throw new DockerCommandException(DockerFailure.Other, "The session is not connected.");
}
