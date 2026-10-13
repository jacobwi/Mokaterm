using System.Net;

namespace Mokaterm.Core.Tests.Plugins.Gallery;

/// <summary>
/// Stands in for GitHub Pages and Releases: answers from a table of files by address and keeps every request, so a test
/// can say exactly what was asked and what was sent with it. Nothing leaves the process.
/// </summary>
internal sealed class FakeGalleryServer : HttpMessageHandler
{
	private readonly Dictionary<string, Answer> _answers = new(StringComparer.Ordinal);
	private readonly List<HttpRequestMessage> _requests = [];
	private readonly Lock _gate = new();

	public IReadOnlyList<HttpRequestMessage> Requests
	{
		get
		{
			lock (_gate)
			{
				return [.. _requests];
			}
		}
	}

	/// <summary>Serves <paramref name="body"/> at <paramref name="address"/>.</summary>
	/// <param name="hideLength">Sends the body without a Content-Length, the way a chunked answer arrives.</param>
	public void Serve(Uri address, byte[] body, bool hideLength = false) =>
		_answers[address.AbsoluteUri] = new Answer(HttpStatusCode.OK, body, hideLength);

	public void Refuse(Uri address, HttpStatusCode status) => _answers[address.AbsoluteUri] = new Answer(status, [], HideLength: false);

	public void Forget(Uri address) => _answers.Remove(address.AbsoluteUri);

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		lock (_gate)
		{
			_requests.Add(request);
		}

		HttpResponseMessage response = _answers.TryGetValue(request.RequestUri!.AbsoluteUri, out Answer? answer)
			? new HttpResponseMessage(answer.Status) { Content = answer.Content() }
			: new HttpResponseMessage(HttpStatusCode.NotFound);
		response.RequestMessage = request;
		return Task.FromResult(response);
	}

	private sealed record Answer(HttpStatusCode Status, byte[] Body, bool HideLength)
	{
		// A stream that cannot seek gives StreamContent no length to send.
		public HttpContent Content() => HideLength ? new StreamContent(new OneWayStream(Body)) : new ByteArrayContent(Body);
	}

	private sealed class OneWayStream(byte[] body) : MemoryStream(body, writable: false)
	{
		public override bool CanSeek => false;
	}
}
