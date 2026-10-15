using System;
using UnityEditor;
using UnityEngine;

namespace Tauntastic.SyntyPacks.Editor
{
    /// <summary>Preferences (per machine, EditorPrefs) and the per-project record of which pack version the user has marked as imported.</summary>
    public static class SyntySettings
    {
        public const string PreferencesPath = "Tools/Tauntastic/Synty Packs";
        public const string StoreBaseUrl = "https://syntystore.com";
        public const string DefaultOrdersUrl = "https://syntystore.com/apps/downloads/orders/6072168218876";

        // The key prefix keeps the name the package had before it became com.tauntastic.syntypacks, so saved preferences survive the move.
        private const string _PREFIX = "SyntyStudios.Packages.";

        public static bool Enabled
        {
            get { return EditorPrefs.GetBool(_PREFIX + "Enabled", true); }
            set { EditorPrefs.SetBool(_PREFIX + "Enabled", value); }
        }

        public static int IntervalHours
        {
            get { return Math.Max(1, EditorPrefs.GetInt(_PREFIX + "IntervalHours", 24)); }
            set { EditorPrefs.SetInt(_PREFIX + "IntervalHours", Math.Max(1, value)); }
        }

        /// <summary>Categories (store product types) shown in the Packs window, separated by "|"; Polygon only by default.</summary>
        public static string Categories
        {
            get { return EditorPrefs.GetString(_PREFIX + "Categories", "Polygon"); }
            set { EditorPrefs.SetString(_PREFIX + "Categories", value ?? string.Empty); }
        }

        /// <summary>JSON file listing the packs owned in the Synty store (default: SyntyStudios/SyntyStoreLibrary.json in your user folder).</summary>
        public static string LibraryFile
        {
            get
            {
                string path = EditorPrefs.GetString(_PREFIX + "LibraryFile", string.Empty).Trim();
                return path.Length > 0
                    ? path
                    : System.IO.Path.Combine(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SyntyStudios"), "SyntyStoreLibrary.json");
            }
            set { EditorPrefs.SetString(_PREFIX + "LibraryFile", (value ?? string.Empty).Trim()); }
        }

        /// <summary>Handles of the packs whose row is collapsed in the Packs window, separated by "|".</summary>
        public static string CollapsedHandles
        {
            get { return EditorPrefs.GetString(_PREFIX + "Collapsed", string.Empty); }
            set { EditorPrefs.SetString(_PREFIX + "Collapsed", value ?? string.Empty); }
        }

        /// <summary>The text in the search box of the Packs window.</summary>
        public static string SearchText
        {
            get { return EditorPrefs.GetString(_PREFIX + "Search", string.Empty); }
            set { EditorPrefs.SetString(_PREFIX + "Search", value ?? string.Empty); }
        }

        /// <summary>The "Show installable" toggle of the Packs window.</summary>
        public static bool ShowInstallable
        {
            get { return EditorPrefs.GetBool(_PREFIX + "ShowInstallable", true); }
            set { EditorPrefs.SetBool(_PREFIX + "ShowInstallable", value); }
        }

        /// <summary>How far the Packs window list was scrolled.</summary>
        public static float ScrollY
        {
            get { return EditorPrefs.GetFloat(_PREFIX + "ScrollY", 0f); }
            set { EditorPrefs.SetFloat(_PREFIX + "ScrollY", value); }
        }

        /// <summary>Ownership filters shown in the Packs window (Unity, Synty, NotOwned), separated by "|"; all three (no filtering) by default.</summary>
        public static string OwnershipFilter
        {
            get { return EditorPrefs.GetString(_PREFIX + "OwnershipFilter", "Unity|Synty|NotOwned"); }
            set { EditorPrefs.SetString(_PREFIX + "OwnershipFilter", value ?? string.Empty); }
        }

        /// <summary>Page opened by the "My downloads" button; it needs your Synty store login in the browser.</summary>
        public static string OrdersUrl
        {
            get
            {
                string url = EditorPrefs.GetString(_PREFIX + "OrdersUrl", DefaultOrdersUrl).Trim();
                return url.Length == 0 ? DefaultOrdersUrl : url;
            }
            set { EditorPrefs.SetString(_PREFIX + "OrdersUrl", (value ?? string.Empty).Trim()); }
        }

        /// <summary>Folder searched for packs downloaded from the Synty store (default: the user's Downloads folder).</summary>
        public static string DownloadsFolder
        {
            get
            {
                string folder = EditorPrefs.GetString(_PREFIX + "DownloadsFolder", string.Empty).Trim();
                return folder.Length > 0
                    ? folder
                    : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }
            set { EditorPrefs.SetString(_PREFIX + "DownloadsFolder", (value ?? string.Empty).Trim()); }
        }

        /// <summary>The pack version the user marked as imported in this project, or an empty string when none was recorded.</summary>
        public static string GetRecordedVersion(string handle)
        {
            return EditorPrefs.GetString(VersionKey(handle), string.Empty);
        }

        public static void SetRecordedVersion(string handle, string version)
        {
            EditorPrefs.SetString(VersionKey(handle), version ?? string.Empty);
        }

        private static string VersionKey(string handle)
        {
            return _PREFIX + "Version." + Application.dataPath + "." + handle;
        }
    }
}
