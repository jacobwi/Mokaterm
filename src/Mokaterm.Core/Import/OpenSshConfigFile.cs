namespace Mokaterm.Core.Import;

/// <summary>A config file an <c>Include</c> line named, already read.</summary>
/// <param name="Name">Full path when there is one, otherwise the name to show in a message.</param>
internal sealed record OpenSshConfigFile(string Name, string Text);
