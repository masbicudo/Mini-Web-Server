using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace HttpFileServer
{
    public static class FileAssociationHelper
    {
        public static void SetAssociation(string extension, string keyName, string openWith, string fileDescription)
        {
            var baseKey = Registry.ClassesRoot.CreateSubKey(extension);
            Debug.Assert(baseKey != null, "baseKey != null");
            baseKey.SetValue("", keyName);

            var openMethod = Registry.ClassesRoot.CreateSubKey(keyName);
            Debug.Assert(openMethod != null, "openMethod != null");
            openMethod.SetValue("", fileDescription);
            openMethod.CreateSubKey("DefaultIcon").SetValue("", "\"" + openWith + "\",0");
            var shell = openMethod.CreateSubKey("Shell");
            Debug.Assert(shell != null, "shell != null");
            shell.CreateSubKey("edit").CreateSubKey("command").SetValue("", "\"" + openWith + "\"" + " \"%1\"");
            shell.CreateSubKey("open").CreateSubKey("command").SetValue("", "\"" + openWith + "\"" + " \"%1\"");
            baseKey.Close();
            openMethod.Close();
            shell.Close();


            var currentUser = Registry.CurrentUser.OpenSubKey(
                "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\FileExts\\" + extension,
                true);
            if (currentUser != null)
            {
                currentUser.DeleteSubKey("UserChoice", false);
                currentUser.Close();
            }

            // Tell explorer the file association has been changed
            SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
    }
}