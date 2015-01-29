using System.IO;
using System.Threading.Tasks;

namespace HttpFileServer
{
    public static class StreamWriterExtensions
    {
        public static async Task WriteHttpHeaderAsync(this StreamWriter writer, string name, string value)
        {
            await writer.WriteAsync(string.Format("{0}: {1}\r\n", name, value));
        }

        public static async Task WriteHttpHeaderAsync(this StreamWriter writer, string line)
        {
            await writer.WriteAsync(string.Format("{0}\r\n", line));
        }
    }
}