using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Schema;

namespace HttpFileServer
{
    public class MyHttpServer
    {
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();

        private TcpListener tcpListenerIPv4;
        private TcpListener tcpListenerIPv6;
        private Thread thread;
        private int canceled;

        /// <summary>
        /// Starts the file HTTP server.
        /// </summary>
        /// <param name="host">The host to accept requests for.</param>
        /// <param name="port">The port number to accept connections.</param>
        public void Start(string host = "localhost", int port = 80)
        {
            if (host == null)
                throw new ArgumentNullException("host");

            this.thread = new Thread(() =>
                {
                    this.tcpListenerIPv4 = new TcpListener(IPAddress.Any, port);
                    this.tcpListenerIPv4.Start();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("IPv4 Server started!");

                    this.tcpListenerIPv6 = new TcpListener(IPAddress.IPv6Any, port);
                    this.tcpListenerIPv6.Start();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("IPv6 Server started!");

                    var taskIPv4 = this.ListeningToClientsIPv4();
                    var taskIPv6 = this.ListeningToClientsIPv6();
                    this.host = host;

                    try
                    {
                        Task.WaitAll(new[] { taskIPv4, taskIPv6 }, this.cancellation.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        this.tcpListenerIPv4.Stop();
                        this.tcpListenerIPv6.Stop();
                    }

                    Interlocked.Exchange(ref this.canceled, -1);

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("----------------");
                    Console.WriteLine("Server finished!");
                    Console.WriteLine("----------------");
                });
            this.thread.Start();
        }

        public void Join()
        {
            if (this.thread != null && this.thread.IsAlive)
                this.thread.Join();
        }

        public void Cancel(bool waitFullCancellation)
        {
            this.cancellation.Cancel();
            if (waitFullCancellation)
                while (this.canceled == 0)
                    Thread.Sleep(0);
        }

        private async Task ListeningToClientsIPv4()
        {
            try
            {
                while (!this.cancellation.IsCancellationRequested)
                {
                    var tcpClient = await this.tcpListenerIPv4.AcceptTcpClientAsync();
                    // ReSharper disable CSharpWarnings::CS4014
                    //  We won't 'await' the following operation, we want it to run independently.
                    //  We want to accept another tcp client as soon as possible by calling AcceptTcpClientAsync.
                    Task.Run(() => this.HandleClient(tcpClient), this.cancellation.Token);
                    // ReSharper restore CSharpWarnings::CS4014
                }
            }
            catch (OperationCanceledException)
            {
                this.tcpListenerIPv4.Stop();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("ListeningToClientsIPv4: " + ex.Message);
            }
        }

        private async Task ListeningToClientsIPv6()
        {
            try
            {
                while (!this.cancellation.IsCancellationRequested)
                {
                    var tcpClient = await this.tcpListenerIPv6.AcceptTcpClientAsync();
                    // ReSharper disable CSharpWarnings::CS4014
                    //  We won't 'await' the following operation, we want it to run independently.
                    //  We want to accept another tcp client as soon as possible by calling AcceptTcpClientAsync.
                    Task.Run(() => this.HandleClient(tcpClient), this.cancellation.Token);
                    // ReSharper restore CSharpWarnings::CS4014
                }
            }
            catch (OperationCanceledException)
            {
                this.tcpListenerIPv6.Stop();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("ListeningToClientsIPv6: " + ex.Message);
            }
        }

        private static readonly Regex line1Regex = new Regex(@"^(GET|POST|DELETE|HEAD|PUT)(?=\s) ([^ ]*) (.*)$");
        private static readonly Dictionary<string, HttpVerbs> dicVerbToEnum = new Dictionary<string, HttpVerbs>(StringComparer.InvariantCultureIgnoreCase)
            {
                { "DELETE", HttpVerbs.Delete },
                { "GET", HttpVerbs.Get },
                { "HEAD", HttpVerbs.Head },
                { "OPTIONS", HttpVerbs.Options },
                { "PATCH", HttpVerbs.Patch },
                { "POST", HttpVerbs.Post },
                { "PUT", HttpVerbs.Put },
            };

        private string basePath;
        private string host;

        DateTime? dateFirstIcon = null;

        private int serializer = 0;
        public bool SerializeResponses { get; set; }

        private async Task HandleClient(TcpClient tcpClient)
        {
            //// Reusable SocketAsyncEventArgs and awaitable wrapper
            //var args = new SocketAsyncEventArgs();
            //args.SetBuffer(new byte[0x1000], 0, 0x1000);
            //var awaitable = new SocketAwaitable(args);

            try
            {
                if (this.SerializeResponses)
                    while (Interlocked.CompareExchange(ref this.serializer, 1, 0) != 0)
                        await Task.Delay(1);

                var clientEndPoint = tcpClient.Client.LocalEndPoint as IPEndPoint;

                var stream = tcpClient.GetStream();
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine("Handling client");
                using (var reader = new StreamReader(stream, Encoding.ASCII))
                using (var writer = new StreamWriter(stream, Encoding.ASCII))
                {
                    string line;

                    line = await reader.ReadLineAsync();

                    HttpVerbs verb = 0;
                    string path = null;
                    string protocol = null;
                    {
                        var match = line1Regex.Match(line);
                        if (match.Success)
                        {
                            if (!dicVerbToEnum.TryGetValue(match.Groups[1].Value, out verb))
                                throw new Exception("Invalid HTTP verb.");

                            path = match.Groups[2].Value;

                            protocol = match.Groups[3].Value;
                        }
                    }

                    var headers = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
                    bool inHeader = true;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (inHeader && line == "")
                            inHeader = false;

                        if (!inHeader && !verb.HasBody())
                            break;

                        if (inHeader)
                        {
                            var strsHeader = line.Split(":".ToCharArray(), 2);
                            if (strsHeader.Length == 2)
                                headers.Add(strsHeader[0], strsHeader[1].Trim());
                        }
                    }

                    string host;
                    int port;
                    {
                        string hostAndPort;
                        if (headers.TryGetValue("host", out hostAndPort))
                        {
                            var split = hostAndPort.Split(":".ToCharArray(), 2);
                            host = split[0];
                            if (split.Length == 2)
                                int.TryParse(split[1], out port);
                            else
                                port = 80;
                        }
                        else
                        {
                            host = clientEndPoint.Address.ToString();
                            port = clientEndPoint.Port;
                        }
                    }

                    if (host != this.host)
                        throw new Exception("Cannot accept host: " + host);

                    string pathNoQuery;
                    string query = null;
                    {
                        var pos = path.IndexOf('?');
                        if (pos >= 0)
                        {
                            pathNoQuery = path.Substring(0, pos);
                            query = path.Substring(pos);
                        }
                        else
                        {
                            pathNoQuery = path;
                        }
                    }

                    var uri = new UriBuilder("http", host, port, pathNoQuery, query).Uri;

                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine(uri);
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("" + verb.ToString().ToUpperInvariant() + " " + path + " " + protocol);
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine(string.Join("\n", headers.Select(kv => kv.Key + ": " + kv.Value)));


                    // SENDING RESPONSE
                    var basePath = BasePath;
                    var fileFullName = Path.Combine(basePath, uri.LocalPath.TrimStart('/'));
                    DateTime? fileDate = null;
                    try
                    {
                        fileDate = File.GetLastWriteTimeUtc(fileFullName);
                    }
                    catch
                    {
                    }

                    string contentType = null;
                    byte[] fileBytes = null;
                    Exception ex = null;
                    try
                    {
                        if (File.Exists(fileFullName))
                        {
                            var queryItems =
                                uri.Query.TrimStart('?').Split('&').Select(x => x.Split("=".ToCharArray(), 2)).ToArray();
                            var iconSize =
                                queryItems.Where(x => x[0] == "icon").Select(x => int.Parse(x[1])).FirstOrDefault();
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
                                if (this.dateFirstIcon == null || DateTime.UtcNow < this.dateFirstIcon.Value.AddSeconds(2))
                                {
                                    this.dateFirstIcon = this.dateFirstIcon ?? DateTime.UtcNow;
                                    while (DateTime.UtcNow < this.dateFirstIcon.Value.AddSeconds(2))
                                        await Task.Delay(500);
                                }

                                fileBytes = IconAsSizedPng(icon, iconSize);
                                if (this.dateFirstIcon == null || fileBytes.Length == 1092
                                    && DateTime.UtcNow < this.dateFirstIcon.Value.AddSeconds(10))
                                {
                                    this.dateFirstIcon = DateTime.UtcNow;
                                    goto REPEAT;
                                }

                                contentType = "image/png";
                            }
                            else
                            {
                                fileBytes = File.ReadAllBytes(fileFullName);
                                contentType = MimeUtils.GetMimeType(Path.GetExtension(fileFullName));
                            }
                        }
                        else if (Directory.Exists(fileFullName))
                        {
                            var queryItems = uri.Query.TrimStart('?').Split('&').Select(x => x.Split("=".ToCharArray(), 2)).ToArray();
                            var iconSize = queryItems.Where(x => x[0] == "icon").Select(x => int.Parse(x[1])).FirstOrDefault();
                            if (iconSize != 0)
                            {
                                fileBytes = Res.GetFolderIconPng();
                                contentType = "image/png";
                            }
                            else
                            {
                                fileBytes = GetDirectoryBytes(fileFullName, uri);
                                contentType = MimeUtils.GetMimeType("html");
                                contentType += "; charset=utf-8";
                            }
                        }
                        else
                        {
                            fileBytes = GetNotFoundBytes(uri);
                            contentType = MimeUtils.GetMimeType("html");
                            contentType += "; charset=utf-8";
                        }
                    }
                    catch (Exception ex1)
                    {
                        ex = ex1;
                    }

                    if (ex != null || fileBytes == null)
                    {
                        fileBytes = GetErrorBytes(ex);
                        contentType = MimeUtils.GetMimeType("html");
                        contentType += "; charset=utf-8";
                    }

                    if (ex != null)
                    {
                        await writer.WriteHttpHeaderAsync("HTTP/1.1 404 Not Found");
                    }
                    else
                    {
                        await writer.WriteHttpHeaderAsync("HTTP/1.1 200 OK");
                    }

                    await writer.WriteHttpHeaderAsync("Date", DateTime.UtcNow.ToString("R"));
                    await writer.WriteHttpHeaderAsync("Server", "Mini-Http-Server");
                    //await writer.WriteLineAsync(@"ETag: ""51142bc1-7449-479b075b2891b""");
                    await writer.WriteHttpHeaderAsync("Accept-Ranges", "bytes");
                    await
                        writer.WriteHttpHeaderAsync(
                            "Content-Length",
                            fileBytes.Length.ToString(CultureInfo.InvariantCulture));
                    await writer.WriteHttpHeaderAsync("Content-Type", contentType);
                    await writer.WriteHttpHeaderAsync("Last-Modified", (fileDate ?? DateTime.UtcNow).ToString("R"));
                    await writer.WriteHttpHeaderAsync("");
                    await writer.FlushAsync();

                    await stream.WriteAsync(fileBytes, 0, fileBytes.Length);
                }

                tcpClient.Close();
            }
            catch (Exception exe)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Exception in HandleClient: " + exe.Message);
            }
            finally
            {
                if (this.SerializeResponses)
                    while (Interlocked.CompareExchange(ref this.serializer, 0, 1) != 1)
                        throw new Exception("Lock broken!");
            }
        }

        public string BasePath
        {
            get
            {
                if (this.basePath != null)
                {
                    return this.basePath;
                }

                var path = Environment.CurrentDirectory;
#if DEBUG
                // ReSharper disable once AccessToModifiedClosure
                path = path.Substring(
                    0,
                    path.IndexOf("\\bin", StringComparison.Ordinal)
                        .With(x => x < 0 ? path.Length : x));
#endif
                return path;
            }

            set
            {
                this.basePath = value;
            }
        }

        private static byte[] IconAsSizedPng(Icon icon, int size)
        {
            icon = new Icon(icon, size, size);
            using (icon)
            {
                using (var bmp = icon.ToBitmap())
                {
                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        return ms.ToArray();
                    }
                }
            }
        }

        private static byte[] GetNotFoundBytes(Uri uri)
        {
            return Encoding.UTF8.GetBytes(@"<!DOCTYPE html>
<html>
    <head>
        <title>Not found</title>
    </head>
    <body>
        <h1>Not found</h1>
        <p>%FILE%</p>
    </body>
</html>
".Replace("%FILE%", uri.LocalPath));
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
                linksList = linksList.Concat(new[]
                    {
                        "<div class='item parent dir'><a href='" + Uri.EscapeUriString("..") + "'>" + ".." + "</a></div>"
                    });

            var currentPath = uri.LocalPath.TrimEnd('/');

            linksList = linksList.Concat(
                dir.GetDirectories().Select(
                    sd => "<div class='item dir'><a href='" + Uri.EscapeUriString(currentPath + '/' + sd.Name) + "'>"
                        + "<img src='" + Uri.EscapeUriString(currentPath + '/' + sd.Name) + "?icon=16" + "' width='16' height='16' />"
                        + sd.Name + "</a></div>"));

            linksList = linksList.Concat(
                dir.GetFiles().Select(
                    sd => "<div class='item file'><a href='" + Uri.EscapeUriString(currentPath + '/' + sd.Name) + "'>"
                        + "<img src='" + Uri.EscapeUriString(currentPath + '/' + sd.Name) + "?icon=16" + "' width='16' height='16' />"
                        + sd.Name + "</a></div>"));

            str = str.Replace("%LIST%", string.Join("\n", linksList));

            return Encoding.UTF8.GetBytes(str);
        }

        private static byte[] GetErrorBytes(Exception ex)
        {
            return Encoding.UTF8.GetBytes(@"<!DOCTYPE html>
<html>
    <head>
        <title>%EX%</title>
    </head>
    <body>
        <h1>%EX%</h1>
        <p>%ERR%</p>
    </body>
</html>
".Replace("%EX%", ex.GetType().Name).Replace("%ERR%", ex.Message));
        }
    }
}
