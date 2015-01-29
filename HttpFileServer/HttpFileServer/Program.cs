using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
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

        static void Main(string[] args)
        {
            InitCloseHandle();
            InitTrayIcon();

            Console.BufferWidth = 200;

            StartServer();

            TrayIcon.Text = string.Format("Serving {0}:{1}", usedHost, usedPort);
            TrayIcon.ShowBalloonTip(
                5000,
                "Http File Server",
                string.Format("Host: {0}\nPort: {1}\nDir: {2}", usedHost, usedPort, server.BasePath),
                ToolTipIcon.None);

            var start = StringComparer.InvariantCultureIgnoreCase.Equals(
                ConfigurationManager.AppSettings["start-browser"],
                "TRUE");

            if (start)
            {
                Process.Start(string.Format("http://{0}:{1}", usedHost, usedPort));
            }

            Application.Run();
        }

        private static void StartServer()
        {
            ConfigurationManager.RefreshSection("appSettings");
            var appSettings_Host = ConfigurationManager.AppSettings["host"];
            var appSettings_Port = ConfigurationManager.AppSettings["port"];

            server = new MyHttpServer
                {
#if DEBUG
                    SerializeResponses = true,
#endif
                };

            usedHost = string.IsNullOrWhiteSpace(appSettings_Host)
                           ? "localhost"
                           : appSettings_Host;

            usedPort = 0;
            for (int it = 0; it < 100; it++)
                try
                {
                    usedPort = int.Parse(appSettings_Port) + it;
                    server.Start(usedHost, usedPort);
                    break;
                }
                catch
                {
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
                    new ToolStripMenuItem("Exit", null, smoothExit),
                });

            TrayIcon.Visible = true;

            ShowWindow(ThisConsole, showWindow);
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
            using (var reader = new myStreamReader(stream, Encoding.ASCII))
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
                    MessageBox.Show("Request returned nothing.");
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

            MessageBox.Show("Data received:\n" + line0, "Result", MessageBoxButtons.OK);
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
