namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>What asking a registry about an image's tag found.</summary>
internal enum ImageUpdateState
{
	/// <summary>The tag still points at the image this host pulled.</summary>
	UpToDate,

	/// <summary>The tag points at a different image now: a pull would fetch it.</summary>
	UpdateAvailable,

	/// <summary>No registry digest: built on this host or loaded from a file, so there is nothing to compare with.</summary>
	LocalOnly,

	/// <summary>No tag to ask about: an image id, or a reference pinned by digest.</summary>
	NoTag,

	/// <summary>The host's docker has neither <c>buildx imagetools</c> nor <c>docker manifest</c>.</summary>
	Unsupported,

	/// <summary>The registry wants a login the docker host does not have, or the image is private.</summary>
	Refused,

	/// <summary>The docker host could not reach the registry.</summary>
	Unreachable,

	/// <summary>The registry has no such tag any more.</summary>
	TagGone,

	/// <summary>Anything else; the detail quotes what the tool printed.</summary>
	Failed,
}
