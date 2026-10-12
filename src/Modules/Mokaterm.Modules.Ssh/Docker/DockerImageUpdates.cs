using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// The update checks of one session: run when asked, never in the background, and kept for
/// <see cref="FreshFor"/> so every page can mark the images, containers and services it shows without asking again.
/// </summary>
/// <remarks>
/// One <c>docker image inspect</c> reads every local digest at once; the registry is then asked per tag, two at a
/// time, so a long list leaves the pages' own listings their exec channels.
/// </remarks>
internal sealed class DockerImageUpdates
{
	/// <summary>How long a check stands before a page stops showing it.</summary>
	public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(30);

	private const int AtOnce = 2;

	private readonly TimeProvider _time;
	private readonly Lock _lock = new();
	private readonly Dictionary<string, ImageUpdateResult> _results = new(StringComparer.Ordinal);
	private DockerRegistryTool? _tool;
	private int _checking;

	public DockerImageUpdates(TimeProvider time)
	{
		ArgumentNullException.ThrowIfNull(time);
		_time = time;
	}

	/// <summary>Raised after every result. Handlers may run on any thread.</summary>
	public event Action? Changed;

	public bool IsChecking => Volatile.Read(ref _checking) > 0;

	/// <summary>The newest check of any image still fresh, or null.</summary>
	public DateTimeOffset? LastChecked
	{
		get
		{
			lock (_lock)
			{
				return Fresh().Select(result => (DateTimeOffset?)result.CheckedAt).DefaultIfEmpty(null).Max();
			}
		}
	}

	/// <summary>The fresh result for <paramref name="reference"/>, however it was written (<c>nginx</c> or <c>docker.io/library/nginx:latest</c>).</summary>
	public ImageUpdateResult? Find(string? reference)
	{
		if (string.IsNullOrWhiteSpace(reference))
		{
			return null;
		}

		lock (_lock)
		{
			return _results.TryGetValue(DockerImageName.Key(reference), out ImageUpdateResult? result) && IsFresh(result) ? result : null;
		}
	}

	/// <summary>Drops what is known about <paramref name="references"/>, after a pull made it stale.</summary>
	public void Forget(IEnumerable<string> references)
	{
		ArgumentNullException.ThrowIfNull(references);
		lock (_lock)
		{
			foreach (string reference in references)
			{
				_results.Remove(DockerImageName.Key(reference));
			}
		}

		Changed?.Invoke();
	}

	/// <summary>
	/// Checks each reference: local digests in one inspect, then the registry per tag. A reference already checked
	/// within <see cref="FreshFor"/> is asked again only when <paramref name="force"/> is set.
	/// </summary>
	/// <param name="progress">Told how many of how many are done, on the caller's context.</param>
	/// <exception cref="DockerCommandException">The local images could not be read at all.</exception>
	public async Task<IReadOnlyList<ImageUpdateResult>> CheckAsync(
		DockerClient client,
		IEnumerable<string> references,
		bool force,
		Action<int, int>? progress,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(client);
		ArgumentNullException.ThrowIfNull(references);
		List<string> wanted = [.. references
			.Select(reference => reference.Trim())
			.Where(reference => reference.Length > 0)
			.DistinctBy(DockerImageName.Key, StringComparer.Ordinal)
			.Where(reference => force || Find(reference) is null)];
		if (wanted.Count == 0)
		{
			return [];
		}

		Interlocked.Increment(ref _checking);
		try
		{
			return await CheckAllAsync(client, wanted, progress, cancellationToken);
		}
		finally
		{
			Interlocked.Decrement(ref _checking);
			Changed?.Invoke();
		}
	}

	private async Task<IReadOnlyList<ImageUpdateResult>> CheckAllAsync(
		DockerClient client,
		List<string> wanted,
		Action<int, int>? progress,
		CancellationToken cancellationToken)
	{
		List<(string Reference, DockerImageName? Name)> named = [.. wanted.Select(reference => (reference, DockerImageName.Parse(reference)))];
		List<string> tagged = [.. named.Where(item => item.Name is { Tag: not null }).Select(item => item.Reference)];
		IReadOnlyList<DockerLocalImage> images = tagged.Count == 0 ? [] : await InspectAsync(client, tagged, cancellationToken);

		List<ImageUpdateResult> results = [];
		List<(string Reference, DockerImageName Name, DockerLocalImage Image)> remote = [];
		DateTimeOffset now = _time.GetUtcNow();
		foreach ((string reference, DockerImageName? name) in named)
		{
			DockerLocalImage? image = name is null ? null : DockerLocalImage.Find(images, name);
			if (ImageUpdateCheck.WithoutRegistry(reference, name, image, now) is { } local)
			{
				results.Add(Keep(local));
			}
			else
			{
				remote.Add((reference, name!, image!));
			}
		}

		progress?.Invoke(results.Count, wanted.Count);
		foreach ((string Reference, DockerImageName Name, DockerLocalImage Image)[] batch in remote.Chunk(AtOnce))
		{
			ImageUpdateResult[] answers = await Task.WhenAll(batch.Select(item => AskAsync(client, item.Reference, item.Name, item.Image, cancellationToken)));
			results.AddRange(answers.Select(Keep));
			progress?.Invoke(results.Count, wanted.Count);
		}

		return results;
	}

