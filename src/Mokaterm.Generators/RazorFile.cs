namespace Mokaterm.Generators;

/// <summary>What the settings search needs from one .razor file.</summary>
/// <param name="Path">The full path, as the compiler was handed it.</param>
/// <param name="Namespace">The file's own <c>@namespace</c>, when it has one.</param>
/// <param name="IsGeneric">A <c>@typeparam</c> component, which no <c>typeof</c> can name without its arguments.</param>
/// <param name="Rows">Every row with a literal label, in the order the page draws them.</param>
internal sealed record RazorFile(string Path, string? Namespace, bool IsGeneric, EquatableArray<SettingsRowInfo> Rows)
{
	public bool IsImports => string.Equals(System.IO.Path.GetFileName(Path), "_Imports.razor", StringComparison.OrdinalIgnoreCase);

	/// <summary>Only pages with rows, and imports that set a namespace for the pages under them, reach the output.</summary>
	public bool Matters => Rows.Count > 0 || (IsImports && Namespace is not null);
}
