using System.Net.Sockets;

namespace HttpFileServer
{
    public static class SocketExtensions
    {
        // http://blogs.msdn.com/b/pfxteam/archive/2011/12/15/10248293.aspx

        public static SocketAwaitable ReceiveAsync(this Socket socket,
                                                   SocketAwaitable awaitable)
        {
            awaitable.Reset();
            if (!socket.ReceiveAsync(awaitable.m_eventArgs))
                awaitable.m_wasCompleted = true;
            return awaitable;
        }

        public static SocketAwaitable SendAsync(this Socket socket,
                                                SocketAwaitable awaitable)
        {
            awaitable.Reset();
            if (!socket.SendAsync(awaitable.m_eventArgs))
                awaitable.m_wasCompleted = true;
            return awaitable;
        }
    }
}