namespace Mokaterm.Abstractions.Plugins;

/// <summary>The gallery's list or a package from it could not be read or failed its checks. The message is for the user.</summary>
public sealed class PluginGalleryException : Exception
{
	public PluginGalleryException()
	{
	}

	public PluginGalleryException(string message)
		: base(message)
	{
	}

	public PluginGalleryException(string message, Exception? innerException)
		: base(message, innerException)
	{
	}
}
