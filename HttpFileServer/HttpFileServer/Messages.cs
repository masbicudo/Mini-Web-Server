using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HttpFileServer
{
    public static class Messages
    {
        [DllImport("User32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int uMsg, int wParam, [MarshalAs(UnmanagedType.BStr)]string lParam);

        //this is a constant indicating the window that we want to send a text message
        private const int WM_SETTEXT = 0X000C;
        private const int WM_USER = 0x0400;
        private const int WM_APP = 0x8000;

        private const int WM_CUSTOM_MSG = WM_APP + 438;

        public static void SendString(this Process process, string message)
        {
            SendMessage(process.MainWindowHandle, WM_CUSTOM_MSG, 0, message);
        }

        public static event Action<string> ReceiveString;

        private class MessageFilter : IMessageFilter
        {
            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg == WM_CUSTOM_MSG && ReceiveString != null)
                {
                    ReceiveString(Marshal.PtrToStringBSTR(m.LParam));
                    return true;
                }

                return false;
            }
        }

        public static void Init()
        {
            Application.AddMessageFilter(new MessageFilter());
        }
    }
}