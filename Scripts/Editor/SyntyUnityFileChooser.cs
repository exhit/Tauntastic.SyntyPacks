using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Tauntastic.SyntyPacks.Editor
{
    /// <summary>One file listed on a pack's page in the Synty store library, for example "POLYGON_FarmUnity_2022_3 | v1_7_3 (132 MB)".</summary>
    public struct SyntyStoreFile
    {
        public string Name;
        public string Href;
    }

    /// <summary>Picks the Unity package among the files of a pack page (Source files, Unreal, Godot and the Unity package are listed side by side).</summary>
    public static class SyntyUnityFileChooser
    {
        private static readonly Regex _unityVersion = new Regex(@"unity[_ ]?(\d{4})[_.](\d+)", RegexOptions.IgnoreCase);

        /// <summary>
        /// The file for this Unity version, or null when the page has no Unity file. Files are named like "..Unity_2022_3 | v1_7_3": the newest one that is not newer than
        /// <paramref name="editorVersion"/> ("6000.3.10f1", "2022.3.16f1") wins; if all are newer, the oldest one is taken; a Unity file without a version in its name is the last resort.
        /// </summary>
        public static SyntyStoreFile? Choose(IEnumerable<SyntyStoreFile> files, string editorVersion)
        {
            int editorYear, editorMinor;
            ParseEditorVersion(editorVersion, out editorYear, out editorMinor);

            SyntyStoreFile? unversioned = null;
            SyntyStoreFile? best = null;
            SyntyStoreFile? oldest = null;
            int bestKey = -1, oldestKey = int.MaxValue;

            foreach (SyntyStoreFile file in files)
            {
                if (file.Name == null || file.Name.IndexOf("unity", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                Match match = _unityVersion.Match(file.Name);
                if (!match.Success)
                {
                    if (unversioned == null)
                        unversioned = file;
                    continue;
                }

                int key = int.Parse(match.Groups[1].Value) * 100 + int.Parse(match.Groups[2].Value);
                if (key <= editorYear * 100 + editorMinor && key > bestKey)
                {
                    bestKey = key;
                    best = file;
                }
                if (key < oldestKey)
                {
                    oldestKey = key;
                    oldest = file;
                }
            }

            return best ?? oldest ?? unversioned;
        }

        // "v1_7_3" in "POLYGON_FarmUnity_2022_3 | v1_7_3 (132 MB)" or "..._v1.7.3.unitypackage"; a "v" glued to a letter or digit (such as in a word) is not a version.
        private static readonly Regex _packVersion = new Regex(@"(?<![A-Za-z0-9])v(\d+(?:[_.]\d+)*)", RegexOptions.IgnoreCase);

        /// <summary>
        /// The pack version in a file name or file entry name as dotted text ("1.7.3"), or an empty string when there is none. The Unity version part of the name
        /// ("Unity_2022_3") is not a pack version.
        /// </summary>
        public static string ParseVersion(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            Match match = _packVersion.Match(name);
            return match.Success ? match.Groups[1].Value.Replace('_', '.') : string.Empty;
        }

        private static void ParseEditorVersion(string version, out int year, out int minor)
        {
            year = 0;
            minor = 0;
            string[] parts = (version ?? string.Empty).Split('.');
            if (parts.Length > 0) int.TryParse(parts[0], out year);
            if (parts.Length > 1) int.TryParse(parts[1], out minor);
        }
    }
}
