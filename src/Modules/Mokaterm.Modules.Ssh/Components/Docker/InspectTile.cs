using Moka.Red.Core.Enums;

namespace Mokaterm.Modules.Ssh.Components.Docker;

internal sealed record InspectTile(string Label, string Value, string? Help, MokaColor? Color);
