namespace Mokaterm.Modules.Local.Shells;

/// <summary>A WSL distribution this user has installed, and whether a bare <c>wsl</c> opens it.</summary>
internal sealed record WslDistribution(string Name, bool IsDefault);
