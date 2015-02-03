using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace HttpFileServer
{
    public class ScriptFileHandler : HttpResquestHandler
    {
        private static readonly object fileSystemWatcherKey = new object();
        private static readonly object fileCompiledTypesKey = new object();
        private static readonly object scriptsAppDomainKey = new object();

        public override async Task RespondAsync(HttpContext context)
        {
            var compiledTypes = context.Server.GetOrAddValue(
                fileCompiledTypesKey,
                s => new ConcurrentDictionary<string, Type>());

            var basePath = context.Server.BasePath;
            var fileFullName = Path.Combine(basePath, context.Uri.LocalPath.TrimStart('/').Replace("/", "\\"));
            DateTime? fileDate = null;
            try
            {
                fileDate = File.GetLastWriteTimeUtc(fileFullName);
            }
            catch
            {
            }

            if (!fileDate.HasValue || Path.GetExtension(fileFullName) != ".cs")
                return;

            var type = compiledTypes.GetOrAdd(
                fileFullName,
                k =>
                {
                    context.Server.GetOrAddValue(
                        fileSystemWatcherKey,
                        server =>
                        {
                            var watcher2 = new FileSystemWatcher(server.BasePath, "*.cs") { Site = server };
                            watcher2.Created += WatcherEvent;
                            watcher2.Deleted += WatcherEvent;
                            watcher2.Changed += WatcherEvent;
                            watcher2.Renamed += WatcherEvent;
                            watcher2.Error += WatcherError;
                            watcher2.Disposed += WatcherDisposed;
                            watcher2.IncludeSubdirectories = true;
                            watcher2.EnableRaisingEvents = true;
                            return watcher2;
                        });

                    return CompilationHelper.Compile(fileFullName)
                        .GetTypes()
                        .Single(t => typeof(HttpResquestHandler).IsAssignableFrom(t));
                });

            var handler = (HttpResquestHandler)Activator.CreateInstance(type);

            await handler.RespondAsync(context);
        }

        static void WatcherDisposed(object sender, EventArgs e)
        {
        }

        static void WatcherError(object sender, ErrorEventArgs e)
        {
        }

        static void WatcherEvent(object sender, FileSystemEventArgs e)
        {
            var watcher = (FileSystemWatcher)sender;
            var server = (MyHttpServer)watcher.Site;

            var compiledTypes = server.GetOrAddValue(
                fileCompiledTypesKey,
                s => new ConcurrentDictionary<string, Type>(StringComparer.InvariantCultureIgnoreCase));

            var fullPathDir = e.FullPath;
            if (!fullPathDir.EndsWith("\\", StringComparison.InvariantCulture))
                fullPathDir += "\\";

            Type oldType;
            var allCompiled = compiledTypes.ToArray();
            foreach (var kvp in allCompiled)
                if (kvp.Key.StartsWith(fullPathDir, StringComparison.InvariantCultureIgnoreCase))
                    compiledTypes.TryRemove(kvp.Key, out oldType);

            compiledTypes.TryRemove(e.FullPath, out oldType);
        }
    }
}