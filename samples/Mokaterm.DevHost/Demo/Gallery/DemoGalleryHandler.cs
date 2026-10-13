using System.Net;

namespace Mokaterm.DevHost.Demo.Gallery;

/// <summary>
/// Answers the gallery client's requests for the demo gallery's host from memory, after a short pause so the Browse
/// tab's loading state can be seen. Anything else gets a 404, as a web server would answer a file it does not have.
/// </summary>
internal sealed class DemoGalleryHandler : HttpMessageHandler
{
	private static readonly TimeSpan Latency = TimeSpan.FromMilliseconds(400);

	private readonly string _host;
	private readonly IReadOnlyDictionary<string, byte[]> _files;

	public DemoGalleryHandler(string host, IReadOnlyDictionary<string, byte[]> files)
	{
		_host = host;
		_files = files;
	}

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		await Task.Delay(Latency, cancellationToken);
		HttpResponseMessage response = request.Method == HttpMethod.Get
			&& request.RequestUri is { } address
			&& string.Equals(address.Host, _host, StringComparison.OrdinalIgnoreCase)
			&& _files.TryGetValue(address.AbsolutePath, out byte[]? body)
				? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
				: new HttpResponseMessage(HttpStatusCode.NotFound);
		response.RequestMessage = request;
		return response;
	}
}
