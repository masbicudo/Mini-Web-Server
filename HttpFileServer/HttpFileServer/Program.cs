using System;
using System.Configuration;
using System.Diagnostics;

namespace HttpFileServer
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.BufferWidth = 200;

            var appSettings_Port = ConfigurationManager.AppSettings["port"];

            var start = StringComparer.InvariantCultureIgnoreCase.Equals(
                ConfigurationManager.AppSettings["start-browser"],
                "TRUE");

            if (start)
                Process.Start("http://localhost:" + appSettings_Port);

            var server = new MyHttpServer(int.Parse(appSettings_Port));
            server.Start();
        }
    }
}
