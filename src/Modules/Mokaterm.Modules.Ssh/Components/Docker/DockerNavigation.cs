namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// How a page of the Docker manager opens another, cascaded from the manager: the overview's "Clean up", an image's
/// "New container". A request for the page being opened waits here until that page takes it, once.
/// </summary>
internal sealed class DockerNavigation
{
	private readonly Action<DockerManagerPage> _show;
	private string? _newContainerImage;

	public DockerNavigation(Action<DockerManagerPage> show)
	{
		ArgumentNullException.ThrowIfNull(show);
		_show = show;
	}

	public void Show(DockerManagerPage page) => _show(page);

	/// <summary>Opens the containers page with the new container form filled in with <paramref name="image"/>.</summary>
	public void NewContainer(string image)
	{
		ArgumentNullException.ThrowIfNull(image);
		_newContainerImage = image;
		_show(DockerManagerPage.Containers);
	}

	/// <summary>The image a new container was asked for, once; later calls get null.</summary>
	public string? TakeNewContainerImage()
	{
		string? image = _newContainerImage;
		_newContainerImage = null;
		return image;
	}
}
