namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>What docker 27 and compose 2.29 print, trimmed to the fields the manager reads, with names chosen to be awkward.</summary>
internal static class DockerFixtures
{
	/// <summary><c>docker ps --all --no-trunc --format '{{json .}}'</c>: one object per line, with a warning line among them.</summary>
	public const string Containers = """
		{"Command":"\"/docker-entrypoint.sh nginx -g 'daemon off;'\"","CreatedAt":"2026-10-09 07:00:00 +0000 UTC","ID":"0a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f9","Image":"nginx:1.27-alpine","Labels":"com.docker.compose.project=shop,com.docker.compose.service=web,com.docker.compose.project.working_dir=/srv/shop,com.docker.compose.project.config_files=/srv/shop/compose.yaml,/srv/shop/compose.prod.yaml,com.docker.compose.oneoff=False","LocalVolumes":"0","Mounts":"","Names":"shop-web-1","Networks":"shop_default","Ports":"0.0.0.0:8080->80/tcp, :::8080->80/tcp","RunningFor":"2 days ago","Size":"","State":"running","Status":"Up 2 days (healthy)"}
		WARNING: Error loading config file: /home/ada/.docker/config.json: permission denied
		{"Command":"\"docker-entrypoint.sh postgres\"","CreatedAt":"2026-10-09 07:00:00 +0000 UTC","ID":"f27c9f265ce1aa","Image":"postgres:16","Labels":"com.docker.compose.project=shop,com.docker.compose.service=db,com.docker.compose.project.working_dir=/srv/shop","Mounts":"shop_pgdata","Names":"shop-db-1","Networks":"shop_default,monitoring","Ports":"127.0.0.1:5432->5432/tcp","State":"running","Status":"Up 2 days (unhealthy)"}
		{"Command":"\"redis-server\"","ID":"9f8e7d6c5b4a39281706f5e4d3c2b1a0","Image":"redis:7.4","Labels":"","Names":"a.b_c-d,old/alias","Networks":"bridge","Ports":"6379/tcp","State":"exited","Status":"Exited (137) 3 hours ago"}
		{"Command":"\"/run.sh\"","ID":"1122334455667788","Image":"grafana/grafana:11.2.0","Labels":"com.docker.compose.project=monitoring,com.docker.compose.service=grafana,com.docker.compose.project.working_dir=/opt/monitoring,com.docker.compose.project.config_files=docker-compose.yml,com.docker.compose.project.environment_file=/opt/monitoring/.env.prod","Names":"monitoring-grafana-1","Networks":"monitoring","Ports":"0.0.0.0:3000->3000/tcp","Status":"Up 5 hours (Paused)"}
		{"Command":"\"sh\"","ID":"aabbccddeeff0011","Image":"alpine:3.20","Labels":"com.docker.compose.project=shop,com.docker.compose.service=web,com.docker.compose.oneoff=True","Names":"shop-web-run-1a2b","Networks":"shop_default","State":"exited","Status":"Exited (0) 1 hour ago"}
		""";

