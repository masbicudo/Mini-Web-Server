using System;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HttpFileServer
{
    class Program
    {
        static void Main(string[] args)
        {
            InitCloseHandle();
            InitTrayIcon();

            Console.BufferWidth = 200;

            var appSettings_Port = ConfigurationManager.AppSettings["port"];

            var start = StringComparer.InvariantCultureIgnoreCase.Equals(
                ConfigurationManager.AppSettings["start-browser"],
                "TRUE");

            var server = new MyHttpServer();
            var portToUse = 0;
            for (int it = 0; it < 100; it++)
                try
                {
                    portToUse = int.Parse(appSettings_Port) + it;
                    server.Start(portToUse);
                    break;
                }
                catch
                {
                }

            TrayIcon.Text = string.Format("Serving on port {0}", portToUse);
            TrayIcon.ShowBalloonTip(
                5000,
                "Http File Server",
                string.Format("Port {0}\nDir: {1}", portToUse, server.BasePath),
                ToolTipIcon.None);

            if (start)
            {
                Process.Start("http://localhost:" + portToUse);
            }

            Application.Run();
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
            TrayIcon.ContextMenuStrip.Items.AddRange(new ToolStripItem[] { new ToolStripMenuItem() });
            TrayIcon.ContextMenuStrip.Items[0].Text = "Exit";
            TrayIcon.ContextMenuStrip.Items[0].Click += smoothExit;

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

        private static void smoothExit(object sender, EventArgs e)
        {
            //TrayIcon.Visible = false;
            Application.Exit();
            //Environment.Exit(1);
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
