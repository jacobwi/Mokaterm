using Mokaterm.Abstractions.Import;

namespace Mokaterm.Core.Import;

/// <summary>The blocks a config produced, plus everything the parser had to leave alone.</summary>
internal sealed record OpenSshConfigParseResult(IReadOnlyList<OpenSshConfigBlock> Blocks, IReadOnlyList<ImportSkip> Notes);
