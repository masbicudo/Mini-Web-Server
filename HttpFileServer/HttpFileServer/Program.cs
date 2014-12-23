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

            if (start)
                Process.Start("http://localhost:" + portToUse);

            server.Join();
        }
    }
}
