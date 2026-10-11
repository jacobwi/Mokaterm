using System.Collections.Frozen;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.FileBrowser.Editing;

/// <summary>The languages the editor colours, and which one a file is written in.</summary>
internal static class EditorLanguages
{
	public static EditorLanguage Plain { get; } = new("plain", "Plain text");

	public static EditorLanguage Css { get; } = new("css", "CSS");

	public static EditorLanguage Dockerfile { get; } = new("dockerfile", "Dockerfile");

	public static EditorLanguage Html { get; } = new("html", "HTML");

	public static EditorLanguage Ini { get; } = new("ini", "INI / config");

	public static EditorLanguage JavaScript { get; } = new("javascript", "JavaScript");

	public static EditorLanguage Json { get; } = new("json", "JSON");

	public static EditorLanguage Makefile { get; } = new("makefile", "Makefile");

	public static EditorLanguage Markdown { get; } = new("markdown", "Markdown");

	public static EditorLanguage Nginx { get; } = new("nginx", "Nginx");

	public static EditorLanguage Python { get; } = new("python", "Python");

	public static EditorLanguage Shell { get; } = new("shell", "Shell");

	public static EditorLanguage Sql { get; } = new("sql", "SQL");

	public static EditorLanguage Toml { get; } = new("toml", "TOML");

	public static EditorLanguage TypeScript { get; } = new("typescript", "TypeScript");

	public static EditorLanguage Xml { get; } = new("xml", "XML");

	public static EditorLanguage Yaml { get; } = new("yaml", "YAML");

	/// <summary>Plain text first, since picking it is how colouring is turned off, then the rest by name.</summary>
	public static IReadOnlyList<EditorLanguage> All { get; } =
	[
		Plain, Css, Dockerfile, Html, Ini, JavaScript, Json, Makefile, Markdown, Nginx, Python, Shell, Sql, Toml, TypeScript, Xml, Yaml,
	];

	// Whole file names, looked up before any extension: a Dockerfile has none, and to an extension reader .bashrc is
	// nothing but extension.
	private static readonly FrozenDictionary<string, EditorLanguage> Names = new Dictionary<string, EditorLanguage>(StringComparer.OrdinalIgnoreCase)
	{
		["Dockerfile"] = Dockerfile,
		["Containerfile"] = Dockerfile,
		["Makefile"] = Makefile,
		["GNUmakefile"] = Makefile,
		["BSDmakefile"] = Makefile,
		["nginx.conf"] = Nginx,
		["sshd_config"] = Ini,
		["ssh_config"] = Ini,
		[".gitconfig"] = Ini,
		[".gitmodules"] = Ini,
		[".editorconfig"] = Ini,
		[".npmrc"] = Ini,
		[".pypirc"] = Ini,
		[".htaccess"] = Ini,
		[".wgetrc"] = Ini,
		[".hgrc"] = Ini,
		[".flake8"] = Ini,
		[".coveragerc"] = Ini,
		[".pylintrc"] = Ini,
		[".bashrc"] = Shell,
		[".bash_profile"] = Shell,
		[".bash_login"] = Shell,
		[".bash_logout"] = Shell,
		[".bash_aliases"] = Shell,
		[".bash_history"] = Shell,
		[".profile"] = Shell,
		[".zshrc"] = Shell,
		[".zshenv"] = Shell,
		[".zprofile"] = Shell,
		[".zlogin"] = Shell,
		[".zlogout"] = Shell,
		[".kshrc"] = Shell,
		[".mkshrc"] = Shell,
		[".shrc"] = Shell,
		[".envrc"] = Shell,
		[".env"] = Shell,
		[".xinitrc"] = Shell,
		[".xprofile"] = Shell,
		[".xsession"] = Shell,
		[".xsessionrc"] = Shell,
		["bashrc"] = Shell,
		["bash.bashrc"] = Shell,
		["profile"] = Shell,
		["environment"] = Shell,
		["crontab"] = Shell,
		["PKGBUILD"] = Shell,
		["APKBUILD"] = Shell,
		[".babelrc"] = Json,
		[".eslintrc"] = Json,
		[".jshintrc"] = Json,
		[".swcrc"] = Json,
		["composer.lock"] = Json,
		["flake.lock"] = Json,
		[".clang-format"] = Yaml,
		[".clang-tidy"] = Yaml,
		[".yamllint"] = Yaml,
		["Pipfile"] = Toml,
		["Cargo.lock"] = Toml,
		["poetry.lock"] = Toml,
		["uv.lock"] = Toml,
		["SConstruct"] = Python,
		["SConscript"] = Python,
		["BUILD.bazel"] = Python,
		["WORKSPACE.bazel"] = Python,
	}.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

