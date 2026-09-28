using Microsoft.Net.Http.Headers;
using Mokaterm.Abstractions.Diagnostics;

namespace Mokaterm.Web.Services;

internal static class DownloadEndpoints
{
	public const string RoutePrefix = "_mokaterm/download";

	public static IEndpointRouteBuilder MapDownloads(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapGet("/" + RoutePrefix + "/{ticket}", HandleAsync).ExcludeFromDescription();
		return endpoints;
	}

	private static async Task HandleAsync(string ticket, HttpContext context, DownloadTicketStore store, ILoggerFactory loggerFactory)
	{
		if (!store.TryTake(ticket, out DownloadTicket download))
		{
			context.Response.StatusCode = StatusCodes.Status404NotFound;
			return;
		}

		ContentDispositionHeaderValue disposition = new("attachment");
		disposition.SetHttpFileName(download.FileName);

		// No Content-Length: the size comes from the listing, and a file that changed since (a growing log) would write more
		// or less than it promised, which fails the whole download. Without one the browser just shows no total size.
		context.Response.ContentType = "application/octet-stream";
		context.Response.Headers.ContentDisposition = disposition.ToString();
		context.Response.Headers.CacheControl = "no-store";

		try
		{
			await download.WriteAsync(context.Response.Body, context.RequestAborted);
			download.Completion.TrySetResult(true);
		}
		catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
		{
			download.Completion.TrySetResult(false);
		}
		catch (Exception ex)
		{
			loggerFactory.CreateLogger(typeof(DownloadEndpoints)).LogWarning("Download failed: {Error}", LogSafe.Describe(ex));
			download.Completion.TrySetException(ex);
			context.Abort();
		}
	}
}
