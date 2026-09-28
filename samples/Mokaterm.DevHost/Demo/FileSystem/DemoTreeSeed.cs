using System.Text;

namespace Mokaterm.DevHost.Demo.FileSystem;

/// <summary>
/// The tree a demo session starts with: a lived-in home folder, plus enough of /etc, /var, /srv, /root and /tmp to try
/// permissions, links, long listings and big transfers.
/// </summary>
internal static class DemoTreeSeed
{
	private const long KiB = 1024;
	private const long MiB = 1024 * KiB;
	private const long GiB = 1024 * MiB;
	private const int LogFileCount = 350;
	private const int LogHeadBytes = 3 * 1024;

	private static readonly string[] SystemUsers = ["root", "daemon", "bin", "sys", "www-data", "syslog", "postgres", "nobody"];

	private static readonly string[] SystemGroups = ["root", "daemon", "bin", "sys", "adm", "www-data", "syslog", "postgres", "sudo", "users", "nogroup"];

	private static readonly byte[] PdfHead = [.. "%PDF-1.7\n"u8, 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A];

	private static readonly byte[] ZipHead = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00, 0x08, 0x00, 0x00, 0x00, 0x21, 0x00];

	public static DemoFileTree Create(DemoAccount login, string hostName, TimeProvider timeProvider)
	{
		DateTimeOffset now = timeProvider.GetUtcNow();
		DateTimeOffset installed = now.AddDays(-212).AddHours(-5);
		Owner root = new("root", "root");

		DemoNode tree = DemoNode.NewDirectory("", root.User, root.Group, DemoModes.Octal("755"), installed);
		AddEtc(Folder(tree, "etc", root, "755", now.AddDays(-3)), login, hostName, installed, now);

		DemoNode home = Folder(tree, "home", root, "755", installed);
		DemoNode rootHome = Folder(tree, "root", root, "700", now.AddDays(-9));
		Text(rootHome, ".bashrc", root, "644", installed, RootBashrc);

		DemoNode www = Folder(Folder(tree, "srv", root, "755", installed), "www", root, "755", now.AddDays(-21));
		Text(www, "index.html", root, "644", now.AddDays(-21), IndexHtml.Replace("{host}", hostName, StringComparison.Ordinal));

		Folder(tree, "tmp", root, "1777", now.AddMinutes(-3));

		DemoNode log = Folder(Folder(tree, "var", root, "755", installed), "log", root, "755", now);
		Text(log, "syslog", new Owner("syslog", "adm"), "644", now, DemoLog.Lines(80, now, 1, hostName, login.Name));
		Text(log, "auth.log", new Owner("root", "adm"), "640", now.AddMinutes(-12), DemoLog.Lines(40, now.AddMinutes(-12), 500, hostName, login.Name));

		DemoNode loginHome = login.IsRoot ? rootHome : Folder(home, login.Name, new Owner(login.Name, login.Group), "750", now.AddHours(-1));
		AddHome(loginHome, new Owner(login.Name, login.Group), hostName, installed, now);

		return new DemoFileTree(tree, [.. SystemUsers, login.Name], [.. SystemGroups, login.Group], timeProvider);
	}

	private static void AddEtc(DemoNode etc, DemoAccount login, string hostName, DateTimeOffset installed, DateTimeOffset now)
	{
		Owner root = new("root", "root");
		Text(etc, "hosts", root, "644", now.AddDays(-40), Hosts.Replace("{host}", hostName, StringComparison.Ordinal));
		Text(etc, "fstab", root, "644", installed, Fstab);

		string passwd = login.IsRoot ? Passwd : Passwd + $"{login.Name}:x:1000:1000:{login.Name},,,:/home/{login.Name}:/bin/bash\n";
		Text(etc, "passwd", root, "644", now.AddDays(-30), passwd);

		DemoNode nginx = Folder(etc, "nginx", root, "755", now.AddDays(-21));
		Text(nginx, "nginx.conf", root, "644", now.AddDays(-21), NginxConf);
		Text(Folder(nginx, "sites-available", root, "755", now.AddDays(-21)), "default", root, "644", now.AddDays(-21), NginxSite);
		Link(Folder(nginx, "sites-enabled", root, "755", now.AddDays(-21)), "default", "/etc/nginx/sites-available/default", root, now.AddDays(-21));

		Text(Folder(etc, "ssh", root, "755", installed), "sshd_config", root, "600", now.AddDays(-60), SshdConfig);
	}

	private static void AddHome(DemoNode home, Owner user, string hostName, DateTimeOffset installed, DateTimeOffset now)
	{
		Text(home, ".bashrc", user, "644", installed, UserBashrc);
		Text(home, ".profile", user, "644", installed, Profile);
		Text(home, ".bash_history", user, "600", now.AddHours(-1), BashHistory);

		DemoNode ssh = Folder(home, ".ssh", user, "700", now.AddDays(-30));
		Text(ssh, "authorized_keys", user, "600", now.AddDays(-30), $"ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIGq3v0mK8x2dN5bQ7hT1cL9rW4yE6uF0sJ2pA8zX3nVd {user.User}@laptop\n");
		Text(ssh, "known_hosts", user, "644", now.AddDays(-2), KnownHosts);

		DemoNode projects = Folder(home, "projects", user, "755", now.AddDays(-5));
		DemoNode mokaterm = Folder(projects, "mokaterm", user, "755", now.AddDays(-1));
		Text(mokaterm, "README.md", user, "644", now.AddDays(-12), Readme);
		DemoNode src = Folder(mokaterm, "src", user, "755", now.AddHours(-20));
		Text(src, "Program.cs", user, "644", now.AddHours(-20), ProgramCs);
		Text(src, "SessionManager.cs", user, "644", now.AddDays(-2), SessionManagerCs);
		Text(src, "Session.cs", user, "644", now.AddDays(-6), SessionCs);
		Text(src, "Dockerfile", user, "644", now.AddDays(-15), Dockerfile);
		Folder(projects, "archive", user, "755", now.AddDays(-90));

		AddLogs(Folder(home, "logs", user, "755", now.AddMinutes(-40)), user, hostName, now);

		DemoNode downloads = Folder(home, "downloads", user, "755", now.AddDays(-2));
		Blob(downloads, "ubuntu-24.04.iso", user, "644", now.AddDays(-45), 6 * GiB);
		Blob(downloads, "backup-2026-09-01.tar.gz", user, "644", now.AddDays(-16), GiB + (215 * MiB));
		Blob(downloads, "invoice-2026-08.pdf", user, "644", now.AddDays(-11), 184 * KiB, PdfHead);
		Blob(downloads, "Quarterly report (final) v2.xlsx", user, "644", now.AddDays(-4), 48_732, ZipHead);
		Text(downloads, "2026-09-12_customer-export_all-regions_including-archived-accounts_and-cancelled-subscriptions_final.csv", user, "644", now.AddDays(-5), CustomerExport);
		Text(downloads, "\u00DCberpr\u00FCfung r\u00E9sum\u00E9 \u65E5\u672C\u8A9E \U0001F680.md", user, "644", now.AddDays(-2), UnicodeNotes);

		Text(home, "notes.md", user, "644", now.AddHours(-3), Notes);
		Text(home, "run.sh", user, "755", now.AddDays(-8), RunScript);
		Link(home, "current", "projects/mokaterm", user, now.AddDays(-1));
		Link(home, "old-release", "/opt/releases/2025-12-01", user, now.AddDays(-160));
	}

	private static void AddLogs(DemoNode logs, Owner user, string hostName, DateTimeOffset now)
	{
		for (int number = 1; number <= LogFileCount; number++)
		{
			ulong seed = DemoRandom.Seed(hostName) + (ulong)number;
			long size = DemoRandom.Next(seed, 100) switch
			{
				< 70 => (2 * KiB) + DemoRandom.Next(seed + 1, (int)(400 * KiB)),
				< 95 => (400 * KiB) + DemoRandom.Next(seed + 2, (int)(8 * MiB)),
				_ => (8 * MiB) + DemoRandom.Next(seed + 3, (int)(56 * MiB)),
			};

			DateTimeOffset modified = now.AddHours(-2 * (LogFileCount - number)).AddMinutes(-DemoRandom.Next(seed + 4, 110));
			byte[] head = Encoding.UTF8.GetBytes(DemoLog.Lines(30, modified, seed * 31, hostName, user.User));
			byte[] content = head.AsSpan(0, (int)Math.Min(Math.Min(head.Length, LogHeadBytes), size)).ToArray();
			Blob(logs, $"app-2026-09-{number:000}.log", user, "644", modified, size, content);
		}
	}

	private static DemoNode Folder(DemoNode parent, string name, Owner owner, string mode, DateTimeOffset modified) =>
		parent.Add(DemoNode.NewDirectory(name, owner.User, owner.Group, DemoModes.Octal(mode), modified));

	private static DemoNode Text(DemoNode parent, string name, Owner owner, string mode, DateTimeOffset modified, string text)
	{
		byte[] content = Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n"));
		return parent.Add(DemoNode.NewFile(name, owner.User, owner.Group, DemoModes.Octal(mode), modified, content.Length, content));
	}

	private static DemoNode Blob(DemoNode parent, string name, Owner owner, string mode, DateTimeOffset modified, long size, byte[]? head = null) =>
		parent.Add(DemoNode.NewFile(name, owner.User, owner.Group, DemoModes.Octal(mode), modified, size, head ?? []));

	private static DemoNode Link(DemoNode parent, string name, string target, Owner owner, DateTimeOffset modified) =>
		parent.Add(DemoNode.NewLink(name, target, owner.User, owner.Group, modified));

	private readonly record struct Owner(string User, string Group);

	private const string Hosts = """
		127.0.0.1	localhost
		127.0.1.1	{host}
		10.10.2.3	build-server
		192.168.1.20	nas

		# The following lines are desirable for IPv6 capable hosts
		::1	ip6-localhost ip6-loopback
		ff02::1	ip6-allnodes
		ff02::2	ip6-allrouters

		""";

	private const string Fstab = """
		# /etc/fstab: static file system information.
		# <file system>                           <mount point>  <type>  <options>                            <dump> <pass>
		UUID=0b6d5c1e-8f0a-4d7e-9c3b-2a1f4e5d6c7b /              ext4    errors=remount-ro                    0      1
		UUID=4A1B-2C3D                            /boot/efi      vfat    umask=0077                           0      1
		/swap.img                                 none           swap    sw                                   0      0
		//192.168.1.20/backups                    /mnt/backups   cifs    credentials=/etc/nas.cred,uid=1000   0      0

		""";

	private const string Passwd = """
		root:x:0:0:root:/root:/bin/bash
		daemon:x:1:1:daemon:/usr/sbin:/usr/sbin/nologin
		bin:x:2:2:bin:/bin:/usr/sbin/nologin
		sys:x:3:3:sys:/dev:/usr/sbin/nologin
		www-data:x:33:33:www-data:/var/www:/usr/sbin/nologin
		syslog:x:104:111::/home/syslog:/usr/sbin/nologin
		postgres:x:113:120:PostgreSQL administrator,,,:/var/lib/postgresql:/bin/bash
		nobody:x:65534:65534:nobody:/nonexistent:/usr/sbin/nologin

		""";

	private const string NginxConf = """
		user www-data;
		worker_processes auto;
		pid /run/nginx.pid;

		events {
			worker_connections 768;
		}

		http {
			sendfile on;
			tcp_nopush on;
			client_max_body_size 64m;
			include /etc/nginx/mime.types;
			default_type application/octet-stream;

			access_log /var/log/nginx/access.log;
			error_log /var/log/nginx/error.log;

			gzip on;
			include /etc/nginx/sites-enabled/*;
		}

		""";

	private const string NginxSite = """
		server {
			listen 80 default_server;
			listen [::]:80 default_server;
			server_name _;

			root /srv/www;
			index index.html;

			location / {
				try_files $uri $uri/ =404;
			}

			location /api/ {
				proxy_pass http://127.0.0.1:5080;
				proxy_http_version 1.1;
				proxy_set_header Upgrade $http_upgrade;
				proxy_set_header Connection "upgrade";
			}
		}

		""";

	private const string SshdConfig = """
		Include /etc/ssh/sshd_config.d/*.conf

		Port 22
		PermitRootLogin prohibit-password
		PasswordAuthentication no
		KbdInteractiveAuthentication no
		UsePAM yes
		X11Forwarding no
		PrintMotd no
		AcceptEnv LANG LC_*
		Subsystem sftp /usr/lib/openssh/sftp-server

		""";

	private const string RootBashrc = """
		# ~/.bashrc: executed by bash(1) for non-login shells.

		export PS1='\h:\w\$ '
		umask 022

		alias ls='ls --color=auto'
		alias ll='ls -l'
		alias rm='rm -i'

		""";

	private const string IndexHtml = """
		<!DOCTYPE html>
		<html lang="en">
		<head>
			<meta charset="utf-8">
			<title>{host}</title>
		</head>
		<body>
			<h1>It works</h1>
			<p>Served by nginx on {host}.</p>
		</body>
		</html>

		""";

	private const string UserBashrc = """
		# ~/.bashrc: executed by bash(1) for non-login shells.

		# If not running interactively, don't do anything
		case $- in
		    *i*) ;;
		      *) return;;
		esac

		HISTCONTROL=ignoreboth
		HISTSIZE=1000
		HISTFILESIZE=2000
		shopt -s histappend checkwinsize

		PS1='\[\e]0;\u@\h: \w\a\]\[\033[01;32m\]\u@\h\[\033[00m\]:\[\033[01;34m\]\w\[\033[00m\]\$ '

		if [ -x /usr/bin/dircolors ]; then
		    eval "$(dircolors -b)"
		    alias ls='ls --color=auto'
		    alias grep='grep --color=auto'
		fi

		alias ll='ls -alF'
		alias la='ls -A'

		export DOTNET_CLI_TELEMETRY_OPTOUT=1
		export PATH="$HOME/.dotnet/tools:$PATH"

		""";

	private const string Profile = """
		# ~/.profile: executed by the command interpreter for login shells.

		if [ -n "$BASH_VERSION" ]; then
		    if [ -f "$HOME/.bashrc" ]; then
		        . "$HOME/.bashrc"
		    fi
		fi

		if [ -d "$HOME/.local/bin" ] ; then
		    PATH="$HOME/.local/bin:$PATH"
		fi

		""";

	private const string BashHistory = """
		cd projects/mokaterm
		git pull --rebase
		dotnet build -c Release
		docker compose up -d
		docker compose logs -f api
		sudo systemctl restart nginx
		tail -f /var/log/syslog
		df -h
		htop
		ls -la ~/downloads
		sha256sum downloads/ubuntu-24.04.iso
		scp downloads/backup-2026-09-01.tar.gz nas:/volume1/backups/
		exit

		""";

	private const string KnownHosts = """
		|1|F1E1KeoE/eEWhi10WpGv4OdiO6Y=|3988QV0VE8wmZL7suNrYQLITLCg= ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIBx7Lr2kPqN1c8vYjWm4sT0hD6gE9fU3aZ5oX2iK7bRe
		|1|tO1a2kJ3vLcBq9WzXr8mYpN4s6E=|hG5bT3yU1oL8kR2nP7cV9dS4wQ0= ecdsa-sha2-nistp256 AAAAE2VjZHNhLXNoYTItbmlzdHAyNTYAAAAIbmlzdHAyNTYAAABBBEm7Qp4wZk2

		""";

	private const string Readme = """
		# Mokaterm

		Modular SSH, SFTP and FTP client built with Blazor. One shared UI runs as a desktop app and as a web app.

		## Build

		    dotnet build Mokaterm.slnx

		## Run

		    dotnet run --project src/Hosts/Mokaterm.Web

		""";

	private const string ProgramCs = """
		using Mokaterm.Agent;

		WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
		builder.Services.AddSingleton<SessionManager>();

		WebApplication app = builder.Build();
		app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
		app.Run();

		""";

	private const string SessionManagerCs = """
		namespace Mokaterm.Agent;

		public sealed class SessionManager(ILogger<SessionManager> logger)
		{
			private readonly Dictionary<Guid, Session> _sessions = [];

			public Session Open(string user, int columns, int rows)
			{
				Session session = new(Guid.NewGuid(), user, columns, rows);
				_sessions.Add(session.Id, session);
				logger.LogInformation("Session {Id} opened for {User}", session.Id, user);
				return session;
			}
		}

		""";

	private const string SessionCs = """
		namespace Mokaterm.Agent;

		public sealed record Session(Guid Id, string User, int Columns, int Rows);

		""";

	private const string Dockerfile = """
		FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
		WORKDIR /src
		COPY . .
		RUN dotnet publish -c Release -o /app

		FROM mcr.microsoft.com/dotnet/aspnet:10.0
		WORKDIR /app
		COPY --from=build /app .
		EXPOSE 8080
		ENTRYPOINT ["dotnet", "Mokaterm.Agent.dll"]

		""";

	private const string CustomerExport = """
		id,region,account,status,mrr_eur,cancelled_at
		10421,eu-west,Acme GmbH,active,1290.00,
		10422,us-east,Globex LLC,cancelled,0.00,2026-08-14
		10423,ap-south,Initech Ltd,archived,0.00,2025-11-02

		""";

	private const string UnicodeNotes = """
		# Überprüfung

		Résumé der Prüfung, 日本語のメモ und eine Rakete 🚀.

		""";

	private const string Notes = """
		# Notes

		- rotate the deploy key before October
		- nginx: raise client_max_body_size to 512m for the release uploads
		- the backup job on nas last ran 2026-09-01, check why it stopped
		- ask about moving web01 to the new hypervisor

		""";

	private const string RunScript = """
		#!/usr/bin/env bash
		set -euo pipefail

		cd "$(dirname "$0")/projects/mokaterm"
		dotnet run --project src --urls http://0.0.0.0:5080

		""";
}
