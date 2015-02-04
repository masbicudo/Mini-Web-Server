using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HttpFileServer
{
    public class FileIconHandler : HttpRequestHandler
    {
        private static object dateFirstIconKey = new object();

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
            var start = "/*Meta/";
            var basePath = context.Server.BasePath;
            var localPath = context.Uri.LocalPath;

            if (!localPath.StartsWith(start, StringComparison.InvariantCultureIgnoreCase))
                return;

            localPath = localPath.Substring(start.Length);

            var fileFullName = Path.Combine(basePath, localPath);
            var isFile = File.Exists(fileFullName);
            var isDir = Directory.Exists(fileFullName);

            if (!isFile && !isDir)
                return;

            var fileDate = TryCatch(() => File.GetLastWriteTimeUtc(fileFullName), e => (DateTime?)null);

            if (!fileDate.HasValue)
                return;

            byte[] iconBytes = null;
            string contentType = null;
            if (isFile)
            {
                var queryItems = context.Uri.Query
                    .TrimStart('?')
                    .Split('&')
                    .Select(x => x.Split("=".ToCharArray(), 2)).ToArray();

                var iconSize = queryItems
                    .Where(x => x[0] == "icon")
                    .Select(x => int.Parse(x[1])).FirstOrDefault();

                if (iconSize != 0)
                {
                    REPEAT:
                    // ExtractAssociatedIcon has a bug
                    //  When reading the file icon, it may return incorrect icons in the first tries.
                    //  This happens only for the first reads.
                    //  To fix this, we try to read the icon multiple times,
                    //  for a defined period of time, or while the wrong icon is returned.
                    //  After 10 seconds, if the icon is still wrong, we change opinion and assume it is correct.
                    var icon = Icon.ExtractAssociatedIcon(fileFullName.Replace("/", "\\"));
                    var dateFirstIcon = context.Server[dateFirstIconKey] as DateTime?;
                    if (dateFirstIcon == null
                        || DateTime.UtcNow < dateFirstIcon.Value.AddSeconds(2))
                    {
                        context.Server[dateFirstIconKey] = dateFirstIcon ?? DateTime.UtcNow;
                        await Task.Delay(2000);
                        goto REPEAT;
                    }

                    iconBytes = IconAsSizedPng(icon, iconSize);
                    if (iconBytes.Length == 1092
                        && DateTime.UtcNow < dateFirstIcon.Value.AddSeconds(10))
                    {
                        await Task.Delay(1000);
                        goto REPEAT;
                    }

                    contentType = "image/png";
                }
            }
            else
            {
                // Should redirect to "//Res/DirIcon"
                var queryItems =
                    context.Uri.Query.TrimStart('?').Split('&').Select(x => x.Split("=".ToCharArray(), 2)).ToArray();
                var iconSize =
                    queryItems.Where(x => x[0] == "icon").Select(x => int.Parse(x[1])).FirstOrDefault();
                if (iconSize != 0)
                {
                    iconBytes = Res.GetFolderIconPng();
                    contentType = "image/png";
                }
            }

            if (iconBytes == null)
                return;

            using (var writer = new StreamWriter(context.Output, Encoding.ASCII))
            {
                // writing header
                await writer.WriteHttpHeaderAsync("HTTP/1.1 200 OK");
                await writer.WriteHttpHeaderAsync("Date", DateTime.UtcNow.ToString("R"));
                await writer.WriteHttpHeaderAsync("Server", "Mini-Http-Server");
                await writer.WriteHttpHeaderAsync("Accept-Ranges", "bytes");
                await writer.WriteHttpHeaderAsync("Content-Length", "" + iconBytes.Length);
                await writer.WriteHttpHeaderAsync("Content-Type", contentType);
                await writer.WriteHttpHeaderAsync("Last-Modified", (fileDate ?? DateTime.UtcNow).ToString("R"));
                await writer.WriteHttpHeaderAsync("");

                await writer.FlushAsync();

                // writing file contents
                await context.Output.WriteAsync(iconBytes, 0, iconBytes.Length);
            }
        }

        private static byte[] IconAsSizedPng(Icon icon, int size)
        {
            icon = new Icon(icon, size, size);
            using (icon)
            using (var bmp = icon.ToBitmap())
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return ms.ToArray();
            }
        }
    }
}