	private static readonly FrozenDictionary<string, EditorLanguage> Extensions = new Dictionary<string, EditorLanguage>(StringComparer.OrdinalIgnoreCase)
	{
		["sh"] = Shell,
		["bash"] = Shell,
		["zsh"] = Shell,
		["ksh"] = Shell,
		["ash"] = Shell,
		["dash"] = Shell,
		["mksh"] = Shell,
		["bats"] = Shell,
		["command"] = Shell,
		["env"] = Shell,
		["ebuild"] = Shell,
		["eclass"] = Shell,
		["json"] = Json,
		["jsonc"] = Json,
		["json5"] = Json,
		["jsonl"] = Json,
		["ndjson"] = Json,
		["geojson"] = Json,
		["webmanifest"] = Json,
		["har"] = Json,
		["ipynb"] = Json,
		["code-workspace"] = Json,
		["yaml"] = Yaml,
		["yml"] = Yaml,
		["ini"] = Ini,
		["cfg"] = Ini,
		["conf"] = Ini,
		["cnf"] = Ini,
		["properties"] = Ini,
		["prefs"] = Ini,
		["desktop"] = Ini,
		["service"] = Ini,
		["socket"] = Ini,
		["timer"] = Ini,
		["target"] = Ini,
		["mount"] = Ini,
		["automount"] = Ini,
		["swap"] = Ini,
		["path"] = Ini,
		["slice"] = Ini,
		["scope"] = Ini,
		["network"] = Ini,
		["netdev"] = Ini,
		["container"] = Ini,
		["volume"] = Ini,
		["pod"] = Ini,
		["kube"] = Ini,
		["repo"] = Ini,
		["inf"] = Ini,
		["reg"] = Ini,
		["ovpn"] = Ini,
		["toml"] = Toml,
		["xml"] = Xml,
		["xsd"] = Xml,
		["xsl"] = Xml,
		["xslt"] = Xml,
		["svg"] = Xml,
		["plist"] = Xml,
		["mobileconfig"] = Xml,
		["csproj"] = Xml,
		["fsproj"] = Xml,
		["vbproj"] = Xml,
		["vcxproj"] = Xml,
		["proj"] = Xml,
		["props"] = Xml,
		["targets"] = Xml,
		["nuspec"] = Xml,
		["resx"] = Xml,
		["xaml"] = Xml,
		["axaml"] = Xml,
		["config"] = Xml,
		["wsdl"] = Xml,
		["rss"] = Xml,
		["atom"] = Xml,
		["kml"] = Xml,
		["gpx"] = Xml,
		["pom"] = Xml,
		["iml"] = Xml,
		["xlf"] = Xml,
		["xliff"] = Xml,
		["html"] = Html,
		["htm"] = Html,
		["xhtml"] = Html,
		["shtml"] = Html,
		["vue"] = Html,
		["svelte"] = Html,
		["nginx"] = Nginx,
		["dockerfile"] = Dockerfile,
		["containerfile"] = Dockerfile,
		["py"] = Python,
		["pyw"] = Python,
		["pyi"] = Python,
		["pyx"] = Python,
		["wsgi"] = Python,
		["bzl"] = Python,
		["star"] = Python,
		["js"] = JavaScript,
		["mjs"] = JavaScript,
		["cjs"] = JavaScript,
		["jsx"] = JavaScript,
		["ts"] = TypeScript,
		["mts"] = TypeScript,
		["cts"] = TypeScript,
		["tsx"] = TypeScript,
		["css"] = Css,
		["scss"] = Css,
		["sass"] = Css,
		["less"] = Css,
		["pcss"] = Css,
		["sql"] = Sql,
		["psql"] = Sql,
		["pgsql"] = Sql,
		["mysql"] = Sql,
		["plsql"] = Sql,
		["ddl"] = Sql,
		["dml"] = Sql,
		["hql"] = Sql,
		["md"] = Markdown,
		["markdown"] = Markdown,
		["mdown"] = Markdown,
		["mkd"] = Markdown,
		["mkdn"] = Markdown,
		["mdwn"] = Markdown,
		["mdx"] = Markdown,
		["mk"] = Makefile,
		["mak"] = Makefile,
		["make"] = Makefile,
	}.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

	// What a #! line runs, once the folder, any version number and env are taken off it.
	private static readonly FrozenDictionary<string, EditorLanguage> Interpreters = new Dictionary<string, EditorLanguage>(StringComparer.Ordinal)
	{
		["sh"] = Shell,
		["bash"] = Shell,
		["zsh"] = Shell,
		["ksh"] = Shell,
		["dash"] = Shell,
		["ash"] = Shell,
		["mksh"] = Shell,
		["yash"] = Shell,
		["python"] = Python,
		["pypy"] = Python,
		["node"] = JavaScript,
		["nodejs"] = JavaScript,
		["deno"] = JavaScript,
		["bun"] = JavaScript,
		["ts-node"] = TypeScript,
		["tsx"] = TypeScript,
		["make"] = Makefile,
		["gmake"] = Makefile,
	}.ToFrozenDictionary(StringComparer.Ordinal);