	/// <summary><c>docker container inspect</c> of one container: an array, as docker prints it.</summary>
	public const string Inspect = """
		[
		  {
		    "Id": "f27c9f265ce1aa",
		    "Created": "2026-10-09T07:00:00.123456789Z",
		    "Name": "/shop-db-1",
		    "Image": "sha256:4ca2d821beb2",
		    "RestartCount": 2,
		    "State": {
		      "Status": "running",
		      "Pid": 4242,
		      "ExitCode": 0,
		      "Error": "",
		      "OOMKilled": false,
		      "StartedAt": "2026-10-09T07:00:01.5Z",
		      "FinishedAt": "0001-01-01T00:00:00Z",
		      "Health": {
		        "Status": "unhealthy",
		        "FailingStreak": 3,
		        "Log": [
		          { "Start": "2026-10-11T07:00:00Z", "ExitCode": 1, "Output": "pg_isready: no response\n" }
		        ]
		      }
		    },
		    "HostConfig": {
		      "NetworkMode": "shop_default",
		      "Privileged": false,
		      "RestartPolicy": { "Name": "on-failure", "MaximumRetryCount": 3 }
		    },
		    "Mounts": [
		      { "Type": "volume", "Name": "shop_pgdata", "Source": "/var/lib/docker/volumes/shop_pgdata/_data", "Destination": "/var/lib/postgresql/data", "Mode": "z", "RW": true },
		      { "Type": "bind", "Source": "/srv/shop/initdb", "Destination": "/docker-entrypoint-initdb.d", "Mode": "ro", "RW": false }
		    ],
		    "Config": {
		      "Hostname": "f27c9f265ce1",
		      "User": "postgres",
		      "Env": [ "POSTGRES_PASSWORD=s3cr3t-demo", "POSTGRES_USER=shop", "EMPTY=", "PATH=/usr/local/bin:/usr/bin" ],
		      "Cmd": [ "postgres" ],
		      "Image": "postgres:16",
		      "WorkingDir": "/",
		      "Entrypoint": [ "docker-entrypoint.sh" ],
		      "Labels": { "com.docker.compose.project": "shop", "com.docker.compose.service": "db" }
		    },
		    "NetworkSettings": {
		      "Ports": {
		        "5432/tcp": [ { "HostIp": "127.0.0.1", "HostPort": "5432" } ],
		        "8008/tcp": null
		      },
		      "Networks": {
		        "shop_default": { "IPAddress": "172.18.0.3", "Gateway": "172.18.0.1", "MacAddress": "02:42:ac:12:00:03", "GlobalIPv6Address": "", "Aliases": [ "shop-db-1", "db" ] }
		      }
		    }
		  }
		]
		""";

	/// <summary><c>docker image ls --no-trunc --format '{{json .}}'</c>, a dangling image and one kept by digest included.</summary>
	public const string Images = """
		{"Containers":"N/A","CreatedAt":"2026-08-12 10:00:00 +0000 UTC","CreatedSince":"2 months ago","Digest":"\u003cnone\u003e","ID":"sha256:aaaaaaaaaaaabbbbbbbbbbbbccccccccccccddddddddddddeeeeeeeeeeeeffff","Repository":"nginx","SharedSize":"N/A","Size":"48.3MB","Tag":"1.27-alpine","UniqueSize":"N/A","VirtualSize":"48.3MB"}
		{"CreatedSince":"9 days ago","Digest":"\u003cnone\u003e","ID":"sha256:4ca2d821beb2aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","Repository":"\u003cnone\u003e","Size":"198MB","Tag":"\u003cnone\u003e"}
		{"CreatedSince":"3 weeks ago","Digest":"sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","ID":"sha256:9999999999998888888888887777777777776666666666665555555555554444","Repository":"registry.local:5000/team/app","Size":"1.2GB","Tag":"\u003cnone\u003e"}
		""";

	public const string Volumes = """
		{"Driver":"local","Labels":"com.docker.compose.project=shop","Links":"N/A","Mountpoint":"/var/lib/docker/volumes/shop_pgdata/_data","Name":"shop_pgdata","Scope":"local","Size":"N/A"}
		{"Driver":"local","Labels":"","Mountpoint":"/var/lib/docker/volumes/old-cache/_data","Name":"old-cache","Scope":"local"}
		""";

	public const string Networks = """
		{"CreatedAt":"2026-10-09 07:00:00 +0000 UTC","Driver":"bridge","ID":"4aecec00112233445566778899aabbccddeeff00112233445566778899aabbcc","IPv6":"false","Internal":"false","Labels":"","Name":"shop_default","Scope":"local"}
		{"Driver":"bridge","ID":"17f29b00112233445566778899aabbccddeeff00112233445566778899aabbcc","IPv6":"false","Internal":"false","Name":"bridge","Scope":"local"}
		{"Driver":"host","ID":"4740ae6000112233445566778899aabbccddeeff00112233445566778899aabb","Name":"host","Scope":"local"}
		{"Driver":"bridge","ID":"c49fea7000112233445566778899aabbccddeeff00112233445566778899aabb","Internal":"true","Name":"legacy_net","Scope":"local"}
		""";

