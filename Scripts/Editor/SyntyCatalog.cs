using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Tauntastic.SyntyPacks.Editor
{
    /// <summary>
    /// Downloads the public product list of the Synty store (syntystore.com/products.json, no login needed), keeps only the Unity packs
    /// and reads the latest "Version x.y.z (Unity)" from each product's change log. The result is cached in the project's Library folder.
    /// </summary>
    [InitializeOnLoad]
    public static class SyntyCatalog
    {
        private const int _PAGE_SIZE = 250;
        private const int _MAX_PAGES = 12;
        private static readonly string _cachePath = Path.Combine("Library", "SyntyStudiosPackages.catalog");
        private static readonly Regex _unityVersion = new Regex(@"Version\s+(\d+(?:\.\d+)*)\s*\(\s*Unity\s*\)", RegexOptions.IgnoreCase);
        private static readonly Regex _tags = new Regex("<[^>]+>");

        private static readonly List<SyntyProduct> _products = new List<SyntyProduct>();
        private static List<SyntyProduct> _fetched;
        private static UnityWebRequest _request;
        private static int _page;

        public static IReadOnlyList<SyntyProduct> Products { get { return _products; } }
        public static bool IsFetching { get { return _request != null; } }
        public static string Error { get; private set; }

        /// <summary>When the catalog was last downloaded (UTC), or MinValue when never.</summary>
        public static DateTime LastRefreshUtc
        {
            get { return File.Exists(_cachePath) ? File.GetLastWriteTimeUtc(_cachePath) : DateTime.MinValue; }
        }

        /// <summary>Raised on the main thread when the product list or the fetching state changed.</summary>
        public static event Action Changed;

        static SyntyCatalog()
        {
            LoadCache();
            EditorApplication.delayCall += () =>
            {
                if (SyntySettings.Enabled && !Application.isBatchMode && IsDue())
                    Refresh();
            };
        }

        private static bool IsDue()
        {
            return !File.Exists(_cachePath) ||
                   DateTime.UtcNow - File.GetLastWriteTimeUtc(_cachePath) >= TimeSpan.FromHours(SyntySettings.IntervalHours);
        }

        /// <summary>Starts downloading the catalog; does nothing while a download is running.</summary>
        public static void Refresh()
        {
            if (IsFetching)
                return;
            _fetched = new List<SyntyProduct>();
            _page = 1;
            Error = null;
            RequestPage();
        }

        private static void RequestPage()
        {
            _request = UnityWebRequest.Get(SyntySettings.StoreBaseUrl + "/products.json?limit=" + _PAGE_SIZE + "&page=" + _page);
            _request.timeout = 30;
            _request.SendWebRequest();
            EditorApplication.update += Poll;
            RaiseChanged();
        }

        private static void Poll()
        {
            if (_request == null || !_request.isDone)
                return;

            EditorApplication.update -= Poll;
            UnityWebRequest finished = _request;
            _request = null;

            int count;
            if (finished.result != UnityWebRequest.Result.Success)
                Error = "Could not reach the Synty store: " + finished.error;
            else if (!TryParsePage(finished.downloadHandler.text, out count))
                Error = "The Synty store returned a catalog this package cannot read.";
            else if (count >= _PAGE_SIZE && _page < _MAX_PAGES)
            {
                finished.Dispose();
                _page++;
                RequestPage();
                return;
            }
            else
                Commit();

            finished.Dispose();
            RaiseChanged();
        }

        private static bool TryParsePage(string json, out int count)
        {
            count = 0;
            object root;
            var dictionary = SimpleJson.TryParse(json, out root) ? root as Dictionary<string, object> : null;
            object list;
            if (dictionary == null || !dictionary.TryGetValue("products", out list) || !(list is List<object>))
                return false;

            foreach (object item in (List<object>)list)
            {
                count++;
                var product = item as Dictionary<string, object>;
                if (product == null)
                    continue;

                string type = SimpleJson.GetString(product, "product_type");
                if (type.IndexOf("Bundle", StringComparison.OrdinalIgnoreCase) >= 0 || !IsUnityProduct(product))
                    continue;

                string title = SimpleJson.GetString(product, "title");
                string handle = SimpleJson.GetString(product, "handle");
                if (title.Length == 0 || handle.Length == 0)
                    continue;

                _fetched.Add(new SyntyProduct
                {
                    Title = title,
                    Handle = handle,
                    ProductType = type,
                    LatestUnityVersion = HighestUnityVersion(SimpleJson.GetString(product, "body_html"))
                });
            }

            return true;
        }

        /// <summary>True when the product has no tag list at all, or lists the tag "Unity".</summary>
        private static bool IsUnityProduct(Dictionary<string, object> product)
        {
            object tags;
            if (!product.TryGetValue("tags", out tags))
                return true;

            var list = tags as List<object>;
            if (list != null)
            {
                foreach (object tag in list)
                {
                    if (string.Equals(tag as string, "Unity", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }

            var text = tags as string;
            return text == null || text.IndexOf("Unity", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string HighestUnityVersion(string html)
        {
            string best = string.Empty;
            foreach (Match match in _unityVersion.Matches(_tags.Replace(html, " ")))
            {
                string candidate = match.Groups[1].Value;
                if (best.Length == 0 || IsNewer(candidate, best))
                    best = candidate;
            }
            return best;
        }

        /// <summary>Compares dotted numeric versions part by part ("1.10" is newer than "1.9").</summary>
        public static bool IsNewer(string candidate, string current)
        {
            string[] a = candidate.Split('.');
            string[] b = current.Split('.');
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                int x = 0, y = 0;
                if (i < a.Length) int.TryParse(a[i], out x);
                if (i < b.Length) int.TryParse(b[i], out y);
                if (x != y)
                    return x > y;
            }
            return false;
        }

        private static void Commit()
        {
            _fetched.Sort((l, r) => string.Compare(l.Title, r.Title, StringComparison.OrdinalIgnoreCase));
            _products.Clear();
            _products.AddRange(_fetched);

            var builder = new StringBuilder();
            foreach (SyntyProduct p in _products)
                builder.Append(p.Handle).Append('\t').Append(p.Title).Append('\t').Append(p.ProductType).Append('\t').Append(p.LatestUnityVersion).Append('\n');
            try { File.WriteAllText(_cachePath, builder.ToString()); }
            catch (IOException) { }
        }

        private static void LoadCache()
        {
            if (!File.Exists(_cachePath))
                return;

            try
            {
                foreach (string line in File.ReadAllLines(_cachePath))
                {
                    string[] f = line.Split('\t');
                    if (f.Length >= 4)
                        _products.Add(new SyntyProduct { Handle = f[0], Title = f[1], ProductType = f[2], LatestUnityVersion = f[3] });
                }
            }
            catch (IOException) { }
        }

        private static void RaiseChanged()
        {
            if (Changed != null)
                Changed();
        }
    }
}
