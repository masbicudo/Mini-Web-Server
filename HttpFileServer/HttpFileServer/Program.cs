using System;
using System.Configuration;
using System.IO;
using System.Reflection;

namespace HttpFileServer
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.BufferWidth = 200;
            var appSettings_Port = ConfigurationManager.AppSettings["port"];
            var server = new MyHttpServer(int.Parse(appSettings_Port));
            server.Start();
        }
    }
}
