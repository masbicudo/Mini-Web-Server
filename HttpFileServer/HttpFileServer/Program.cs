using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TGREER;

namespace HttpFileServer
{
    class Program
    {
        private static int usedPort;
        private static string usedHost;
        private static MyHttpServer server;
        private static string rootPath;
        private static FileSystemWatcher watcher;

#if DEBUG
        private static readonly bool DEBUG = true;
#else
        private static readonly bool DEBUG = false;
#endif

        [STAThread]
        static void Main(string[] args)
        {
            var allArgs = string.Join(" ", args);
            SetRootPath(allArgs);

            Messages.Init();

            string fileToOpen = "";

            var answers = new ConcurrentQueue<Tuple<int, bool>>();

            Messages.ReceiveString += (str) =>
            {
                var match1 = Regex.Match(str, @"^(?<PID>\d+)\: ARE YOU SERVING ""(?<PATH>[^""]*)""?$");
                if (match1.Success)
                {
                    var path = match1.Groups["PATH"].Value;
                    var procId = int.Parse(match1.Groups["PID"].Value);
                    var proc = Process.GetProcessById(procId);
                    path = path.EndsWith("\\") ? path : path + "\\";
                    var myRoot = rootPath.StartsWith("\\") ? rootPath : rootPath + "\\";
                    proc.SendString(
                        myRoot.StartsWith(path, StringComparison.InvariantCultureIgnoreCase)
                            ? string.Format("{0}: YES", procId)
                            : string.Format("{0}: NO", procId));

                    return;
                }

                var match2 = Regex.Match(str, @"^(?<PID>\d+)\: (?<ANS>YES|NO)$");
                if (match2.Success)
                {
                    var procId = int.Parse(match2.Groups["PI"].Value);
                    var ans = match2.Groups["ANS"].Value;
                    answers.Enqueue(new Tuple<int, bool>(procId, ans == "YES"));
                    return;
                }
            };

            // sending messages to all processes, asking "Are you serving this folder already?"
            foreach (var proc in Process.GetProcesses())
            {
                proc.SendString(
                    string.Format(
                        @"{0}: ARE YOU SERVING ""{1}""?",
                        Process.GetCurrentProcess().Id,
                        rootPath));
            }

            // waiting for answers
            var waitStart = DateTime.UtcNow;
            while (DateTime.UtcNow < waitStart.AddSeconds(DEBUG ? 10 : 0.1))
            {
                while (true)
                {
                    Tuple<int, bool> item;
                    if (!answers.TryDequeue(out item))
                        break;

                    if (item.Item2)
                    {
                        if (string.IsNullOrWhiteSpace(fileToOpen))
                            return;

                        var proc = Process.GetProcessById(item.Item1);
                        proc.SendString(
                            string.Format(
                                @"{0}: OPEN FILE ""{1}""",
                                Process.GetCurrentProcess().Id,
                                fileToOpen));
                    }
                }

                Thread.Sleep(1);
            }

            InitCloseHandle();
            InitTrayIcon();

            Console.BufferWidth = 200;

            StartServer();

            Application.Run();
        }

        private static void SetRootPath(string allArgs)
        {
            var matchHttpFile = Regex.Match(allArgs, @"^\s*""(?<FILE>[^""]+.http)""|(?<FILE>[^\s]+.http)");
            rootPath = null;
            if (matchHttpFile.Success)
                rootPath = Path.GetDirectoryName(matchHttpFile.Groups["FILE"].Value);
#if DEBUG
            // ReSharper disable once AccessToModifiedClosure
            rootPath = rootPath ?? Environment.CurrentDirectory;
            rootPath = rootPath.Substring(
                0,
                rootPath.IndexOf("\\bin\\Debug", StringComparison.Ordinal)
                    .With(x => x < 0 ? rootPath.Length : x));
#endif
        }

        static int? FirstInt(params object[] els)
        {
            foreach (var el in els)
            {
                if (el is int?)
                    return (int?)el;

                if (el != null && Regex.IsMatch(el.ToString(), @"^\d+"))
                    return int.Parse(el.ToString(), CultureInfo.InvariantCulture);
            }

            return null;
        }

        static void WatcherDisposed(object sender, EventArgs e)
        {
        }

        static void WatcherError(object sender, ErrorEventArgs e)
        {
        }

        static void WatcherEvent(object sender, FileSystemEventArgs e)
        {
            StartServer();
        }

