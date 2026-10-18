using System.Globalization;

namespace Mokaterm.DevHost.Demo.Docker;

/// <summary>
/// The demo's builds and pushes, which print over a few seconds the way BuildKit and a push do, so the Docker manager's
/// live output, its Stop and its health watch notes can be tried. A build reads the Dockerfile from the demo tree (one
/// sits in <c>/srv/shop/api</c>) and fails as BuildKit does when there is none; a push is refused by a registry this
/// demo docker is not logged in to.
/// </summary>
internal sealed partial class DemoDockerEngine
{
	private static readonly TimeSpan Quick = TimeSpan.FromMilliseconds(120);

	/// <summary>The command as lines over time, for a build, a push or a compose build; null for every other command.</summary>
	public DemoDockerStream? Stream(string[] words, string? directory)
	{
		ArgumentNullException.ThrowIfNull(words);
		lock (_lock)
		{
			return words switch
			{
				["docker", "buildx", "build", .. var rest] => Build(rest, buildKit: true),
				["docker", "build", .. var rest] => Build(rest, buildKit: false),
				["docker", "push", .. var rest] => Push(rest),
				["docker", "compose", .. var rest] when ComposeWord(rest) == "build" => ComposeBuild(rest, directory),
				_ => null,
			};
		}
	}

	/// <summary>The compose command after its <c>-p</c>, <c>-f</c> and other options, each followed by a value.</summary>
	private static string? ComposeWord(string[] words)
	{
		int index = 0;
		while (index < words.Length && words[index].StartsWith('-'))
		{
			index += 2;
		}

		return index < words.Length ? words[index] : null;
	}

	private static string Value(string[] words, string option) =>
		Array.IndexOf(words, option) is var at and >= 0 && at + 1 < words.Length ? words[at + 1] : "";

	private static IEnumerable<string> Values(string[] words, string option)
	{
		for (int index = 0; index + 1 < words.Length; index++)
		{
			if (words[index] == option)
			{
				yield return words[index + 1];
			}
		}
	}

	private DemoDockerStream Build(string[] words, bool buildKit)
	{
		string context = words.Length > 0 ? words[^1] : ".";
		string file = Value(words, "--file") is { Length: > 0 } named ? named : context.TrimEnd('/') + "/Dockerfile";
		List<string> tags = [.. Values(words, "--tag")];
		bool cached = !words.Contains("--no-cache");
		List<DemoDockerLine> lines = [];
		void Line(string text, double seconds = 0.12) => lines.Add(new DemoDockerLine(TimeSpan.FromSeconds(seconds), text, buildKit));

		if (!buildKit)
		{
			lines.Add(new DemoDockerLine(Quick, "DEPRECATED: The legacy builder is deprecated and will be removed in a future release.", true));
		}

		if (_readFile(file) is null)
		{
			if (buildKit)
			{
				Line("#0 building with \"default\" instance using docker driver");
				Line("");
				Line("#1 [internal] load build definition from Dockerfile");
				Line("#1 transferring dockerfile: 2B done");
				Line("#1 DONE 0.0s");
				lines.Add(new DemoDockerLine(Quick, "ERROR: failed to solve: failed to read dockerfile: open Dockerfile: no such file or directory", true));
			}
			else
			{
				lines.Add(new DemoDockerLine(Quick, "unable to prepare context: unable to evaluate symlinks in Dockerfile path: lstat " + file + ": no such file or directory", true));
			}

			return new DemoDockerStream(lines, () => 1);
		}

		string id = "sha256:" + HexId("built " + string.Join(',', tags) + _time.GetUtcNow().Ticks.ToString(CultureInfo.InvariantCulture));
		if (buildKit)
		{
			Line("#0 building with \"default\" instance using docker driver");
			Line("");
			Line("#1 [internal] load build definition from Dockerfile");
			Line("#1 transferring dockerfile: 312B done");
			Line("#1 DONE 0.0s");
			Line("");
			Line("#2 [internal] load metadata for docker.io/library/node:22-alpine", 0.3);
			Line("#2 DONE 1.1s", 0.9);
			Line("");
			Line("#3 [internal] load .dockerignore");
			Line("#3 transferring context: 2B done");
			Line("#3 DONE 0.0s");
			Line("");
			Line("#4 [build 1/4] FROM docker.io/library/node:22-alpine@" + Digest("node-22-alpine"));
			Line(cached ? "#4 CACHED" : "#4 resolve docker.io/library/node:22-alpine 0.4s done");
			Line("");
			Line("#5 [internal] load build context");
			Line("#5 transferring context: 48.21kB 0.1s done");
			Line("#5 DONE 0.1s");
			Line("");
			Line("#6 [build 2/4] WORKDIR /app");
			Line(cached ? "#6 CACHED" : "#6 DONE 0.1s");
			Line("");
			Line("#7 [build 3/4] COPY package*.json ./");
			Line("#7 DONE 0.1s");
			Line("");
			Line("#8 [build 4/4] RUN npm ci --omit=dev", 0.3);
			Line("#8 0.912 added 142 packages, and audited 143 packages in 3s", 1.0);
			Line("#8 0.913 found 0 vulnerabilities", 0.4);
			Line("#8 DONE 3.4s", 0.8);
			Line("");
			Line("#9 [stage-1 3/3] COPY --from=build /app .");
			Line("#9 DONE 0.2s", 0.3);
			Line("");
			Line("#10 exporting to image");
			Line("#10 exporting layers 0.3s done", 0.4);
			Line("#10 writing image " + id + " done");
			foreach (string tag in tags)
			{
				Line("#10 naming to " + FullName(Tagged(tag)) + " done");
			}

			Line("#10 DONE 0.4s");
		}
		else
		{
			Line("Sending build context to Docker daemon  48.21kB", 0.3);
			Line("Step 1/5 : FROM node:22-alpine");
			Line(" ---> 1a2b3c4d5e6f", 0.6);
			Line("Step 2/5 : WORKDIR /app");
			Line(" ---> Using cache");
			Line("Step 3/5 : COPY package*.json ./");
			Line(" ---> 2b3c4d5e6f70");
			Line("Step 4/5 : RUN npm ci --omit=dev", 0.3);
			Line("added 142 packages, and audited 143 packages in 3s", 1.6);
			Line("Step 5/5 : COPY . .");
			Line("Successfully built " + id[7..19], 0.4);
			foreach (string tag in tags)
			{
				Line("Successfully tagged " + Tagged(tag));
			}
		}

		return new DemoDockerStream(lines, () =>
		{
			lock (_lock)
			{
				DateTimeOffset now = _time.GetUtcNow();
				if (tags.Count == 0)
				{
					_images.Add(("<none>", "<none>", id, 168_000_000, now));
				}

				foreach (string tag in tags)
				{
					string tagged = Tagged(tag);
					string repository = RepositoryOf(tagged);
					string name = tagged[(tagged.LastIndexOf(':') + 1)..];
					_images.RemoveAll(image => image.Repository == repository && image.Tag == name);
					_images.Add((repository, name, id, 168_000_000, now));
					_pulled.Remove(tagged);
				}

				return 0;
			}
		});
	}

