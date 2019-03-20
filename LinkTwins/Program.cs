using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CodeProject;

namespace LinkTwins {

    public static class Program {

        private const string LOG_PATH = @"C:\Users\samue\Desktop\minifier-log.txt";
        private const string TARGET_PATH = @"C:\Unity\Shared\";

        [DllImport("kernel32.dll")]
        public static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, SymbolicLink dwFlags);

        public enum SymbolicLink {
            File = 0,
            Directory = 1
        }

        public static ParallelQuery<TSource> Tap<TSource>(this ParallelQuery<TSource> source, Action<TSource> action) {
            return source.Select(item => {
                action(item);
                return item;
            });
        }

        public static IEnumerable<TSource> Tap<TSource>(this IEnumerable<TSource> source, Action<TSource> action) {
            return source.Select(item => {
                action(item);
                return item;
            });
        }

        private static Dictionary<string, string> ReadFromLog() {
            var lines = File.ReadAllLines(LOG_PATH);
            var dic = new Dictionary<string, string>();
            var lastHash = string.Empty;

            for (var i = 0; i < lines.Length; i++) {
                var line = lines[i];

                if (string.IsNullOrWhiteSpace(line))
                    continue;
                if (!line.StartsWith("\t"))
                    lastHash = line;
                else
                    dic.Add(line.Replace("\t", "").Trim(), lastHash);
            }

            return dic;
        }

