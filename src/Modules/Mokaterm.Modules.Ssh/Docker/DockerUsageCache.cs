namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// The session's last <c>docker system df --verbose</c>, the one read that sizes every volume. Sizing can keep the daemon
/// busy for a while on a big host, so the volumes page measures when it opens on a read older than <see cref="MaxAge"/>
/// and when asked, never with every refresh of its list; the cleanup page's own reads land here too.
/// </summary>
internal sealed class DockerUsageCache(TimeProvider time)
{
	public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

	private readonly Lock _lock = new();
	private DockerSystemUsage? _usage;
	private DateTimeOffset? _readAt;

	/// <summary>When the last read ended, or null before one.</summary>
	public DateTimeOffset? ReadAt
	{
		get
		{
			lock (_lock)
			{
				return _readAt;
			}
		}
	}

	/// <summary>The last read, or null before one and when that docker printed no such detail.</summary>
	public DockerSystemUsage? Usage
	{
		get
		{
			lock (_lock)
			{
				return _usage;
			}
		}
	}

	/// <summary>True before the first read and once the last one is <see cref="MaxAge"/> old.</summary>
	public bool IsStale
	{
		get
		{
			lock (_lock)
			{
				return _readAt is not { } readAt || time.GetUtcNow() - readAt >= MaxAge;
			}
		}
	}

	public void Record(DockerSystemUsage? usage)
	{
		lock (_lock)
		{
			_usage = usage;
			_readAt = time.GetUtcNow();
		}
	}

	/// <summary>Reads the sizes now and keeps them.</summary>
	public async Task<DockerSystemUsage?> MeasureAsync(DockerClient client, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(client);
		DockerSystemUsage? usage = await client.SystemUsageAsync(cancellationToken);
		Record(usage);
		return usage;
	}
}
