using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace _YetAnotherHttpHandler.Test;

class TestServerForHttp2 : ITestServerBuilder
{
    public static WebApplication BuildApplication(WebApplicationBuilder builder)
    {
        var app = builder.Build();

        app.MapGet("/", () => Results.Content("__OK__"));
        app.MapGet("/not-found", () => Results.Content("__Not_Found__", statusCode: 404));
        app.MapGet("/response-headers", (HttpContext httpContext) =>
        {
            httpContext.Response.Headers["x-test"] = "foo";
            return Results.Content("__OK__");
        });
        app.MapPost("/post-echo", async (HttpContext httpContext, Stream bodyStream) =>
        {
            httpContext.Response.Headers["x-request-content-type"] = httpContext.Request.ContentType;

            return Results.Bytes(await bodyStream.ToArrayAsync(), "application/octet-stream");
        });
        app.MapPost("/post-streaming", async (HttpContext httpContext, PipeReader reader) =>
        {
            // Send status code and response headers.
            httpContext.Response.Headers["x-header-1"] = "foo";
            httpContext.Response.StatusCode = (int)HttpStatusCode.OK;
            await httpContext.Response.BodyWriter.FlushAsync();

            var readLen = 0L;
            while (true)
            {
                var readResult = await reader.ReadAsync();
                readLen += readResult.Buffer.Length;
                reader.AdvanceTo(readResult.Buffer.End);
                if (readResult.IsCompleted || readResult.IsCanceled) break;
            }

            await httpContext.Response.WriteAsync(readLen.ToString());

            return Results.Empty;
        });
        app.MapPost("/post-response-trailers", (HttpContext httpContext) =>
        {
            httpContext.Response.AppendTrailer("x-trailer-1", "foo");
            httpContext.Response.AppendTrailer("x-trailer-2", "bar");

            return Results.Ok("__OK__");
        });
        app.MapPost("/post-response-headers-immediately", async (HttpContext httpContext, PipeReader reader) =>
        {
            // Send status code and response headers.
            httpContext.Response.Headers["x-header-1"] = "foo";
            httpContext.Response.StatusCode = (int)HttpStatusCode.Accepted;
            await httpContext.Response.BodyWriter.FlushAsync();

            await Task.Delay(100000);
            await httpContext.Response.WriteAsync("__OK__");
            return Results.Empty;
        });

        // HTTP/2
        app.MapGet("/error-reset", (HttpContext httpContext) =>
        {
            // https://learn.microsoft.com/ja-jp/aspnet/core/fundamentals/servers/kestrel/http2?view=aspnetcore-7.0#reset-1
            var resetFeature = httpContext.Features.Get<IHttpResetFeature>();
            resetFeature!.Reset(errorCode: 2); // INTERNAL_ERROR
            return Results.Empty;
        });

        return app;
    }
}