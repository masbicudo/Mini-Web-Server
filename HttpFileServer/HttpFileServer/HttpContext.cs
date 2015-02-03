using System;
using System.Collections.Generic;
using System.IO;

namespace HttpFileServer
{
    public class HttpContext
    {
        private readonly Uri uri;
        private readonly Dictionary<string, string> headers;
        private readonly Stream outputStream;
        private readonly MyHttpServer server;
        internal bool handled;

        public HttpContext(string uri, Dictionary<string, string> headers, Stream outputStream, MyHttpServer server)
        {
            this.uri = new Uri(uri);
            this.headers = headers;
            this.outputStream = outputStream;
            this.server = server;
        }

        public IDictionary<string, string> Headers
        {
            get { return this.headers; }
        }

        public Stream Output
        {
            get { return this.outputStream; }
        }

        public Uri Uri
        {
            get { return this.uri; }
        }

        public MyHttpServer Server
        {
            get { return server; }
        }

        public void Handled()
        {
            this.handled = true;
        }
    }
}