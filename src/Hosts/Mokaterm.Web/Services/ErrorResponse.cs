namespace Mokaterm.Web.Services;

/// <summary>What a request that fails outside the circuit gets back: a status and one sentence, never the exception.</summary>
internal static class ErrorResponse
{
	public static Task WriteAsync(HttpContext context)
	{
		context.Response.StatusCode = StatusCodes.Status500InternalServerError;
		context.Response.ContentType = "text/plain; charset=utf-8";
		context.Response.Headers.CacheControl = "no-store";
		return context.Response.WriteAsync("Mokaterm could not handle this request. The details are in the server log.", context.RequestAborted);
	}
}