	private DemoDockerStream Push(string[] words)
	{
		bool allTags = words.Contains("--all-tags");
		string reference = words.Length > 0 ? words[^1] : "";
		List<string> tagged = allTags
			? [.. _images.Where(image => image.Repository == reference).Select(image => $"{image.Repository}:{image.Tag}")]
			: [Tagged(reference)];
		List<DemoDockerLine> lines = [];
		if (tagged.Count == 0 || !tagged.All(tag => _images.Any(image => $"{image.Repository}:{image.Tag}" == tag)))
		{
			lines.Add(new DemoDockerLine(Quick, $"Error response from daemon: An image does not exist locally with the tag: {reference}", true));
			return new DemoDockerStream(lines, () => 1);
		}

		string host = RegistryOf(tagged[0]);
		lines.Add(new DemoDockerLine(Quick, $"The push refers to repository [{FullName(tagged[0])[..FullName(tagged[0]).LastIndexOf(':')]}]", false));
		if (!_logins.Keys.Any(key => HostOf(key) == host))
		{
			lines.Add(new DemoDockerLine(Quick, "5f70bf18a086: Preparing", false));
			lines.Add(new DemoDockerLine(TimeSpan.FromSeconds(0.6), host == "docker.io"
				? "denied: requested access to the resource is denied"
				: $"unauthorized: authentication required (log in to {host} first; the demo takes the password {RegistryPassword})", true));
			return new DemoDockerStream(lines, () => 1);
		}

		foreach (string tag in tagged)
		{
			lines.AddRange(LayerLines(tag, known: _registry.ContainsKey(tag)));
		}

		return new DemoDockerStream(lines, () =>
		{
			lock (_lock)
			{
				foreach (string tag in tagged)
				{
					_registry[tag] = Digest("pushed " + tag);
					_pulled[tag] = _registry[tag];
					_registryErrors.Remove(tag);
				}

				return 0;
			}
		});
	}

	/// <summary><c>compose ... build [SERVICE]</c>: the shop's api has a build context in the demo tree, the rest are pulled.</summary>
	private DemoDockerStream ComposeBuild(string[] words, string? directory)
	{
		string[] after = words[Array.IndexOf(words, "build")..];
		string? service = after.Length > 1 ? after[1] : null;
		List<DemoDockerLine> lines = [];
		void Line(string text, double seconds = 0.15) => lines.Add(new DemoDockerLine(TimeSpan.FromSeconds(seconds), text, true));

		if (directory != ShopFolder || service is not (null or "api"))
		{
			Line(service is null ? "No services to build" : $"{service} uses an image, skipping");
			return new DemoDockerStream(lines, () => 0);
		}

		Line("#0 building with \"default\" instance using docker driver");
		Line("");
		Line("#1 [api internal] load build definition from Dockerfile");
		Line("#1 transferring dockerfile: 312B done");
		Line("#1 DONE 0.0s");
		Line("");
		Line("#2 [api internal] load metadata for docker.io/library/node:22-alpine", 0.3);
		Line("#2 DONE 0.9s", 0.8);
		Line("");
		Line("#3 [api build 4/4] RUN npm ci --omit=dev", 0.3);
		Line("#3 1.204 added 142 packages in 2s", 1.2);
		Line("#3 DONE 2.6s", 0.6);
		Line("");
		Line("#4 [api] exporting to image");
		Line("#4 naming to ghcr.io/acme/shop-api:2.4.1 done", 0.3);
		Line("#4 DONE 0.3s");
		Line(" api  Built");
		return new DemoDockerStream(lines, () =>
		{
			lock (_lock)
			{
				_pulled.Remove("ghcr.io/acme/shop-api:2.4.1");
				return 0;
			}
		});
	}
}
