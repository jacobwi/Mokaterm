namespace Mokaterm.Web.Services;

internal static class SecurityHeaders
{
	// Scripts only from this origin, no eval. Styles allow inline because Moka.Red writes theme tokens into style
	// attributes and a <style> block. 'self' in connect-src also covers the same-origin WebSocket for the circuit.
	// blob: in img-src is the RDP pointer: rdp.js turns the server's cursor bitmap into a blob URL for CSS cursor, and
	// only this origin's own scripts can mint one.
	private const string ContentSecurityPolicy =
		"default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; " +
		"connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

	public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
		app.Use(async (context, next) =>
		{
			IHeaderDictionary headers = context.Response.Headers;
			headers["Content-Security-Policy"] = ContentSecurityPolicy;
			headers["X-Content-Type-Options"] = "nosniff";
			headers["X-Frame-Options"] = "DENY";
			headers["Referrer-Policy"] = "no-referrer";
			headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
			headers["Cross-Origin-Opener-Policy"] = "same-origin";
			await next();
		});
}
