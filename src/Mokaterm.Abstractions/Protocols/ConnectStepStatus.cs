namespace Mokaterm.Abstractions.Protocols;

/// <summary>Where one step of a connect attempt stands.</summary>
/// <param name="Step">The step as the protocol planned it.</param>
/// <param name="State">How it stands.</param>
/// <param name="Detail">
/// What a running step is doing (the address being dialed), why a step failed or why it was skipped. May name the server,
/// as connect errors do; never a secret.
/// </param>
public sealed record ConnectStepStatus(ConnectStep Step, ConnectStepState State, string? Detail = null);
