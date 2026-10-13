using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.Core.Plugins.Gallery;

/// <summary>
/// The gallery's one HTTP client, shared by every window: reads the signed list and downloads packages, each under a
/// size cap, over https only. A request carries a user agent naming Mokaterm and its version and nothing else: no
/// cookies, no referrer, nothing about the user or the machine.
/// </summary>
internal sealed class PluginGalleryClient : IDisposable
{
	/// <summary>How long any one wait on the gallery's host may last: the answer's headers, or the next piece of a body.</summary>
	internal static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

	private const int CopyBufferBytes = 81920;

	private readonly HttpClient _http;
	private readonly byte[][] _keys;

	/// <param name="options">The list's address and the keys it must be signed with.</param>
	/// <param name="handler">Where requests go, or null for the network. Tests and the DevHost's demo gallery pass their own.</param>
	/// <exception cref="ArgumentException">The address is not https, or a key is not an Ed25519 public key.</exception>
	public PluginGalleryClient(PluginGalleryOptions options, HttpMessageHandler? handler = null)
	{
		ArgumentNullException.ThrowIfNull(options);
		Index = CheckIndex(options.Index);
		Signature = new UriBuilder(Index) { Path = Index.AbsolutePath + ".sig" }.Uri;
		_keys = PluginIndexSignature.ReadKeys(options.TrustedKeys);
		_http = new HttpClient(handler ?? CreateHandler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
		if (!_http.DefaultRequestHeaders.UserAgent.TryParseAdd($"Mokaterm/{DisplayVersion.Of(typeof(PluginGalleryClient).Assembly)}"))
		{
			_http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Mokaterm", null));
		}
	}

	public Uri Index { get; }

	public Uri Signature { get; }

	public void Dispose() => _http.Dispose();

	/// <summary>Refuses an address a host could not have meant: the list is only ever read over https.</summary>
	/// <exception cref="ArgumentException">The address is not an absolute https address.</exception>
	public static Uri CheckIndex(Uri? index) =>
		index is { IsAbsoluteUri: true } && index.Scheme == Uri.UriSchemeHttps && index.Host.Length > 0
			? index
			: throw new ArgumentException("The gallery's list must be an https address.", nameof(index));

	/// <summary>Reads the list and its signature, checks the signature over the exact bytes, then reads the list.</summary>
	/// <exception cref="PluginGalleryException">Either file could not be read, the signature does not match, or the list breaks a rule.</exception>
	public async Task<PluginGalleryIndex> ReadIndexAsync(string hostName, CancellationToken cancellationToken)
	{
		byte[] index = await GetAsync(Index, PluginIndexReader.MaxIndexBytes, Wording.List, cancellationToken);
		byte[] signatureFile = await GetAsync(Signature, PluginIndexSignature.MaxFileBytes, Wording.Signature, cancellationToken);
		byte[] signature = PluginIndexSignature.ReadFile(signatureFile);
		if (!PluginIndexSignature.Verify(index, signature, _keys))
		{
			throw new PluginGalleryException(
				"The plugin list is not signed with a key this version of Mokaterm trusts, so it was not used. It may have been "
				+ "changed on the way, or Mokaterm needs an update.");
		}

		return PluginIndexReader.Read(index, hostName);
	}

	/// <summary>Downloads a package and checks its size and SHA-256 against the list before anything reads it.</summary>
	/// <exception cref="PluginGalleryException">The download failed or does not match the list.</exception>
	public async Task<byte[]> DownloadAsync(PluginGalleryVersion version, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(version);
		if (version.Url.Scheme != Uri.UriSchemeHttps)
		{
			throw new PluginGalleryException("The gallery names a download that is not https, so it was not fetched.");
		}

		long cap = Math.Min(version.Size, PluginPackage.MaxPackageBytes);
		byte[] data = await GetAsync(version.Url, cap, Wording.Package, cancellationToken);
		if (data.Length != version.Size)
		{
			throw new PluginGalleryException(
				$"The download is {data.Length} bytes and the gallery lists {version.Size}, so it was not opened. Try again later.");
		}

		if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(data)), version.Sha256, StringComparison.Ordinal))
		{
			throw new PluginGalleryException(
				"The download does not match the SHA-256 the gallery lists, so it was not opened. It may have been changed on the "
				+ "way; try again later.");
		}

		return data;
	}

	private static SocketsHttpHandler CreateHandler() => new()
	{
		// GitHub release downloads redirect to its storage host. The handler never follows https to http.
		AllowAutoRedirect = true,
		MaxAutomaticRedirections = 5,
		UseCookies = false,
		AutomaticDecompression = DecompressionMethods.All,
		ConnectTimeout = StallTimeout,
		PooledConnectionLifetime = TimeSpan.FromMinutes(5),
	};

	private async Task<byte[]> GetAsync(Uri address, long maxBytes, Wording wording, CancellationToken cancellationToken)
	{
		string host = address.Host;
		using CancellationTokenSource stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		stall.CancelAfter(StallTimeout);
		try
		{
			using HttpRequestMessage request = new(HttpMethod.Get, address);
			using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token);
			if (response.RequestMessage?.RequestUri is { } final && final.Scheme != Uri.UriSchemeHttps)
			{
				throw new PluginGalleryException($"{wording.Subject(host)} was sent somewhere that is not https, so it was not read.");
			}

			if (!response.IsSuccessStatusCode)
			{
				throw new PluginGalleryException(wording.Refused(host, response.StatusCode));
			}

			if (response.Content.Headers.ContentLength > maxBytes)
			{
				throw new PluginGalleryException(wording.TooLarge(maxBytes));
			}

			// Sized from the answer's own length, so a package that is exactly as long as it says is never copied again.
			int capacity = (int)Math.Min(response.Content.Headers.ContentLength ?? CopyBufferBytes, maxBytes);
			using MemoryStream copy = new(capacity);
			await using Stream body = await response.Content.ReadAsStreamAsync(stall.Token);
			byte[] buffer = new byte[CopyBufferBytes];
			while (true)
			{
				stall.CancelAfter(StallTimeout);
				int read = await body.ReadAsync(buffer, stall.Token);
				if (read == 0)
				{
					break;
				}

				if (copy.Length + read > maxBytes)
				{
					throw new PluginGalleryException(wording.TooLarge(maxBytes));
				}

				copy.Write(buffer, 0, read);
			}

			return copy.Length == copy.Capacity ? copy.GetBuffer() : copy.ToArray();
		}
		catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
		{
			throw new PluginGalleryException($"{host} stopped answering. Try again later.", ex);
		}
		catch (HttpRequestException ex)
		{
			throw new PluginGalleryException($"Mokaterm could not reach {host}. Check the connection and try again.", ex);
		}
		catch (IOException ex)
		{
			throw new PluginGalleryException($"The connection to {host} broke off. Try again.", ex);
		}
	}

	/// <summary>What each request says when it fails, so the three reads share one loop.</summary>
	private sealed record Wording(string What, string? Missing)
	{
		public static Wording List { get; } = new("the plugin list", null);

		public static Wording Signature { get; } = new("the plugin list's signature", "The plugin list has no signature, so it was not used.");

		public static Wording Package { get; } = new("the plugin package", null);

		public string Subject(string host) => $"The request for {What} on {host}";

		public string Refused(string host, HttpStatusCode status) =>
			Missing is not null && status is HttpStatusCode.NotFound or HttpStatusCode.Gone
				? Missing
				: $"{host} answered {(int)status}{Described(status)} when asked for {What}.";

		public string TooLarge(long maxBytes) => this == Package
			? "The download is larger than the gallery says it is, so it was stopped."
			: $"{char.ToUpperInvariant(What[0])}{What[1..]} is larger than {Size(maxBytes)}, so it was not read.";

		// The status in words from its name, never the server's own reason text, which says whatever the server likes.
		private static string Described(HttpStatusCode status)
		{
			string name = status.ToString();
			return name.All(char.IsAsciiDigit) ? "" : $" ({string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? $" {c}" : $"{c}"))})";
		}

		private static string Size(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / (1024 * 1024)} MB" : $"{bytes / 1024} KB";
	}
}