        private static void StartServer()
        {
            var execPath = rootPath ?? Environment.CurrentDirectory;
            var conf = HttpFile.Read(Path.Combine(execPath, ".http"));

            rootPath = conf != null && conf.HttpRoot != null ? Path.Combine(execPath, conf.HttpRoot) : execPath;

            if (watcher != null)
            {
                watcher.Created -= WatcherEvent;
                watcher.Deleted -= WatcherEvent;
                watcher.Changed -= WatcherEvent;
                watcher.Renamed -= WatcherEvent;
                watcher.Error -= WatcherError;
                watcher.Disposed -= WatcherDisposed;
            }

            watcher = new FileSystemWatcher(rootPath, "*.http");
            watcher.Created += WatcherEvent;
            watcher.Deleted += WatcherEvent;
            watcher.Changed += WatcherEvent;
            watcher.Renamed += WatcherEvent;
            watcher.Error += WatcherError;
            watcher.Disposed += WatcherDisposed;
            watcher.IncludeSubdirectories = true;
            watcher.EnableRaisingEvents = true;

            ConfigurationManager.RefreshSection("appSettings");
            var appSettings_Host = (conf == null ? null : conf.Host) ?? ConfigurationManager.AppSettings["host"];
            var appSettings_Port = FirstInt(conf == null ? null : conf.Port, ConfigurationManager.AppSettings["port"]) ?? 12345;

            var allTypes = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes());

            var handlersListDic = new ListDictionary<string, string>();
            if (conf != null && conf.Handlers != null && conf.Handlers.Items != null)
                foreach (var itemAction in conf.Handlers.Items)
                {
                    var rem = itemAction as HttpFile.ItemToRemove;
                    if (rem != null)
                    {
                        handlersListDic.Remove(rem.Key);
                        continue;
                    }

                    var add = itemAction as HttpFile.HandlerItemToAdd;
                    if (add != null)
                    {
                        if (string.IsNullOrWhiteSpace(add.Key))
                            handlersListDic.Add(add.Type);
                        else
                            handlersListDic.Add(add.Key, add.Type);
                        continue;
                    }
                }

            var handlers = handlersListDic.ToArray()
                .Select(
                    n => allTypes
                        .Where(t => typeof(HttpRequestHandler).IsAssignableFrom(t))
                        .Where(t => t.FullName == n.ToString(CultureInfo.InvariantCulture))
                        .Select(t => (HttpRequestHandler)Activator.CreateInstance(t))
                        .FirstOrDefault())
                .Where(h => h != null)
                .ToArray();

            if (server != null)
                server.Cancel(true);

            server = new MyHttpServer
                {
                    Handlers = handlers.Length == 0 ? null : handlers,
                    BasePath = rootPath,
#if DEBUG
                    SerializeResponses = conf == null ? true : conf.SerializeResponses,
#else
                    SerializeResponses = conf == null ? false : conf.SerializeResponses,
#endif
                };

            var prevPort = usedPort;
            var prevHost = usedHost;

            usedHost = string.IsNullOrWhiteSpace(appSettings_Host)
                           ? "localhost"
                           : appSettings_Host;

            usedPort = 0;
            for (int it = 0; it < 100; it++)
                try
                {
                    usedPort = appSettings_Port + it;
                    server.Start(usedHost, usedPort);
                    break;
                }
                catch
                {
                }

            TrayIcon.Text = string.Format("Serving {0}:{1}", usedHost, usedPort);
            TrayIcon.ShowBalloonTip(
                5000,
                "Http File Server",
                string.Format("Host: {0}\nPort: {1}\nDir: {2}", usedHost, usedPort, server.BasePath),
                ToolTipIcon.None);

            var start = StringComparer.InvariantCultureIgnoreCase.Equals(
                ConfigurationManager.AppSettings["start-browser"],
                "TRUE") || conf != null && conf.OnStart != null && !string.IsNullOrWhiteSpace(conf.OnStart.OpenInBrowser);

            bool allowOpenInBrowser = prevPort != usedPort || prevHost != usedHost;
            if (start && allowOpenInBrowser)
            {
                var addr = conf != null && conf.OnStart != null ? conf.OnStart.OpenInBrowser : null;
                Process.Start(string.Format("http://{0}:{1}{2}", usedHost, usedPort, addr ?? "/"));
            }
        }

        #region Tray Icon
        // reference: http://sleepycoders.blogspot.com.br/2011/04/c-console-application-in-system-tray.html

        private static readonly NotifyIcon TrayIcon = new NotifyIcon();
        private static readonly IntPtr ThisConsole = GetConsoleWindow();

        private static void InitTrayIcon()
        {
            var currentAssembly = Assembly.GetExecutingAssembly();
            var iconResourceStream = currentAssembly.GetManifestResourceStream("HttpFileServer.console.ico");
            Debug.Assert(iconResourceStream != null, "iconResourceStream != null");
            TrayIcon.Icon = new Icon(iconResourceStream);

            TrayIcon.MouseClick += TrayIcon_Click;

            TrayIcon.ContextMenuStrip = new ContextMenuStrip();
            TrayIcon.ContextMenuStrip.Items.AddRange(new[]
                {
                    new ToolStripMenuItem("Open in browser", null, openInBrowser),
                    new ToolStripMenuItem("Make request", null, testRequest),
                    new ToolStripMenuItem("Restart", null, restart),
                    new ToolStripMenuItem("Associate *.http files", null, assocHttp),
                    new ToolStripMenuItem("Open .http file", null, openHttp),
                    new ToolStripMenuItem("Exit", null, smoothExit),
                });

            TrayIcon.Visible = true;

            ShowWindow(ThisConsole, showWindow);
        }

