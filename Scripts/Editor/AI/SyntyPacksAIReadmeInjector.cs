#if UNITY_EDITOR
// Generated from ARTIFICIAL_INTELLIGENCE_README.md of com.tauntastic.syntypacks.
// Keeps the paragraph below in sync with Assets/ARTIFICIAL_INTELLIGENCE_README.md so AI agents can read how to use this package.
namespace Tauntastic.AIReadme
{
    [UnityEditor.InitializeOnLoad]
    internal static class SyntyPacksAIReadmeInjector
    {
        private const string FileName = "ARTIFICIAL_INTELLIGENCE_README.md";
        private const string Header = "# ARTIFICIAL INTELLIGENCE README";
        private const string PackageKey = "Package \"com.tauntastic.syntypacks\"";

        private const string Paragraph = @"Package ""com.tauntastic.syntypacks"" (assembly Tauntastic.SyntyPacks.Editor, namespace Tauntastic.SyntyPacks.Editor, depends on com.tauntastic.webbrowser, editor-only): a window (titled ""Synty Packs"", menu Tools > Tauntastic > Synty Packs, class SyntyPackWindow) that lists the Unity packs of the public Synty store catalog and what the user has done with them. SyntyCatalog downloads syntystore.com/products.json (cached in Library, refreshed every ""hours between refreshes"", Products with ProductType used as category and LatestUnityVersion read from the change log), SyntyInstalledScanner finds imported packs by folder name under Assets, packs in the Unity Asset Store cache and downloads, and SyntyProduct.SameName compares pack names by their identifying words (case, punctuation, plurals and the words Pack, Free and ""Art by Synty"" are ignored). Ownership flags come from SyntyMyAssets (reads the signed-in Unity account's My Assets list through reflection into the Package Manager's internal UnityEditor.PackageManager.UI.Internal services, which may break in other Unity versions; the scan is saved in Library for the refresh interval), from the Synty library file read by SyntyStoreLibrary (JSON with ""packs""; default SyntyStudios/SyntyStoreLibrary.json in the user folder; filled by SyntyLibraryImporter, which opens the Synty store library in a Tauntastic.WebBrowser WebBrowserWindow with the browser profile ""SyntyStore"" and reads the pack names with the page's own sign-in) and from downloads in the downloads folder. Asset Store packs are downloaded and imported with SyntyMyAssets.Install, Synty store packs with SyntyStoreDownloader.DownloadAndImport, which finds the pack's page in the library, picks the Unity file for the current Unity version (SyntyUnityFileChooser) and downloads it with WebBrowserSession.DownloadAsync before opening Unity's import window. The version of every pack this package downloads is known (SyntyUnityFileChooser.ParseVersion reads it from the Synty file entry name such as ""| v1_7_3"", SyntyMyAssets.GetLocalVersion reads it from the Asset Store cache) and SyntyImportTracker.Expect(packagePath, handle, version) must be called right before AssetDatabase.ImportPackage so that the version is recorded as installed (SyntySettings.SetRecordedVersion) when Unity reports the import finished, never when it is cancelled or failed. SyntyProjectWatcher (an AssetPostprocessor) tells the window to rescan when project files change, and a pack whose folder disappears loses its recorded version; SyntyProduct.FindIn matches a pack to its folder by name (a trailing Kit, Pack or FREE of the title is ignored because Synty names the folder of ""POLYGON - Snow Kit"" PolygonSnow). Settings are EditorPrefs keys starting with ""SyntyStudios.Packages."" (the prefix is kept from the package's former name) and a preferences page under Tools/Tauntastic/Synty Packs; the window state (categories, ownership filter, collapsed rows, search) is saved there too. The package never stores passwords or cookies, and it has no runtime code, so never reference it from runtime assemblies.";
        static SyntyPacksAIReadmeInjector()
        {
            UnityEditor.EditorApplication.delayCall += Inject;
        }

        private static void Inject()
        {
            if (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating)
            {
                UnityEditor.EditorApplication.delayCall += Inject;
                return;
            }

            try
            {
                string path = System.IO.Path.Combine(UnityEngine.Application.dataPath, FileName);
                bool fileExisted = System.IO.File.Exists(path);
                string existingText = fileExisted ? Normalize(System.IO.File.ReadAllText(path)) : string.Empty;

                var paragraphs = new System.Collections.Generic.List<string>();
                if (fileExisted)
                {
                    foreach (string block in existingText.Split(new[] { "\n\n" }, System.StringSplitOptions.RemoveEmptyEntries))
                    {
                        string trimmed = block.Trim();
                        if (trimmed.Length > 0)
                            paragraphs.Add(trimmed);
                    }
                }
                else
                {
                    paragraphs.Add(Header);
                }

                // Identical paragraph already present: add nothing.
                bool alreadyPresent = paragraphs.Contains(Paragraph);

                // Drop every other paragraph that refers to this same package (outdated versions).
                paragraphs.RemoveAll(p => p.StartsWith(PackageKey, System.StringComparison.Ordinal) && !string.Equals(p, Paragraph, System.StringComparison.Ordinal));

                if (!alreadyPresent)
                    paragraphs.Add(Paragraph);

                string newText = string.Join("\n\n", paragraphs) + "\n";
                if (fileExisted && string.Equals(newText, existingText, System.StringComparison.Ordinal))
                    return;

                System.IO.File.WriteAllText(path, newText, new System.Text.UTF8Encoding(false));
                UnityEditor.AssetDatabase.ImportAsset("Assets/" + FileName);
            }
            catch (System.Exception exception)
            {
                UnityEngine.Debug.LogWarning("[SyntyPacksAIReadmeInjector] Could not update Assets/" + FileName + ": " + exception.Message);
            }
        }

        private static string Normalize(string text)
        {
            return text.Replace("\r\n", "\n").Replace("\r", "\n");
        }
    }
}
#endif