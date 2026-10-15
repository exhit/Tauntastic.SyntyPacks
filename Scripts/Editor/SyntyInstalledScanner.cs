using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Tauntastic.SyntyPacks.Editor
{
    /// <summary>Finds Synty packs by name: imported ones as folders under Assets (two levels deep, e.g. Assets/PolygonBattleRoyale), downloaded ones as .unitypackage files in Unity's Asset Store cache.</summary>
    public static class SyntyInstalledScanner
    {
        /// <summary>Match key of a pack to its project folder (relative to the project, forward slashes).</summary>
        public static Dictionary<string, string> ScanProject()
        {
            var found = new Dictionary<string, string>();
            string assets = Application.dataPath;
            foreach (string level1 in SafeDirectories(assets))
            {
                Add(found, level1, assets);
                foreach (string level2 in SafeDirectories(level1))
                    Add(found, level2, assets);
            }
            return found;
        }

        /// <summary>Match key of a pack to the full path of its cached .unitypackage.</summary>
        public static Dictionary<string, string> ScanAssetStoreCache()
        {
            var found = new Dictionary<string, string>();
            foreach (string root in CacheRoots())
            {
                foreach (string publisher in SafeDirectories(root))
                {
                    if (Path.GetFileName(publisher).IndexOf("Synty", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    foreach (string file in SafeFiles(publisher))
                        found[SyntyProduct.MakeKey(Path.GetFileNameWithoutExtension(file))] = file;
                }
            }
            return found;
        }

        /// <summary>
        /// Product handle to the newest .unitypackage or .zip in <paramref name="folder"/> (top level only) whose name contains the pack's name.
        /// When a file name contains several pack names the longest one wins, so "PolygonFantasyKingdom" is not taken for "PolygonFantasy".
        /// </summary>
        public static Dictionary<string, string> ScanSyntyDownloads(IReadOnlyList<SyntyProduct> products, string folder)
        {
            var found = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return found;

            foreach (string file in SafeFiles(folder, "*.unitypackage", SearchOption.TopDirectoryOnly).Concat(SafeFiles(folder, "*.zip", SearchOption.TopDirectoryOnly)))
            {
                string name = SyntyProduct.MakeKey(Path.GetFileNameWithoutExtension(file));
                SyntyProduct best = null;
                foreach (SyntyProduct product in products)
                {
                    string key = product.MatchKey;
                    if (key.Length >= 8 && name.Contains(key) && (best == null || key.Length > best.MatchKey.Length))
                        best = product;
                }

                string existing;
                if (best != null && (!found.TryGetValue(best.Handle, out existing) || File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(existing)))
                    found[best.Handle] = file;
            }
            return found;
        }

        private static void Add(Dictionary<string, string> found, string directory, string assets)
        {
            string key = SyntyProduct.MakeKey(Path.GetFileName(directory));
            if (key.Length > 0 && !found.ContainsKey(key))
                found[key] = "Assets" + directory.Substring(assets.Length).Replace('\\', '/');
        }

        private static IEnumerable<string> CacheRoots()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            yield return Path.Combine(Path.Combine(appData, "Unity"), "Asset Store-5.x");
            string home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            yield return Path.Combine(home, "Library/Unity/Asset Store-5.x");
            yield return Path.Combine(home, ".local/share/unity3d/Asset Store-5.x");
        }

        private static string[] SafeDirectories(string path)
        {
            try { return Directory.Exists(path) ? Directory.GetDirectories(path) : new string[0]; }
            catch (IOException) { return new string[0]; }
            catch (UnauthorizedAccessException) { return new string[0]; }
        }

        private static IEnumerable<string> SafeFiles(string path, string pattern = "*.unitypackage", SearchOption option = SearchOption.AllDirectories)
        {
            try { return Directory.GetFiles(path, pattern, option); }
            catch (IOException) { return new string[0]; }
            catch (UnauthorizedAccessException) { return new string[0]; }
        }
    }
}
