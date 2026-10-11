using Microsoft.CodeAnalysis.Diagnostics;

namespace Mokaterm.Generators;

/// <summary>The two MSBuild properties the Razor SDK hands the compiler that decide a component's namespace.</summary>
internal sealed record ProjectInfo(string RootNamespace, string ProjectDirectory)
{
	public bool IsUsable => RootNamespace.Length > 0 && ProjectDirectory.Length > 0;

	public static ProjectInfo From(AnalyzerConfigOptions options)
	{
		options.TryGetValue("build_property.RootNamespace", out string? rootNamespace);
		options.TryGetValue("build_property.MSBuildProjectDirectory", out string? projectDirectory);
		return new ProjectInfo(rootNamespace?.Trim() ?? "", projectDirectory?.Trim() ?? "");
	}
}
