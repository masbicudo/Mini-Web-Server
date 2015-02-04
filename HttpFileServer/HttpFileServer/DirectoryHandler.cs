using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HttpFileServer
{
    public class DirectoryHandler : HttpRequestHandler
    {
        public override async Task RespondAsync(HttpContext context)
        {
            var basePath = context.Server.BasePath;
            var fileFullName = Path.Combine(basePath, context.Uri.LocalPath.TrimStart('/'));
            DateTime? fileDate = null;
            try
            {
                fileDate = File.GetLastWriteTimeUtc(fileFullName);
            }
            catch
            {
            }

            if (!fileDate.HasValue)
                return;

            var fileBytes = GetDirectoryBytes(fileFullName, context.Uri);
            var contentType = MimeUtils.GetMimeType("html");
            contentType += "; charset=utf-8";

            if (fileBytes == null)
                return;

            using (var writer = new StreamWriter(context.Output, Encoding.ASCII))
            {
                // writing header
                await writer.WriteHttpHeaderAsync("HTTP/1.1 200 OK");
                await writer.WriteHttpHeaderAsync("Date", DateTime.UtcNow.ToString("R"));
                await writer.WriteHttpHeaderAsync("Server", "Mini-Http-Server");
                await writer.WriteHttpHeaderAsync("Accept-Ranges", "bytes");
                await writer.WriteHttpHeaderAsync("Content-Length", "" + fileBytes.Length);
                await writer.WriteHttpHeaderAsync("Content-Type", contentType);
                await writer.WriteHttpHeaderAsync("Last-Modified", fileDate.Value.ToString("R"));
                await writer.WriteHttpHeaderAsync("");

                await writer.FlushAsync();

                // writing file contents
                await context.Output.WriteAsync(fileBytes, 0, fileBytes.Length);
            }
        }

        private static byte[] GetDirectoryBytes(string dirFullName, Uri uri)
        {
            var dir = new DirectoryInfo(dirFullName);

            var isRoot = uri.LocalPath == "" || uri.LocalPath == "/";

            var str = @"<!DOCTYPE html>
<html>
    <head>
        <title>Dir</title>
        <style>
            div.item {
                font-family: monospace;
                font-size: 16px;
                margin: 8px;
            }
            div.item a > * {
                vertical-align: middle;
            }
            div.item a > img {
                margin-right: 8px;
            }
            div.item a.dir {
            }
            div.item a.file {
            }
        </style>
    </head>
    <body>
        <h1>%DIR%</h1>
        <p>%LIST%</p>
    </body>
</html>
";

            str = str.Replace("%DIR%", isRoot ? "Root" : dir.Name);

            IEnumerable<string> linksList = new string[0];

            if (!isRoot)
                linksList = linksList.Concat(
                    new[]
                        {
                            "<div class='item parent dir'><a href='" + Uri.EscapeUriString("..") + "'>" + ".."
                            + "</a></div>"
                        });

            var currentPath = uri.LocalPath.TrimEnd('/');

            linksList = linksList.Concat(
                dir.GetDirectories().Select(
                    sd => "<div class='item dir'><a href='" + Uri.EscapeUriString(currentPath + '/' + sd.Name) + "'>"
                          + "<img src='/*Meta" + Uri.EscapeUriString(currentPath + '/' + sd.Name) + "?icon=16"
                          + "' width='16' height='16' />"
                          + sd.Name + "</a></div>"));

            linksList = linksList.Concat(
                dir.GetFiles().Select(
                    sd => "<div class='item file'><a href='" + Uri.EscapeUriString(currentPath + '/' + sd.Name) + "'>"
                          + "<img src='/*Meta" + Uri.EscapeUriString(currentPath + '/' + sd.Name) + "?icon=16"
                          + "' width='16' height='16' />"
                          + sd.Name + "</a></div>"));

            str = str.Replace("%LIST%", string.Join("\n", linksList));

            return Encoding.UTF8.GetBytes(str);
        }
    }
}