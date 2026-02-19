namespace MERSEL.Services.GibUserList.Web.Infrastructure;

/// <summary>
/// Güvenlik başlıkları ekleyen middleware.
/// X-Content-Type-Options, X-Frame-Options, Content-Security-Policy gibi
/// temel güvenlik başlıklarını tüm yanıtlara ekler.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string DefaultCsp =
        "default-src 'self'; script-src 'self'; style-src 'self'; object-src 'none'; frame-ancestors 'none'";

    private const string ScalarCsp =
        "default-src 'self'; script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data: blob:; " +
        "connect-src 'self'; object-src 'none'; frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        var path = context.Request.Path.Value ?? string.Empty;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["X-XSS-Protection"] = "0";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

        var isScalarPath = path.StartsWith("/scalar", StringComparison.OrdinalIgnoreCase)
                        || path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase);
        headers["Content-Security-Policy"] = isScalarPath ? ScalarCsp : DefaultCsp;

        return next(context);
    }
}

/// <summary>
/// SecurityHeadersMiddleware için uzantı metodu.
/// </summary>
public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        => app.UseMiddleware<SecurityHeadersMiddleware>();
}