	// A copy kept beside the real file under a suffix of its own (nginx.conf.default, .env.example, app.yml.j2, what
	// package managers leave behind) is still in the language of the name before that suffix.
	private static readonly FrozenSet<string> CopySuffixes = new[]
	{
		"example", "sample", "dist", "template", "tmpl", "tpl", "j2", "jinja", "jinja2", "in", "bak", "orig", "old", "default",
		"save", "rpmnew", "rpmsave", "dpkg-dist", "dpkg-old", "dpkg-new", "ucf-dist", "pacnew", "pacsave",
	}.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

	// nginx names its files anything: sites-available/default, example.com, mime.types, ssl.conf.
	private static readonly FrozenSet<string> NginxFolders = new[] { "nginx", "openresty" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

	// Folders whose files are scripts, or KEY=value lines a shell sources.
	private static readonly FrozenSet<string> ShellFolders = new[]
	{
		"/etc/init.d", "/etc/cron.d", "/etc/cron.hourly", "/etc/cron.daily", "/etc/cron.weekly", "/etc/cron.monthly", "/etc/default",
		"/etc/sysconfig", "/etc/profile.d",
	}.ToFrozenSet(StringComparer.Ordinal);

	/// <summary>The language with this id, or <see cref="Plain"/>.</summary>
	public static EditorLanguage Find(string id)
	{
		foreach (EditorLanguage language in All)
		{
			if (string.Equals(language.Id, id, StringComparison.Ordinal))
			{
				return language;
			}
		}

		return Plain;
	}

	/// <summary>
	/// Picks the language from the file name, then from the folder it sits in, then from its extension, then from a
	/// <c>#!</c> on its first line. Anything else is plain text.
	/// </summary>
	public static EditorLanguage Detect(string path, string text)
	{
		string name = RemotePath.GetName(path);
		string folder = RemotePath.GetParent(path);
		return FromName(name, folder) ?? FromShebang(text) ?? Plain;
	}

	private static EditorLanguage? FromName(string name, string folder)
	{
		if (Names.TryGetValue(name, out EditorLanguage? named))
		{
			return named;
		}

		// Inside nginx's own folder only a file that is clearly something else (a page, a JSON or YAML file) is not nginx.
		string extension = ExtensionOf(name);
		if (InFolderNamed(folder, NginxFolders))
		{
			return Extensions.TryGetValue(extension, out EditorLanguage? other) && other != Ini ? other : Nginx;
		}

		if (string.Equals(name, "config", StringComparison.Ordinal) && string.Equals(RemotePath.GetName(folder), ".ssh", StringComparison.Ordinal))
		{
			return Ini;
		}

		if (extension.Length == 0 && ShellFolders.Contains(folder))
		{
			return Shell;
		}

		// .env.local and .env.production are the same KEY=value file as .env.
		if (name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase))
		{
			return Shell;
		}

		if (name.StartsWith("Dockerfile.", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Containerfile.", StringComparison.OrdinalIgnoreCase))
		{
			return Dockerfile;
		}

		if (Extensions.TryGetValue(extension, out EditorLanguage? byExtension))
		{
			return byExtension;
		}

		// Only one suffix comes off: a name made of nothing but copy suffixes is not worth a guess.
		if (CopySuffixes.Contains(extension) && name.Length > extension.Length + 1)
		{
			string original = name[..^(extension.Length + 1)];
			if (Names.TryGetValue(original, out EditorLanguage? originalName))
			{
				return originalName;
			}

			return Extensions.GetValueOrDefault(ExtensionOf(original));
		}

		return null;
	}

	/// <summary>What follows the last dot, or nothing for a name without one or a dotfile with only the leading dot.</summary>
	private static string ExtensionOf(string name)
	{
		int dot = name.LastIndexOf('.');
		return dot <= 0 ? "" : name[(dot + 1)..];
	}

	private static bool InFolderNamed(string folder, FrozenSet<string> names)
	{
		foreach (RemotePathSegment segment in RemotePath.GetSegments(folder))
		{
			if (names.Contains(segment.Name))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Reads <c>#!/bin/sh</c>, <c>#!/usr/bin/env python3</c> and <c>#!/usr/bin/env -S node --flag</c> alike: the program
	/// is the first word, unless that is env, whose own options and assignments come before it.
	/// </summary>
	internal static EditorLanguage? FromShebang(string text)
	{
		if (!text.StartsWith("#!", StringComparison.Ordinal))
		{
			return null;
		}

		int end = text.IndexOf('\n', StringComparison.Ordinal);
		string line = (end < 0 ? text : text[..end])[2..];
		string[] words = line.Split([' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries);
		int index = 0;
		string program = words.Length > 0 ? ProgramName(words[0]) : "";
		if (program is "env" or "busybox")
		{
			index = 1;
			while (index < words.Length && (words[index].StartsWith('-') || words[index].Contains('=', StringComparison.Ordinal)))
			{
				index++;
			}

			program = index < words.Length ? ProgramName(words[index]) : "";
		}

		return Interpreters.GetValueOrDefault(program);
	}

	/// <summary>The last path segment without a version: <c>/usr/bin/python3.12</c> is python.</summary>
	private static string ProgramName(string word)
	{
		string name = word[(word.LastIndexOf('/') + 1)..];
		return name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.');
	}
}
