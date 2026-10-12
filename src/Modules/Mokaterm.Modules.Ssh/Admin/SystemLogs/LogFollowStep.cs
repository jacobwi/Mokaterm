using Mokaterm.Modules.Ssh.Tools;

namespace Mokaterm.Modules.Ssh.Admin.SystemLogs;

/// <summary>What one follow poll brought: the new lines, and a sentence when the log turned over or lines were skipped.</summary>
internal sealed record LogFollowStep(IReadOnlyList<LogLine> Lines, string? Notice);
