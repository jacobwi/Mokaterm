namespace Mokaterm.Abstractions.Modules;

/// <summary>Describes a registered module. Every added module is also resolvable as <c>IEnumerable&lt;ModuleInfo&gt;</c>.</summary>
public sealed record ModuleInfo(string Id, string Name, string Description, string Version);
