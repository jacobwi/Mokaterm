namespace Mokaterm.Abstractions.Protocols;

/// <summary>One step a connect attempt goes through, as its protocol plans it (<see cref="IConnectSteps.Plan"/>).</summary>
/// <param name="Id">Names the step in reports. Unique within a plan, and never shown.</param>
/// <param name="Label">The step's name as the shell shows it, a word or two such as "Host key".</param>
/// <param name="Kind">What the step does, which picks its icon.</param>
public sealed record ConnectStep(string Id, string Label, ConnectStepKind Kind = ConnectStepKind.Other);
