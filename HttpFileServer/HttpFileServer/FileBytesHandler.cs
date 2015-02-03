using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace HttpFileServer
{
    public class FileBytesHandler : HttpResquestHandler
    {
        private static T TryCatch<T>(Func<T> fn, Func<Exception, T> err)
        {
            try
            {
                return fn();
            }
            catch (Exception ex)
            {
                return err(ex);
            }
        }

        public override async Task RespondAsync(HttpContext context)
        {
            var basePath = context.Server.BasePath;
            var fileFullName = Path.Combine(basePath, context.Uri.LocalPath.TrimStart('/'));

            if (!File.Exists(fileFullName))
                return;

            var fileDate = TryCatch(() => File.GetLastWriteTimeUtc(fileFullName), e => (DateTime?)null);

            if (!fileDate.HasValue)
                return;

            var fileBytes = TryCatch(() => File.ReadAllBytes(fileFullName), e => null);

            if (fileBytes == null)
                return;

            string contentType = MimeUtils.GetMimeType(Path.GetExtension(fileFullName));

            using (var writer = new StreamWriter(context.Output, Encoding.ASCII))
            {
                // writing header
                await writer.WriteHttpHeaderAsync("HTTP/1.1 200 OK");
                await writer.WriteHttpHeaderAsync("Date", DateTime.UtcNow.ToString("R"));
                await writer.WriteHttpHeaderAsync("Server", "Mini-Http-Server");
                await writer.WriteHttpHeaderAsync("Accept-Ranges", "bytes");
                await writer.WriteHttpHeaderAsync("Content-Length", "" + fileBytes.Length);
                await writer.WriteHttpHeaderAsync("Content-Type", contentType);
                await writer.WriteHttpHeaderAsync("Last-Modified", (fileDate ?? DateTime.UtcNow).ToString("R"));
                await writer.WriteHttpHeaderAsync("");

                await writer.FlushAsync();

                // writing file contents
                await context.Output.WriteAsync(fileBytes, 0, fileBytes.Length);
            }

            context.Handled();
        }
    }
}
