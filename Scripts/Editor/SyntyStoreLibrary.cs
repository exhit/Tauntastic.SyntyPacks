using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Tauntastic.SyntyPacks.Editor
{
    /// <summary>
    /// The packs you own in the Synty store, read from a JSON file ({"exportedUtc": "...", "packs": ["POLYGON - Farm Pack", ...]}) because the store's
    /// "Your Library" page is behind a login that Unity cannot use. The file path is a preference; it is re-read whenever it changes on disk.
    /// </summary>
    public static class SyntyStoreLibrary
    {
        private static readonly List<string> _packs = new List<string>();
        private static string _loadedPath;
        private static DateTime _loadedWriteUtc;

        /// <summary>Number of packs in the file, or 0 when it is missing or unreadable.</summary>
        public static int Count { get { Refresh(); return _packs.Count; } }

        /// <summary>When the file was exported from the store page (UTC), or MinValue.</summary>
        public static DateTime ExportedUtc { get; private set; }

        /// <summary>True when the file exists and lists at least one pack.</summary>
        public static bool IsAvailable { get { return Count > 0; } }

        /// <summary>True when the pack is in the library file (same name, ignoring case and punctuation).</summary>
        public static bool Owns(SyntyProduct product)
        {
            Refresh();
            foreach (string name in _packs)
            {
                if (SyntyProduct.SameName(name, product.Title, false))
                    return true;
            }
            return false;
        }

        /// <summary>Re-reads the file when its path or modification time changed.</summary>
        private static void Refresh()
        {
            string path = SyntySettings.LibraryFile;
            DateTime write = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (path == _loadedPath && write == _loadedWriteUtc)
                return;

            _loadedPath = path;
            _loadedWriteUtc = write;
            _packs.Clear();
            ExportedUtc = DateTime.MinValue;
            if (write == DateTime.MinValue)
                return;

            try
            {
                object root;
                var dictionary = SimpleJson.TryParse(File.ReadAllText(path), out root) ? root as Dictionary<string, object> : null;
                if (dictionary == null)
                    return;

                DateTime exported;
                if (DateTime.TryParse(SimpleJson.GetString(dictionary, "exportedUtc"), CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out exported))
                    ExportedUtc = exported;

                object packs;
                if (dictionary.TryGetValue("packs", out packs) && packs is List<object>)
                {
                    foreach (object pack in (List<object>)packs)
                    {
                        var name = pack as string;
                        if (!string.IsNullOrWhiteSpace(name))
                            _packs.Add(name);
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