        private static void assocHttp(object sender, EventArgs e)
        {
            FileAssociationHelper.SetAssociation(
                ".http",
                "Http_Server_File",
                Application.ExecutablePath,
                "Mini HTTP Server Starter");
        }

        private static void openHttp(object sender, EventArgs e)
        {
            var dialog = new OpenFileDialog
                {
                    CheckFileExists = true,
                    DereferenceLinks = true,
                    DefaultExt = "http",
                };

            dialog.ShowDialog();

            SetRootPath(dialog.FileName);
            StartServer();
        }

        private static void TrayIcon_Click(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                //reserve right click for context menu
                showWindow = ++showWindow % 2;
                ShowWindow(ThisConsole, showWindow);
            }
        }

        private static void restart(object sender, EventArgs e)
        {
            server.Cancel(true);

            StartServer();

            TrayIcon.Text = string.Format("Serving {0}:{1}", usedHost, usedPort);
            TrayIcon.ShowBalloonTip(
                5000,
                "Http File Server",
                string.Format("Host: {0}\nPort: {1}\nDir: {2}", usedHost, usedPort, server.BasePath),
                ToolTipIcon.None);
        }

        private static void openInBrowser(object sender, EventArgs e)
        {
            Process.Start("http://" + usedHost + ":" + usedPort);
        }

        private static void smoothExit(object sender, EventArgs e)
        {
            TrayIcon.Visible = false;
            Application.Exit();
            Environment.Exit(1);
        }

        private static void testRequest(object sender, EventArgs e)
        {
            var thread = new Thread(() => TestRequestAsync().Wait());
            thread.Start();
        }

        private static async Task TestRequestAsync()
        {
            var form = new MakeRequestForm();
            form.ShowDialog();
            var uri = form.Uri;

            var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(uri.Host, uri.Port);

            string line0 = null;

            using (var stream = InterceptorStream.Create(
                tcpClient.GetStream(),
                File.Open("read-log", FileMode.Create, FileAccess.Write),
                File.Open("write-log", FileMode.Create, FileAccess.Write)
                ))
            using (var reader = new MyStreamReader(stream, Encoding.ASCII))
            using (var writer = new StreamWriter(stream, Encoding.ASCII))
            {
                // Sending GET request
                var verb = HttpVerbs.Get;
                var pathQuery = uri.PathAndQuery ?? "";
                pathQuery = pathQuery == "" || pathQuery[0] != '/' ? '/' + pathQuery : pathQuery;

                await writer.WriteHttpHeaderAsync(string.Format("GET {0} HTTP/1.1", pathQuery));

                await writer.WriteHttpHeaderAsync("Host", uri.Host);
                await writer.WriteHttpHeaderAsync("Cache-Control", "max-age=0");
                await writer.WriteHttpHeaderAsync("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
                await writer.WriteHttpHeaderAsync("User-Agent", "Mini-Http-Client");
                await writer.WriteHttpHeaderAsync("Accept-Language", "en-US");
                await writer.WriteHttpHeaderAsync("");
                await writer.FlushAsync();



                var line = await reader.ReadLineAsync();
                line0 = line;

                if (line == null)
                {
                    MessageBox.Show("Request returned nothing.", "Result", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                HttpStatusCode statusCode = 0;
                string message = null;

                {
                    var match = Regex.Match(line, @"^HTTP/1.[01] (?<CODE>\d+) (?<MSG>.*)$");
                    if (match.Success)
                    {
                        statusCode = (HttpStatusCode)int.Parse(match.Groups["CODE"].Value);
                        message = match.Groups["MSG"].Value;
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

                string tempStr;
                long contentLength = -1;
                if (headers.TryGetValue("Content-Length", out tempStr))
                    if (!long.TryParse(tempStr, out contentLength))
                        contentLength = -1;

                var readNetPos = stream.ReadLoggers.Single().Position;
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

            MessageBox.Show("Data received:\n" + line0, "Result", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
        }

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private static Int32 showWindow = 0; //0 - SW_HIDE - Hides the window and activates another window.
        #endregion

        #region Trap application termination
        // reference: http://stackoverflow.com/questions/474679/capture-console-exit-c-sharp

        [DllImport("Kernel32")]
        private static extern bool SetConsoleCtrlHandler(EventHandler handler, bool add);

        private static void InitCloseHandle()
        {
            // Some biolerplate to react to close window event, CTRL-C, kill, etc
            _handler += Handler;
            SetConsoleCtrlHandler(_handler, true);
        }

        private delegate bool EventHandler(CtrlType sig);
        static EventHandler _handler;

        enum CtrlType
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6
        }

        private static bool Handler(CtrlType sig)
        {
            // Cleanup code
            TrayIcon.Visible = false;

            //// shutdown right away so there are no lingering threads
            //Environment.Exit(1);

            return true;
        }
        #endregion
    }
}
