using System.Globalization;
using System.Text;

namespace Mokaterm.DevHost.Demo.Docker;

/// <summary>The manager's own scripts about the host's files, and commands run inside the demo's containers.</summary>
internal sealed partial class DemoDockerEngine
{
	/// <summary>
	/// The two scripts the manager sends, told apart by how they start: the batched <c>test -f</c>, answered from the
	/// files the demo tree holds, and the search for compose files.
	/// </summary>
	private static DemoDockerResult Script(string script, string[] arguments)
	{
		if (script.StartsWith("for f do", StringComparison.Ordinal))
		{
			return DemoDockerResult.Ok(string.Concat(arguments.Select(path => DemoDockerFiles.Present.Contains(path) ? "1" : "0")));
		}

		if (script.StartsWith("depth=", StringComparison.Ordinal))
		{
			return DemoDockerResult.Ok(string.Concat(DemoDockerFiles.Found.Select(path => path + "\n")));
		}

		return DemoDockerResult.Fail(2, "sh: 1: the demo runs only the manager's own scripts");
	}

	/// <summary>
	/// <c>docker exec [-u U] [-w DIR] [-e K=V]... NAME CMD...</c>: a few commands answer the way a small Linux container
	/// would, the rest are not found. The finder for bash is handled before this.
	/// </summary>
	private DemoDockerResult Exec(string[] words)
	{
		string user = "root";
		string directory = "/";
		List<string> environment = [];
		int index = 0;
		while (index < words.Length && words[index].StartsWith('-'))
		{
			switch (words[index])
			{
				case "-u":
					user = words[index + 1];
					break;
				case "-w":
					directory = words[index + 1];
					break;
				case "-e":
					environment.Add(words[index + 1]);
					break;
			}

			index += 2;
		}

		if (index >= words.Length || Find(words[index]) is not { } container)
		{
			return DemoDockerResult.Fail(1, $"Error response from daemon: No such container: {(index < words.Length ? words[index] : "")}");
		}

		if (container.State != "running")
		{
			return DemoDockerResult.Fail(1, $"Error response from daemon: container {container.Id[..12]} is not running");
		}

		string[] command = words[(index + 1)..];
		string line = command is [("sh" or "bash"), "-c", var script] ? script : string.Join(' ', command);
		string program = line.Split(' ', 2)[0];
		return program switch
		{
			"whoami" => DemoDockerResult.Ok(user + "\n"),
			"pwd" => DemoDockerResult.Ok(directory + "\n"),
			"env" => DemoDockerResult.Ok(string.Concat(container.Environment.Concat(environment).Append("HOSTNAME=" + container.Id[..12]).Select(variable => variable + "\n"))),
			"hostname" => DemoDockerResult.Ok(container.Id[..12] + "\n"),
			"uptime" => DemoDockerResult.Ok(string.Create(CultureInfo.InvariantCulture, $" {_time.GetUtcNow():HH:mm:ss} up {Ago(_time.GetUtcNow() - container.StartedAt)},  load average: 0.08, 0.05, 0.01\n")),
			"cat" when line.Contains("/etc/os-release", StringComparison.Ordinal) => DemoDockerResult.Ok("PRETTY_NAME=\"Debian GNU/Linux 12 (bookworm)\"\nID=debian\nVERSION_ID=\"12\"\n"),
			"ls" => DemoDockerResult.Ok("bin\nboot\ndev\netc\nhome\nlib\nproc\nroot\nrun\nsys\ntmp\nusr\nvar\n"),
			"ps" => DemoDockerResult.Ok($"PID   USER     COMMAND\n    1 {user,-8} {container.Command}\n   42 {user,-8} {line}\n"),
			"echo" => DemoDockerResult.Ok((line.Length > 5 ? line[5..] : "") + "\n"),
			"false" => new DemoDockerResult(1, "", ""),
			_ => DemoDockerResult.Fail(127, $"sh: 1: {program}: not found"),
		};
	}

	private DemoDockerResult InspectVolume(string name)
	{
		if (!_volumes.Contains(name))
		{
			return DemoDockerResult.Fail(1, $"Error response from daemon: get {name}: no such volume");
		}

		return DemoDockerResult.Ok(Line(new[]
		{
			new
			{
				CreatedAt = _time.GetUtcNow().AddDays(-30).ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture),
				Driver = "local",
				Labels = name.StartsWith("shop_", StringComparison.Ordinal) ? new Dictionary<string, string> { ["com.docker.compose.project"] = "shop" } : [],
				Mountpoint = $"/var/lib/docker/volumes/{name}/_data",
				Name = name,
				Options = new Dictionary<string, string>(),
				Scope = "local",
			},
		}) + "\n");
	}

	private DemoDockerResult InspectMany(string[] names)
	{
		StringBuilder all = new("[");
		foreach (string name in names)
		{
			DemoDockerResult one = Inspect(name);
			if (one.ExitCode != 0)
			{
				return one;
			}

			string body = one.Output.Trim();
			all.Append(all.Length > 1 ? "," : "").Append(body[1..^1]);
		}

		return DemoDockerResult.Ok(all.Append("]\n").ToString());
	}
}
