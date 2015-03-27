using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TGREER;

namespace HttpFileServer
{
    public class MyHttpServer : ISite
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

            this.thread = new Thread(
                () =>
                {
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

        private static readonly Dictionary<string, HttpVerbs> dicVerbToEnum =
            new Dictionary<string, HttpVerbs>(StringComparer.InvariantCultureIgnoreCase)
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

        private DateTime? dateFirstIcon = null;

        private int serializer = 0;
        public bool SerializeResponses { get; set; }

        public HttpRequestHandler[] Handlers { get; set; }

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

                using (var stream = ByteCountingStream.Create(tcpClient.GetStream()))
                {
                    Console.ForegroundColor = ConsoleColor.Blue;
                    Console.WriteLine("Handling client");
                    using (var reader = new MyStreamReader(stream, Encoding.ASCII))
                    using (var writer = new StreamWriter(stream, Encoding.ASCII))
                    {
                        string line;

                        line = reader.ReadLine();

                        if (line == null)
                        {
                            var oldColor = Console.ForegroundColor;
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("Null request first line");
                            Console.ForegroundColor = oldColor;
                        }

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

                            if (!inHeader)
                                break;

                            if (inHeader)
                            {
                                var strsHeader = line.Split(":".ToCharArray(), 2);
                                if (strsHeader.Length == 2)
                                    headers.Add(strsHeader[0], strsHeader[1].Trim());
                            }
                        }

                        if (verb.HasBody())
                        {
                            // reading the body of the request
                            string tempStr;
                            long contentLength = -1;
                            if (headers.TryGetValue("Content-Length", out tempStr))
                                if (!long.TryParse(tempStr, out contentLength))
                                    contentLength = -1;

                            var readNetPos = stream.ReadCount;
                            var readStrPos = reader.BytesRead;

                            var buffer = new byte[4096];
                            var remaining = (contentLength < 0 ? long.MaxValue : contentLength) - (readNetPos - readStrPos);
                            var dateLastReceived = DateTime.UtcNow;
                            while (remaining > 0 || contentLength < 0)
                            {
                                var cnt = await stream.ReadAsync(buffer, 0, Math.Min((int)Math.Min(remaining, int.MaxValue), buffer.Length));
                                remaining -= cnt;
                                if (cnt == 0 && contentLength == -1)
                                {
                                    if (DateTime.UtcNow - dateLastReceived > TimeSpan.FromSeconds(5))
                                        break;
                                }
                                else
                                    dateLastReceived = DateTime.UtcNow;
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

                        Console.ForegroundColor = ConsoleColor.DarkGreen;
                        Console.WriteLine("SENDING RESPONSE");

                        // SENDING RESPONSE
                        string contentType = null;
                        byte[] responseBytes = null;
                        Exception ex = null;
                        try
                        {
                            var context = new HttpContext(uri.ToString(), headers, stream, this);
                            var handlers = this.Handlers ?? new HttpRequestHandler[]
                            {
                                new FileIconHandler(), 
                                new ScriptFileHandler(),
                                new FileBytesHandler(),
                                new DirectoryHandler(),
                            };

                            foreach (var handler in handlers)
                            {
                                Console.ForegroundColor = ConsoleColor.DarkGreen;
                                Console.WriteLine("Trying Handler: " + handler.GetType().Name);

                                await handler.RespondAsync(context);
                                if (context.handled)
                                {
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.WriteLine("Handled by: " + handler.GetType().Name);

                                    return;
                                }

                                Console.ForegroundColor = ConsoleColor.DarkGray;
                            }

                            Console.ForegroundColor = ConsoleColor.DarkGreen;
                            Console.WriteLine("Not Handled: repond with not found");

                            responseBytes = GetNotFoundBytes(uri);
                            contentType = MimeUtils.GetMimeType("html");
                            contentType += "; charset=utf-8";
                        }
                        catch (Exception ex1)
                        {
                            ex = ex1;
                        }

                        if (ex != null)
                        {
                            responseBytes = GetErrorBytes(ex);
                            contentType = MimeUtils.GetMimeType("html");
                            contentType += "; charset=utf-8";
                            await writer.WriteHttpHeaderAsync("HTTP/1.1 500 Internal server error");
                        }
                        else if (responseBytes == null)
                        {
                            responseBytes = GetNotFoundBytes(uri);
                            contentType = MimeUtils.GetMimeType("html");
                            contentType += "; charset=utf-8";
                            await writer.WriteHttpHeaderAsync("HTTP/1.1 404 Not found");
                        }

                        if (responseBytes == null)
                            return;

                        await writer.WriteHttpHeaderAsync("Date", DateTime.UtcNow.ToString("R"));
                        await writer.WriteHttpHeaderAsync("Server", "Mini-Http-Server");
                        await writer.WriteHttpHeaderAsync("Accept-Ranges", "bytes");
                        await writer.WriteHttpHeaderAsync("Content-Length", "" + responseBytes.Length);
                        await writer.WriteHttpHeaderAsync("Content-Type", contentType);
                        await writer.WriteHttpHeaderAsync("Last-Modified", DateTime.UtcNow.ToString("R"));
                        await writer.WriteHttpHeaderAsync("");
                        await writer.FlushAsync();

                        await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
                    }
                }
            }
            catch (Exception exe)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Exception in HandleClient: " + exe.Message);
            }
            finally
            {
                tcpClient.Close();

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
                    return this.basePath;

                return Environment.CurrentDirectory;
            }

            set { this.basePath = value; }
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

        ConcurrentDictionary<object, object> dicServerValues = new ConcurrentDictionary<object, object>();

        public object this[object key]
        {
            get
            {
                object value;
                return this.dicServerValues.TryGetValue(key, out value) ? value : null;
            }

            set
            {
                this.dicServerValues[key] = value;
            }
        }

        public T GetOrAddValue<T>(object key, Func<MyHttpServer, T> func)
        {
            return (T)this.dicServerValues.GetOrAdd(key, k => func(this));
        }

        public IComponent Component
        {
            get { return null; }
        }

        public IContainer Container
        {
            get { return null; }
        }

        public bool DesignMode
        {
            get { return false; }
        }

        public string Name { get; set; }

        public object GetService(Type serviceType)
        {
            throw new NotImplementedException();
        }
    }
}
