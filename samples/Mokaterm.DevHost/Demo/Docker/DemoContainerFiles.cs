using System.Text;
using Mokaterm.DevHost.Demo.FileSystem;

namespace Mokaterm.DevHost.Demo.Docker;

/// <summary>
/// What is inside the demo's containers, for the file browser over a container: the nginx container holds an Alpine
/// layout with its config, its site and a file name with spaces, the API container is distroless and has no shell, and
/// every other container gets a small Debian-like tree. Each tree lives as long as the engine, so a file saved into a
/// container is there when the browser lists it again.
/// </summary>
internal static class DemoContainerFiles
{
	/// <summary>The accounts every demo container knows, as its <c>/etc/passwd</c> lists them.</summary>
	public const string Passwd = """
		root:x:0:0:root:/root:/bin/sh
		daemon:x:1:1:daemon:/usr/sbin:/sbin/nologin
		nginx:x:101:101:nginx:/var/cache/nginx:/sbin/nologin
		nobody:x:65534:65534:nobody:/:/sbin/nologin
		""";

	public const string Group = """
		root:x:0:
		daemon:x:1:
		nginx:x:101:nginx
		nogroup:x:65534:
		""";

	private const string NginxConf = """
		user  nginx;
		worker_processes  auto;

		error_log  /var/log/nginx/error.log notice;
		pid        /var/run/nginx.pid;

		events {
		    worker_connections  1024;
		}

		http {
		    include       /etc/nginx/mime.types;
		    default_type  application/octet-stream;
		    sendfile        on;
		    keepalive_timeout  65;
		    include /etc/nginx/conf.d/*.conf;
		}
		""";

	private const string DefaultSite = """
		server {
		    listen       80;
		    server_name  localhost;

		    location / {
		        root   /usr/share/nginx/html;
		        index  index.html;
		    }

		    location /api/ {
		        proxy_pass http://api:5000/;
		    }
		}
		""";

	private const string IndexHtml = """
		<!DOCTYPE html>
		<html>
		<head><title>Acme Shop</title></head>
		<body>
		<h1>Acme Shop</h1>
		<p>Served by nginx in the shop-web container.</p>
		</body>
		</html>
		""";

	private const string OsRelease = """
		NAME="Alpine Linux"
		ID=alpine
		VERSION_ID=3.20.3
		PRETTY_NAME="Alpine Linux v3.20"
		""";

	/// <summary>The tree inside <paramref name="container"/>, or null for an image that has no shell to browse with.</summary>
	public static DemoFileTree? For(DemoDockerContainer container, TimeProvider time)
	{
		ArgumentNullException.ThrowIfNull(container);
		if (container.Image.Contains("shop-api", StringComparison.Ordinal))
		{
			return null;
		}

		DateTimeOffset now = time.GetUtcNow();
		DateTimeOffset built = container.Created;
		DemoNode root = DemoNode.NewDirectory("", "root", "root", DemoModes.Octal("755"), built);
		DemoNode etc = Folder(root, "etc", "755", built);
		Text(etc, "passwd", "644", built, Passwd);
		Text(etc, "group", "644", built, Group);
		Text(etc, "hostname", "644", container.StartedAt, container.Id[..12] + "\n");
		Text(etc, "os-release", "644", built, OsRelease);
		Folder(root, "root", "700", built);
		Folder(root, "tmp", "1777", now.AddMinutes(-5));
		DemoNode bin = Folder(root, "bin", "755", built);
		Text(bin, "sh", "755", built, "#!busybox\n");
		DemoNode var = Folder(root, "var", "755", built);
		DemoNode log = Folder(var, "log", "755", built);

		if (container.Image.StartsWith("nginx", StringComparison.Ordinal))
		{
			DemoNode nginx = Folder(etc, "nginx", "755", built);
			Text(nginx, "nginx.conf", "644", built, NginxConf);
			Text(nginx, "mime.types", "644", built, "types {\n    text/html html htm;\n    text/css css;\n    application/javascript js;\n}\n");
			Text(Folder(nginx, "conf.d", "755", built), "default.conf", "644", now.AddDays(-2), DefaultSite);

			DemoNode html = Folder(Folder(Folder(root, "usr", "755", built), "share", "755", built), "nginx", "755", built).Add(
				DemoNode.NewDirectory("html", "root", "root", DemoModes.Octal("755"), now.AddDays(-2)));
			Text(html, "index.html", "644", now.AddDays(-2), IndexHtml);
			Text(html, "50x.html", "644", built, "<!DOCTYPE html>\n<html><body><h1>An error occurred.</h1></body></html>\n");
			Text(html, "Release notes 2.4.md", "644", now.AddDays(-2), "# 2.4\n\n- Faster checkout\n- Fixed the basket total\n");

			DemoNode nginxLog = Folder(log, "nginx", "755", built);
			nginxLog.Add(DemoNode.NewLink("access.log", "/dev/stdout", "root", "root", built));
			nginxLog.Add(DemoNode.NewLink("error.log", "/dev/stderr", "root", "root", built));
			Text(root, "docker-entrypoint.sh", "775", built, "#!/bin/sh\nset -e\nexec \"$@\"\n");
			Folder(Folder(Folder(var, "cache", "755", built), "nginx", "700", built), "client_temp", "700", now.AddHours(-1), "nginx");
		}
		else
		{
			Text(log, "startup.log", "644", container.StartedAt, $"{container.Name} started\n");
			Folder(root, "data", "755", now.AddHours(-3));
		}

		return new DemoFileTree(root, ["root", "daemon", "nginx", "nobody"], ["root", "daemon", "nginx", "nogroup"], time);
	}

	private static DemoNode Folder(DemoNode parent, string name, string mode, DateTimeOffset modified, string owner = "root") =>
		parent.Add(DemoNode.NewDirectory(name, owner, owner, DemoModes.Octal(mode), modified));

	private static void Text(DemoNode parent, string name, string mode, DateTimeOffset modified, string text)
	{
		byte[] content = Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n"));
		parent.Add(DemoNode.NewFile(name, "root", "root", DemoModes.Octal(mode), modified, content.Length, content));
	}
}
