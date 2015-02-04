using System;
using System.Runtime.Remoting;
using System.Threading.Tasks;

namespace HttpFileServer
{
    public abstract class HttpRequestHandler
    {
        /// <summary>
        /// Responds a request by writing to the output stream.
        /// </summary>
        /// <param name="context">Context containing the request information.</param>
        /// <returns>A task that denotes that actions to be made.</returns>
        public virtual async Task RespondAsync(HttpContext context)
        {
        }
    }
}
