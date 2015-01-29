using System;

namespace HttpFileServer
{
    internal static class ObjectExtensions
    {
        public static TResult With<T, TResult>(this T value, Func<T, TResult> func)
        {
            return func(value);
        }
    }
}