	/// <summary>Every image the references are tags of. An inspect that misses one still prints the others, and exits 1.</summary>
	private static async Task<IReadOnlyList<DockerLocalImage>> InspectAsync(DockerClient client, IReadOnlyList<string> references, CancellationToken cancellationToken)
	{
		DockerOutput output = await client.RunAsync(DockerCommands.InspectImages(references), cancellationToken);
		IReadOnlyList<DockerLocalImage> images = DockerLocalImage.Parse(output.Output);
		return images.Count > 0 || output.Succeeded || DockerErrors.Classify(output.ExitCode, output.Error) == DockerFailure.NotFound
			? images
			: throw client.Failed(output);
	}

	private async Task<ImageUpdateResult> AskAsync(DockerClient client, string reference, DockerImageName name, DockerLocalImage image, CancellationToken cancellationToken)
	{
		DockerRegistryTool tool = await ToolAsync(client, cancellationToken);
		while (tool != DockerRegistryTool.None)
		{
			DockerOutput output;
			try
			{
				output = await client.RunAsync(
					tool == DockerRegistryTool.Buildx ? DockerCommands.ImagetoolsInspect(reference) : DockerCommands.ManifestInspect(reference),
					cancellationToken);
			}
			catch (DockerCommandException ex)
			{
				return new ImageUpdateResult(reference, ImageUpdateState.Failed, ex.Message, _time.GetUtcNow());
			}

			DateTimeOffset now = _time.GetUtcNow();
			if (output.Succeeded)
			{
				DockerRegistryManifest? manifest = tool == DockerRegistryTool.Buildx
					? DockerRegistryManifest.ParseImagetools(output.Output)
					: DockerRegistryManifest.ParseManifestVerbose(output.Output);
				return manifest is null
					? new ImageUpdateResult(reference, ImageUpdateState.Failed, "The registry tool printed nothing this app can read.", now)
					: ImageUpdateCheck.Compare(reference, name, image, manifest, now);
			}

			if (!ImageUpdateCheck.IsUnsupported(output))
			{
				return ImageUpdateCheck.Failed(reference, name, output, client.Host, now);
			}

			// buildx can be there without imagetools, and docker manifest can still need the experimental switch.
			tool = tool == DockerRegistryTool.Buildx ? DockerRegistryTool.Manifest : DockerRegistryTool.None;
			lock (_lock)
			{
				_tool = tool;
			}
		}

		return new ImageUpdateResult(reference, ImageUpdateState.Unsupported, ImageUpdateCheck.Unsupported(client.Host), _time.GetUtcNow());
	}

	/// <summary>buildx when <c>docker buildx version</c> answers, docker manifest otherwise; found once per session.</summary>
	private async Task<DockerRegistryTool> ToolAsync(DockerClient client, CancellationToken cancellationToken)
	{
		lock (_lock)
		{
			if (_tool is { } known)
			{
				return known;
			}
		}

		DockerRegistryTool found;
		try
		{
			found = (await client.RunAsync(DockerCommands.BuildxVersion(), cancellationToken)).Succeeded ? DockerRegistryTool.Buildx : DockerRegistryTool.Manifest;
		}
		catch (DockerCommandException)
		{
			found = DockerRegistryTool.Manifest;
		}

		lock (_lock)
		{
			_tool ??= found;
			return _tool.Value;
		}
	}

	private ImageUpdateResult Keep(ImageUpdateResult result)
	{
		lock (_lock)
		{
			_results[DockerImageName.Key(result.Reference)] = result;
		}

		Changed?.Invoke();
		return result;
	}

	private IEnumerable<ImageUpdateResult> Fresh() => _results.Values.Where(IsFresh);

	private bool IsFresh(ImageUpdateResult result) => _time.GetUtcNow() - result.CheckedAt < FreshFor;
}
