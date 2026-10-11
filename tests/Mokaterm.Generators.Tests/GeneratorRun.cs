using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Mokaterm.Generators.Tests;

/// <summary>
/// Runs <see cref="SettingsSearchGenerator"/> the way a Razor project does: the .razor files as additional texts, the
/// root namespace and project folder as global options, and a compilation that holds the attribute it writes.
/// </summary>
internal sealed class GeneratorRun
{
	public const string ProjectDirectory = "/repo/Demo";

	/// <summary>The attribute as UI.Common declares it, so the generated file binds the way it does there.</summary>
	private const string AttributeSource = """
		namespace Mokaterm.UI.Common.Contributions;

		[System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
		public sealed class SettingsSearchEntryAttribute(System.Type page, string? section, string label, string? description) : System.Attribute
		{
			public System.Type Page { get; } = page;
			public string? Section { get; } = section;
			public string Label { get; } = label;
			public string? Description { get; } = description;
		}
		""";

	private static readonly ImmutableArray<MetadataReference> References =
	[
		.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
			.Split(Path.PathSeparator)
			.Where(path => Path.GetFileName(path) is "System.Runtime.dll" or "System.Private.CoreLib.dll" or "netstandard.dll")
			.Select(path => MetadataReference.CreateFromFile(path)),
	];

	private GeneratorRun(Compilation output, IReadOnlyList<string> generated)
	{
		Output = output;
		Generated = generated;
	}

	public Compilation Output { get; }

	public IReadOnlyList<string> Generated { get; }

	/// <summary>Every entry the generated attributes hold: the page's full name, the section, the label and the description.</summary>
	public IReadOnlyList<(string Page, string? Section, string Label, string? Description)> Entries =>
	[
		.. Output.Assembly.GetAttributes()
			.Where(attribute => attribute.AttributeClass?.Name == "SettingsSearchEntryAttribute")
			.Select(attribute => (
				((ITypeSymbol)attribute.ConstructorArguments[0].Value!).ToDisplayString(),
				(string?)attribute.ConstructorArguments[1].Value,
				(string)attribute.ConstructorArguments[2].Value!,
				(string?)attribute.ConstructorArguments[3].Value)),
	];

	/// <summary>Errors the generated file brought into the compilation, such as a page type it named wrongly.</summary>
	public IReadOnlyList<Diagnostic> Errors =>
		[.. Output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];

	/// <param name="files">Paths relative to <see cref="ProjectDirectory"/> with their markup.</param>
	/// <param name="pages">C# the compilation holds besides the attribute, usually the code-behind of the pages.</param>
	/// <param name="withAttribute">False for a project that cannot see the attribute.</param>
	public static GeneratorRun Of(IEnumerable<(string Path, string Markup)> files, string pages = "", bool withAttribute = true)
	{
		List<SyntaxTree> trees = [CSharpSyntaxTree.ParseText(pages, path: "Pages.cs")];
		if (withAttribute)
		{
			trees.Add(CSharpSyntaxTree.ParseText(AttributeSource, path: "Attribute.cs"));
		}

		CSharpCompilation compilation = CSharpCompilation.Create(
			"Demo",
			trees,
			References,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

		GeneratorDriver driver = CSharpGeneratorDriver.Create(
			[new SettingsSearchGenerator().AsSourceGenerator()],
			[.. files.Select(file => (AdditionalText)new MarkupFile($"{ProjectDirectory}/{file.Path}", file.Markup))],
			optionsProvider: new ProjectOptions());

		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
		GeneratorDriverRunResult result = driver.GetRunResult();
		return new GeneratorRun(output, [.. result.GeneratedTrees.Select(tree => tree.ToString())]);
	}

	public static GeneratorRun Of(string path, string markup, string pages = "") => Of([(path, markup)], pages);

	private sealed class MarkupFile(string path, string markup) : AdditionalText
	{
		public override string Path { get; } = path;

		public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(markup);
	}

	private sealed class ProjectOptions : AnalyzerConfigOptionsProvider
	{
		public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["build_property.RootNamespace"] = "Demo",
			["build_property.MSBuildProjectDirectory"] = ProjectDirectory,
		});

		public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Options.Empty;

		public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Options.Empty;
	}

	private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
	{
		public static Options Empty { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal));

		public override bool TryGetValue(string key, out string value)
		{
			bool found = values.TryGetValue(key, out string? stored);
			value = stored ?? "";
			return found;
		}
	}
}
