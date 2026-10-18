namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>Which builder an image build goes through, found once per session (<see cref="DockerSessionFeature.BuilderAsync"/>).</summary>
internal enum DockerBuilder
{
	/// <summary>
	/// BuildKit through <c>docker buildx build</c>, where <c>docker buildx version</c> answers: plain progress lines, and
	/// <c>--load</c> so the image lands in this host's images whatever builder buildx has selected.
	/// </summary>
	BuildKit,

	/// <summary>The classic builder of <c>docker build</c>, for a client without buildx.</summary>
	Legacy,
}
