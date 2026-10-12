namespace MokatermPlugin1;

/// <summary>One kernel parameter as the page read it: its value, or why there is none.</summary>
internal sealed record KernelParameter(string Name, string? Value, string? Problem);
