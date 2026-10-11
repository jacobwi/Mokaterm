namespace Mokaterm.Modules.Ssh.Admin.Packages;

/// <summary>One installed package.</summary>
internal sealed record InstalledPackage(string Name, string Version, string Architecture);
