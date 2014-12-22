using System;

namespace HttpFileServer
{
    /// <summary>
    /// Enumerates the HTTP verbs.
    /// </summary>
    [Flags]
    public enum HttpVerbs
    {
        /// <summary>
        /// Requests that a specified URI be deleted.
        /// </summary>
        Delete = 1,

        /// <summary>
        /// Retrieves the information or entity that is identified by the URI of the request.
        /// </summary>
        Get = 2,

        /// <summary>
        /// Retrieves the message headers for the information or entity that is identified by the URI of the request.
        /// </summary>
        Head = 4,

        /// <summary>
        /// Represents a request for information about the communication options available on the request/response chain identified by the Request-URI.
        /// </summary>
        Options = 8,

        /// <summary>
        /// Requests that a set of changes described in the request entity be applied to the resource identified by the Request- URI.
        /// </summary>
        Patch = 16,

        /// <summary>
        /// Posts a new entity as an addition to a URI.
        /// </summary>
        Post = 32,

        /// <summary>
        /// Replaces an entity that is identified by a URI.
        /// </summary>
        Put = 64,

        /// <summary>
        /// Echoes back the received request.
        /// </summary>
        Trace = 128,
    }
}