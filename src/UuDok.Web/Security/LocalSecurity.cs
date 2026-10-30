using System.Security.Cryptography;
using System.Text;

namespace UuDok.Web.Security;

/// <summary>
/// Vern for en lokal webtjeneste: streng Host- og Origin-kontroll (mot DNS-rebinding og andre nettsteder),
/// CSRF-token (dobbel innsending, HttpOnly-informasjonskapsel + header) og sikkerhetsheadere.
/// </summary>
public sealed class LocalSecurityMiddleware(RequestDelegate next, LocalSecurityOptions opt)
{
    public const string CsrfCookie = "uudok-csrf";
    public const string CsrfHeader = "X-UuDok-Csrf";

    public async Task InvokeAsync(HttpContext ctx)
    {
        var host = ctx.Request.Host;
        if (!opt.AllowedHosts.Contains(host.Value ?? "", StringComparer.OrdinalIgnoreCase))
        {
            ctx.Response.StatusCode = StatusCodes.Status421MisdirectedRequest;
            await ctx.Response.WriteAsync("Ugyldig vertsnavn.");
            return;
        }
        var origin = ctx.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && !opt.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsync("Forespørsel fra annet nettsted er avvist.");
            return;
        }
        var fetchSite = ctx.Request.Headers["Sec-Fetch-Site"].ToString();
        if (fetchSite is "cross-site") { ctx.Response.StatusCode = 403; return; }

        if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method))
        {
            var cookie = ctx.Request.Cookies[CsrfCookie];
            var header = ctx.Request.Headers[CsrfHeader].ToString();
            if (string.IsNullOrEmpty(cookie) || string.IsNullOrEmpty(header) ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(cookie), Encoding.UTF8.GetBytes(header)))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                await ctx.Response.WriteAsync("{\"error\":\"Økten er utløpt. Last inn siden på nytt.\"}");
                return;
            }
        }

        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["Referrer-Policy"] = "no-referrer";
        h["X-Frame-Options"] = "DENY";
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        h["Cross-Origin-Resource-Policy"] = "same-origin";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        if (!ctx.Request.Path.StartsWithSegments("/api/jobs") || !ctx.Request.Path.Value!.EndsWith("/report.html"))
            h["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data: blob:; style-src 'self'; script-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
        h["Cache-Control"] = ctx.Request.Path.StartsWithSegments("/api") ? "no-store" : h["Cache-Control"].ToString();
        await next(ctx);
    }

    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record LocalSecurityOptions(string[] AllowedHosts, string[] AllowedOrigins)
{
    public static LocalSecurityOptions ForPort(int port) => new(
        [$"127.0.0.1:{port}", $"localhost:{port}"],
        [$"http://127.0.0.1:{port}", $"http://localhost:{port}"]);
}
