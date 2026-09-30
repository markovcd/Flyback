using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;

namespace Flyback.Server;

/// <summary>
/// Serves a file as the brotli or gzip copy its build wrote beside it, to a browser that takes one.
/// </summary>
/// <remarks>
/// The request is pointed at the copy for the static files that follow, so they still answer ranges,
/// ETags and 304s; <see cref="Requested"/> gives back the path that was asked for.
/// </remarks>
internal static class Precompressed
{
    private const string Asked = "Precompressed.Asked";

    private static readonly (string Coding, string Suffix)[] Copies = [("br", ".br"), ("gzip", ".gz")];

    /// <summary>
    /// Serves compressed the files under <paramref name="route"/> that <paramref name="files"/>,
    /// rooted at <paramref name="filesAt"/>, holds a copy of.
    /// </summary>
    public static IApplicationBuilder UsePrecompressed(
        this IApplicationBuilder app, PathString route, IFileProvider files, PathString filesAt, IContentTypeProvider types) =>
        app.Use((http, next) =>
        {
            var path = http.Request.Path;

            if ((HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method))
                && path.StartsWithSegments(route)
                && path.StartsWithSegments(filesAt, out var within)
                && types.TryGetContentType(path.Value!, out var type)
                && Copies.Any(c => files.GetFileInfo(within.Value + c.Suffix).Exists))
            {
                // The plain answer varies on Accept-Encoding as much as a compressed one does.
                http.Response.OnStarting(() =>
                {
                    http.Response.Headers.Append(HeaderNames.Vary, HeaderNames.AcceptEncoding);
                    return Task.CompletedTask;
                });

                if (Pick(http, files, within) is var (coding, suffix))
                {
                    http.Items[Asked] = path;
                    http.Request.Path = new PathString(path.Value + suffix);
                    http.Response.OnStarting(() =>
                    {
                        var headers = http.Response.Headers;
                        headers.ContentEncoding = coding;
                        headers.ContentType = type;
                        return Task.CompletedTask;
                    });
                }
            }

            return next(http);
        });

    /// <summary>The path the browser asked for, before it was pointed at a compressed copy.</summary>
    public static PathString Requested(HttpContext http) =>
        http.Items.TryGetValue(Asked, out var asked) && asked is PathString path ? path : http.Request.Path;

    private static (string Coding, string Suffix)? Pick(HttpContext http, IFileProvider files, PathString within)
    {
        var taken = http.Request.GetTypedHeaders().AcceptEncoding;

        foreach (var (coding, suffix) in Copies)
        {
            var takes = taken.Any(t => t.Value.Equals(coding, StringComparison.OrdinalIgnoreCase) && (t.Quality ?? 1) > 0);

            if (takes && files.GetFileInfo(within.Value + suffix).Exists) return (coding, suffix);
        }

        return null;
    }
}