	public const string Stats = """
		{"BlockIO":"12.3MB / 4.1MB","CPUPerc":"0.52%","Container":"0a1b2c3d4e5f","ID":"0a1b2c3d4e5f","MemPerc":"1.23%","MemUsage":"10.2MiB / 7.6GiB","Name":"shop-web-1","NetIO":"1.2kB / 0B","PIDs":"5"}
		{"BlockIO":"--","CPUPerc":"--","Container":"9f8e7d6c5b4a","MemPerc":"--","MemUsage":"-- / --","Name":"--","NetIO":"--","PIDs":"--"}
		""";

	public const string DiskUsage = """
		{"Active":"6","Reclaimable":"282.3MB (13%)","Size":"1.49GB","TotalCount":"8","Type":"Images"}
		{"Active":"4","Reclaimable":"18kB (1%)","Size":"1.2MB","TotalCount":"6","Type":"Containers"}
		{"Active":"2","Reclaimable":"212MB (14%)","Size":"1.43GB","TotalCount":"5","Type":"Local Volumes"}
		{"Active":"0","Reclaimable":"642MB","Size":"642MB","TotalCount":"31","Type":"Build Cache"}
		""";

	/// <summary><c>docker compose ls --all --format json</c>: one array.</summary>
	public const string ComposeList = """
		[{"Name":"monitoring","Status":"paused(1)","ConfigFiles":"/opt/monitoring/docker-compose.yml"},{"Name":"shop","Status":"running(2)","ConfigFiles":"/srv/shop/compose.yaml,/srv/shop/compose.prod.yaml"},{"Name":"gone","Status":"exited(0)","ConfigFiles":""}]
		""";

	/// <summary><c>docker compose ps --all --format json</c> as 2.21 and later print it: one object per line.</summary>
	public const string ComposePsLines = """
		{"Command":"\"/docker-entrypoint.sh nginx\"","CreatedAt":"2026-10-09 07:00:00 +0000 UTC","ExitCode":0,"Health":"healthy","ID":"0a1b2c3d4e5f","Image":"nginx:1.27-alpine","Name":"shop-web-1","Project":"shop","Publishers":[{"URL":"0.0.0.0","TargetPort":80,"PublishedPort":8080,"Protocol":"tcp"},{"URL":"::","TargetPort":80,"PublishedPort":8080,"Protocol":"tcp"},{"URL":"","TargetPort":443,"PublishedPort":0,"Protocol":"tcp"}],"Service":"web","State":"running","Status":"Up 2 days (healthy)"}
		{"ExitCode":0,"Health":"","ID":"1b2c3d4e5f60","Image":"nginx:1.27-alpine","Name":"shop-web-2","Project":"shop","Publishers":[],"Service":"web","State":"running","Status":"Up 2 days"}
		{"ExitCode":1,"Health":"","ID":"2c3d4e5f6071","Image":"ghcr.io/acme/shop-worker:2.4.1","Name":"shop-worker.v2-1","Project":"shop","Publishers":null,"Service":"worker.v2","State":"exited","Status":"Exited (1) 5 minutes ago"}
		""";

	/// <summary>The same as one array, as 2.0 to 2.20 print it.</summary>
	public const string ComposePsArray = """
		[{"ID":"0a1b2c3d4e5f","Name":"shop-db-1","Service":"db","State":"running","Health":"unhealthy","ExitCode":0,"Publishers":[{"URL":"127.0.0.1","TargetPort":5432,"PublishedPort":5432,"Protocol":"tcp"}],"Status":"Up 2 days (unhealthy)"},{"ID":"3d4e5f607182","Name":"shop-api-1","Service":"api","State":"restarting","ExitCode":2,"Status":"Restarting (2) 10 seconds ago"}]
		""";

	/// <summary><c>docker volume inspect</c>: an array of one.</summary>
	public const string VolumeInspect = """
		[
		  {
		    "CreatedAt": "2026-10-09T07:00:00Z",
		    "Driver": "local",
		    "Labels": { "com.docker.compose.project": "shop", "com.docker.compose.volume": "pgdata" },
		    "Mountpoint": "/var/lib/docker/volumes/shop_pgdata/_data",
		    "Name": "shop_pgdata",
		    "Options": null,
		    "Scope": "local"
		  }
		]
		""";
}
