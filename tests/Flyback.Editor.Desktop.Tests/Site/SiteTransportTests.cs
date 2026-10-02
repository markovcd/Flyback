using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Flyback.Editor.Site;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Desktop.Tests.Site;

/// <summary>What the editor's client for the preset site sends and unpacks.</summary>
public sealed class SiteTransportTests
{
    [Fact]
    public async Task The_editor_asks_the_site_for_brotli_and_unpacks_what_it_sends()
    {
        var file = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("{\"Nodes\":[]}", 200)));

        using var packed = new MemoryStream();
        using (var brotli = new BrotliStream(packed, CompressionLevel.Fastest, leaveOpen: true))
            brotli.Write(file);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var request = Answer(listener, packed.ToArray(), TestContext.Current.CancellationToken);

            using var provider = EditorServices.Provider();
            using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(SiteAccess.Client);

            var got = await client.GetByteArrayAsync(new Uri($"http://127.0.0.1:{port}/api/v1/presets/x/file"), TestContext.Current.CancellationToken);

            got.ShouldBe(file);
            (await request).ShouldContain("Accept-Encoding", Case.Insensitive);
            (await request).ShouldContain("br", Case.Insensitive);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>Reads one request off <paramref name="listener"/> and answers it with <paramref name="packed"/> as brotli; the request's head.</summary>
    private static async Task<string> Answer(TcpListener listener, byte[] packed, CancellationToken cancel)
    {
        using var connection = await listener.AcceptTcpClientAsync(cancel);
        var stream = connection.GetStream();
        var head = new StringBuilder();
        var one = new byte[1];

        while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal) && await stream.ReadAsync(one, cancel) == 1)
            head.Append((char)one[0]);

        var reply = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Encoding: br\r\nContent-Length: {packed.Length}\r\nConnection: close\r\n\r\n");

        await stream.WriteAsync(reply, cancel);
        await stream.WriteAsync(packed, cancel);
        await stream.FlushAsync(cancel);

        return head.ToString();
    }
}
