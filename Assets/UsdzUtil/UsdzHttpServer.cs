using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Mochi;
using UnityEngine;

namespace UsdzUtil
{
    [ExecuteAlways]
    public class UsdzHttpServer : MonoBehaviour
    {
        [SerializeField]
        private bool autoStart = default;

        [SerializeField]
        private string usdzDirectory = "usdz";

        [SerializeField]
        private int port = 19900;

        public bool IsServing => cancellationTokenSource != null;

        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();


        private void Start()
        {
            Cancel();

            if (!this.autoStart) return;


            StartServer();
        }

        public void StartServer()
        {
            if (this.IsServing) return;

            var cache = new HashSet<string>();
            var s = new HTTPServer();
            var gate = new object();
            s.Get("/", async ctx =>
            {
                var results = new List<UsdzEntry>();
                lock (gate)
                {
                    var dir = this.usdzDirectory;
                    if (string.IsNullOrEmpty(dir))
                    {
                        dir = Directory.GetCurrentDirectory();
                    }

                    var files = Directory.EnumerateFiles(dir)
                        .Where(f => Path.GetExtension(f).ToLower() == ".usdz")
                        .Select(f => new FileInfo(f))
                        .OrderByDescending(t => t.LastWriteTime);
                    
                    foreach (var file in files)
                    {
                        var name = Path.GetFileName(file.FullName).Replace(" ", "_");
                        var nameWithoutExtension = Path.GetFileNameWithoutExtension(file.FullName);
                        var path = $"/usdz/{name}";
                        var thumbnailPath = $"{path}-thumb.png";
                        var thumbnailFilePath = Path.Combine(dir, nameWithoutExtension + ".png");

                        if (!cache.Contains(name))
                        {
                            s.Get(path, async ctx2 =>
                            {
                                ctx2.Response.SetContentType("model/usd");
                                var bin = File.ReadAllBytes(file.FullName);
                                await ctx2.Response.WriteAsync(bin, ctx2.CancellationToken);
                            });


                            s.Get(thumbnailPath, async ctx2 =>
                            {
                                ctx2.Response.SetContentType("image/png");
                                var bin = File.ReadAllBytes(thumbnailFilePath);
                                await ctx2.Response.WriteAsync(bin, ctx2.CancellationToken);
                            });


                            cache.Add(name);
                        }

                        var entry = new UsdzEntry
                        {
                            Name = name,
                            Size = GetFormatSizeString(file.Length),
                            UsdzPath = path,
                            ThumbnailPath =  File.Exists(thumbnailFilePath) ? thumbnailPath : string.Empty,
                        };
                        results.Add(entry);
                    }

                }

                await ctx.Response.WriteAsync(ApplyTopTemplate(results), ctx.CancellationToken);
            });

            var currentCtx = SynchronizationContext.Current;
            this.cancellationTokenSource = new CancellationTokenSource();
            var cts = this.cancellationTokenSource;
            _ = Task.Run(async () =>
            {
                Exception ex = null;
                try
                {
                    await s.StartServeAsync(new IPEndPoint(IPAddress.Any, this.port), this.cancellationTokenSource.Token);
                }
                catch (Exception e)
                {
                    ex = e;
                }

                currentCtx?.Post(_ =>
                {
                    if (this.cancellationTokenSource != cts) return;

                    if (ex != null && !(ex is OperationCanceledException))
                    {
                        Debug.LogError(ex);
                    }

                    Cancel();
                }, null);
            });
        }

        public void Stop()
        {
            if (!this.IsServing) return;
            Cancel();
        }

        private void Cancel()
        {
            if (this.cancellationTokenSource == null) return;

            this.cancellationTokenSource.Cancel();
            this.cancellationTokenSource.Dispose();
            this.cancellationTokenSource = null;
        }

        private void OnDestroy()
        {
            Cancel();
        }

        static string GetFormatSizeString(float size)
        {
            var suffix = new[]{ "", "K", "M", "G" };
            var index = 0;

            while (size >= 1024)
            {
                size /= 1024;
                index++;
            }

            var s = index < suffix.Length ? suffix[index] : "-";
            return $"{size:#,##0.##}{s}B";
        }

        struct UsdzEntry
        {
            public string Name;
            public string Size;
            public string UsdzPath;
            public string ThumbnailPath;
        }

        private static string ApplyTopTemplate(IEnumerable<UsdzEntry> entries)
        {
            var items = entries.Select(e => ApplyItemTemplate(e.Name, e.Size, e.UsdzPath, e.ThumbnailPath));
            return string.Format(TopTemplate, string.Join("", items));
        }

        private static string ApplyItemTemplate(string name, string size, string usdzPath, string thumbnailPath)
        {
            if (string.IsNullOrEmpty(thumbnailPath))
            {
                return string.Format(ItemWithoutThumbnailTemplate, name, size, usdzPath);
            }

            return string.Format(ItemTemplate, name, size, usdzPath, thumbnailPath);
        }

        private static readonly string TopTemplate = @"
<!DOCTYPE html>
<html lang=""ja"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <meta http-equiv=""X-UA-Compatible"" content=""ie=edge"">
    <title>UsdzHttpServer</title>
    <style>
        body {{
            text-align: center;
            background: #f8f8f8;
            font-family: 'ヒラギノ角ゴ Pro W3','ヒラギノ角ゴ W3', 'メイリオ', 'ＭＳ Ｐゴシック',sans-serif;
            color: #464646;
        }}
        p {{
            font-size: 1.2em;
            max-width: 800px;
            width: 80vw;
            margin: 1em auto;
        }}
        div.border {{
            border: 1px solid #d8d8d8;
            max-width: 1000px;
            width: 90vw;
            margin: 1em auto;
        }}
        img.thumbnail {{
            border-radius: 30px;
            width: 250px;
        }}
    </style>
</head>
<body>
    <h1>USDZ Files</h1>
    {0}
</body>
</html>
";

        private static readonly string ItemTemplate = @"
    <h2>{0}({1})</h2>
    <div><a href=""{2}"" rel=""ar""><img src=""{3}"" class=""thumbnail""></a></div>
";

        private static readonly string ItemWithoutThumbnailTemplate = @"
    <h2>{0}({1})</h2>
    <div><a href=""{2}"" rel=""ar"">{0}</a></div>
";
    }
}
