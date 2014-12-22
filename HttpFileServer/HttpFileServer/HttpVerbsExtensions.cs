namespace HttpFileServer
{
    public static class HttpVerbsExtensions
    {
        private static readonly HttpVerbs verbsWithBody =
            HttpVerbs.Post |
            HttpVerbs.Put |
            HttpVerbs.Patch;

        private static readonly HttpVerbs safeVerbs =
            HttpVerbs.Head |
            HttpVerbs.Get |
            HttpVerbs.Options |
            HttpVerbs.Trace;

        private static readonly HttpVerbs idempotentVerbs =
            HttpVerbs.Head |
            HttpVerbs.Get |
            HttpVerbs.Options |
            HttpVerbs.Trace |
            HttpVerbs.Put |
            HttpVerbs.Delete;

        public static bool HasBody(this HttpVerbs verb)
        {
            return (verbsWithBody & verb) != 0;
        }

        public static bool IsSafe(this HttpVerbs verb)
        {
            return (safeVerbs & verb) != 0;
        }

        public static bool IsIdempotent(this HttpVerbs verb)
        {
            return (idempotentVerbs & verb) != 0;
        }
    }
}