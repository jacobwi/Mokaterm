using Mokaterm.UI.FileBrowser.Editing;

namespace Mokaterm.UI.FileBrowser.Tests;

/// <summary>
/// Which language the editor colours a file in: its whole name first, then nginx's own folder, then the extension, then
/// a #! line. Server files are often named without an extension, which is why the order matters.
/// </summary>
public sealed class EditorLanguagesTests
{
	[Theory]
	[InlineData("/srv/app/Dockerfile", "dockerfile")]
	[InlineData("/srv/app/Dockerfile.prod", "dockerfile")]
	[InlineData("/srv/app/Containerfile", "dockerfile")]
	[InlineData("/srv/app/Makefile", "makefile")]
	[InlineData("/srv/app/makefile", "makefile")]
	[InlineData("/srv/app/Makefile.in", "makefile")]
	[InlineData("/etc/nginx/nginx.conf", "nginx")]
	[InlineData("/etc/nginx/sites-available/default", "nginx")]
	[InlineData("/etc/nginx/sites-enabled/example.com", "nginx")]
	[InlineData("/etc/nginx/mime.types", "nginx")]
	[InlineData("/etc/nginx/html/index.html", "html")]
	[InlineData("/tmp/nginx.conf.default", "nginx")]
	[InlineData("/etc/ssh/sshd_config", "ini")]
	[InlineData("/home/abc/.ssh/config", "ini")]
	[InlineData("/home/abc/.gitconfig", "ini")]
	[InlineData("/etc/systemd/system/app.service", "ini")]
	[InlineData("/etc/mysql/my.cnf", "ini")]
	[InlineData("/home/abc/.bashrc", "shell")]
	[InlineData("/home/abc/.profile", "shell")]
	[InlineData("/home/abc/.zshrc", "shell")]
	[InlineData("/srv/app/.env", "shell")]
	[InlineData("/srv/app/.env.production", "shell")]
	[InlineData("/etc/default/grub", "shell")]
	[InlineData("/etc/init.d/app", "shell")]
	[InlineData("/srv/app/deploy.sh", "shell")]
	[InlineData("/srv/app/package.json", "json")]
	[InlineData("/srv/app/compose.YAML", "yaml")]
	[InlineData("/srv/app/docker-compose.yml.j2", "yaml")]
	[InlineData("/srv/app/pyproject.toml", "toml")]
	[InlineData("/srv/app/Cargo.lock", "toml")]
	[InlineData("/srv/app/logo.svg", "xml")]
	[InlineData("/srv/app/web.config", "xml")]
	[InlineData("/srv/app/app.py", "python")]
	[InlineData("/srv/app/server.mjs", "javascript")]
	[InlineData("/srv/app/main.tsx", "typescript")]
	[InlineData("/srv/app/site.scss", "css")]
	[InlineData("/srv/app/schema.sql", "sql")]
	[InlineData("/srv/app/README.md", "markdown")]
	[InlineData("/srv/app/notes.txt", "plain")]
	[InlineData("/srv/app/LICENSE", "plain")]
	[InlineData("/", "plain")]
	public void TheNameDecides(string path, string expected) =>
		Assert.Equal(expected, EditorLanguages.Detect(path, "").Id);

	[Theory]
	[InlineData("#!/bin/sh\necho hi", "shell")]
	[InlineData("#!/usr/bin/env bash\n", "shell")]
	[InlineData("#! /bin/bash -e\r\n", "shell")]
	[InlineData("#!/bin/busybox sh\n", "shell")]
	[InlineData("#!/usr/bin/env python3\n", "python")]
	[InlineData("#!/usr/bin/python3.12\n", "python")]
	[InlineData("#!/usr/bin/env -S node --no-warnings\n", "javascript")]
	[InlineData("#!/usr/bin/env NODE_ENV=production deno run\n", "javascript")]
	[InlineData("#!/usr/bin/env ts-node\n", "typescript")]
	[InlineData("#!/usr/bin/make -f\n", "makefile")]
	[InlineData("#!/usr/bin/perl\n", "plain")]
	[InlineData("#!/usr/bin/env\n", "plain")]
	[InlineData("#!", "plain")]
	[InlineData("echo no shebang", "plain")]
	public void WithoutATellingName_TheShebangDecides(string text, string expected) =>
		Assert.Equal(expected, EditorLanguages.Detect("/usr/local/bin/tool", text).Id);

	[Fact]
	public void AnExtension_WinsOverTheShebang() =>
		Assert.Equal("python", EditorLanguages.Detect("/srv/app/run.py", "#!/bin/sh\n").Id);

	[Fact]
	public void PlainTextComesFirst_AndEveryIdIsDistinct()
	{
		Assert.Same(EditorLanguages.Plain, EditorLanguages.All[0]);
		Assert.Equal(EditorLanguages.All.Count, EditorLanguages.All.Select(language => language.Id).Distinct(StringComparer.Ordinal).Count());
	}

	[Fact]
	public void AnUnknownId_IsPlainText()
	{
		Assert.Same(EditorLanguages.Shell, EditorLanguages.Find("shell"));
		Assert.Same(EditorLanguages.Plain, EditorLanguages.Find("cobol"));
	}
}