        private static void Main(params string[] args) {

            var obj = new object();

            var files = ReadFromLog();
            var totalToLink = files.Count;
            var deleted = 0;
            var fails = 0;
            var processed = 0;
            var linked = 0;
            var copied = 0;

            if (false)
                files
                .AsParallel()
                .Tap(kvp => {
                    var source = Path.Combine(TARGET_PATH, kvp.Value);

                    lock (obj)
                        processed++;

                    if (File.Exists(kvp.Key)) {
                        File.Delete(kvp.Key);
                        lock (obj)
                            deleted++;
                    }

                    if (CreateSymbolicLink(kvp.Key, source, SymbolicLink.File))
                        lock (obj)
                            linked++;
                    else {
                        File.Copy(source, kvp.Key); // Restore the file if there's an error
                        lock (obj)
                            fails++;
                    }

                    lock (obj)
                        if (processed % 1000 == 0)
                            Console.WriteLine("Processed {0} of {1} ({2:0.00%}) Symlinked: {3}, Errored: {4}, Deleted: {5}", processed, totalToLink, (float)processed / totalToLink, linked, fails, deleted);
                })
                .ToArray();

            files
            .AsParallel()
            .Tap(kvp => {
                var source = Path.Combine(TARGET_PATH, kvp.Value);

                if (File.Exists(kvp.Key))
                    File.Delete(kvp.Key);

                File.Copy(source, kvp.Key);
                lock (obj)
                    copied++;

                lock (obj)
                    if (copied % 100 == 0)
                        Console.WriteLine("Copied {0} of {1} ({2:0.00%})", copied, totalToLink, (float)copied / totalToLink);
            })
            .ToArray();

            while (true)
                Console.ReadKey(true);

            return;

            if (!Directory.Exists(TARGET_PATH))
                Directory.CreateDirectory(TARGET_PATH);

            files
                .AsParallel()
                .GroupBy(kvp => kvp.Value)
                .Select(group => group.First())
                .Select(kvp => new { hash = kvp.Value, file = new FileInfo(kvp.Key) })
                .Tap(fileAndHash => {
                    var from = fileAndHash.file.FullName;
                    var to = Path.Combine(TARGET_PATH, fileAndHash.hash);

                    File.Copy(from, to, false);
                    Console.WriteLine("Copied {0}", fileAndHash.file.Name);
                })
                .ToArray();

            while (true)
                Console.ReadKey(true);

            return;

            if (File.Exists(@"C:\Users\samue\Desktop\minifier-log-exec.txt"))
                File.Delete(@"C:\Users\samue\Desktop\minifier-log-exec.txt");

            var oldOut = Console.Out;
            var ostrm = new FileStream(@"C:\Users\samue\Desktop\minifier-log-exec.txt", FileMode.OpenOrCreate, FileAccess.Write);
            var writer = new StreamWriter(ostrm);

            writer.AutoFlush = true;

            Console.SetOut(writer);
            Console.Title = "Link Twins";

            var totalBytes = 0L;
            var totalFiles = 0L;
            var filesToHash = 0L;
            var bytesToHash = 0L;
            var hashedFiles = 0L;
            var hashedBytes = 0L;

            var equalFiles = FastDirectoryEnumerator.EnumerateFiles(@"C:\Unity\", "*.*", SearchOption.AllDirectories)
                 .AsParallel()
                 .Tap(file => {
                     lock (obj) {
                         totalFiles++;
                         totalBytes += file.Size;
                     }

                     if (totalFiles % 1000 == 0)
                         Console.WriteLine("Found {0} files ({1})", totalFiles, FormatBytes(totalBytes));
                 })
                 .AsParallel()
                 //.Select(filePath => new FileInfo(filePath))
                 .GroupBy(file => file.Size)
                 .Where(group => group.Count() > 1)
                 //.Tap(group => Console.WriteLine("{0} files named {1}", group.Count(), group.Key))
                 .Tap(group => {
                     lock (obj) {
                         filesToHash += group.Count();
                         bytesToHash += group.Sum(file => file.Size);
                     }
                 })
                 .SelectMany(group => group)
                 .ToArray()
                 .AsParallel()
                 //.GroupBy(file => file.Length)
                 //.Where(group => group.Count() > 1)
                 //.Tap(group => Console.WriteLine("Metadata: {0} with {1} files", group.Key, group.Count()))
                 //.SelectMany(group => group) 
                 .Select(file => new { file, hash = GetChecksumBuffered(File.OpenRead(file.Path)) })
                 .Tap(fileAndHash => {
                     lock (obj) {
                         hashedFiles++;
                         hashedBytes += fileAndHash.file.Size;
                     }

                     if (hashedFiles % 1000 == 0)
                         Console.WriteLine("{0} of {1} ({2:0.00%}) hashed files, {3} of {4} ({5:0.00%}) hashed data",
                             hashedFiles,
                             filesToHash,
                             (float)hashedFiles / filesToHash,
                             FormatBytes(hashedBytes),
                             FormatBytes(bytesToHash),
                             (float)hashedBytes / bytesToHash);
                 })
                 .GroupBy(file => file.hash)
                 .Where(group => group.Count() > 1)
                 //.Tap(group => Console.WriteLine("{0} files equal to {1}", group.Count(), group.First().file.Path))
                 .ToDictionary(group => group.Key, group => group.Select(g => g.file).ToList());

            if (File.Exists(LOG_PATH))
                File.Delete(LOG_PATH);

            using (var log = File.CreateText(LOG_PATH)) {
                foreach (var kvp in equalFiles) {
                    log.WriteLine(kvp.Key);
                    foreach (var file in kvp.Value)
                        log.WriteLine("\t{0}", file.Path);
                    log.WriteLine(string.Empty);
                }
            }

            Console.WriteLine("Final calculations...");

            var finalBytes = equalFiles
                .AsParallel()
                .Select(kvp => kvp.Value.First())
                .Sum(file => file.Size);

            var saveableBytes = equalFiles
                .AsParallel()
                .SelectMany(kvp => kvp.Value)
                .Sum(file => file.Size) - finalBytes;

            Console.WriteLine("{0} of {1} ({2:00.00%}) files can be linked", equalFiles.Count, totalFiles, (float)equalFiles.Count / totalFiles);
            Console.WriteLine("{0} of {1} ({2:00.00%}) could be saved", FormatBytes(saveableBytes), FormatBytes(totalBytes), (float)saveableBytes / totalBytes);
            Console.WriteLine("The final source files would have {0}", FormatBytes(finalBytes));

            //foreach (var f in files)
            //    Console.WriteLine(string.Format("{0} at {1}", f.Key, f.Value.Aggregate("", (a, b) => a + " " + b)));

        }

        private static string FormatBytes(double bytes) {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };

            var order = 0;

            while (bytes >= 1024 && order < sizes.Length - 1) {
                order++;
                bytes = bytes / 1024;
            }

            return string.Format("{0:0.00} {1}", bytes, sizes[order]);
        }

        private static string GetChecksumBuffered(Stream stream) {
            using (stream)
            using (var sha = new SHA256Managed())
            using (var bufferedStream = new BufferedStream(stream, 1024 * 32)) {
                var checksum = sha.ComputeHash(stream);
                return BitConverter.ToString(checksum).Replace("-", string.Empty);
            }
        }

    }
}
