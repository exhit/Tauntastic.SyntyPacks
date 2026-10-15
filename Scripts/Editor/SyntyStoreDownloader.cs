using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Tauntastic.WebBrowser;
using Tauntastic.WebBrowser.Editor;
using UnityEditor;
using UnityEngine;

namespace Tauntastic.SyntyPacks.Editor
{
    /// <summary>
    /// Downloads the Unity package of a pack you own in the Synty store and imports it. It uses the Tauntastic.WebBrowser tab with the "SyntyStore" profile (sign in there once):
    /// the pack is found in your library, its page is opened, the Unity file among the listed files (Source files, Unreal, Godot, Unity) is chosen for this Unity version and
    /// downloaded by the browser itself, then Unity's import window opens. One download runs at a time because they share the browser tab.
    /// </summary>
    public static class SyntyStoreDownloader
    {
        private const string _PROFILE = "SyntyStore";
        private const int _OPEN_TIMEOUT_MS = 30000;
        private const int _SIGN_IN_TIMEOUT_MS = 10 * 60 * 1000;
        private const int _DOWNLOAD_TIMEOUT_MS = 30 * 60 * 1000;
        private const double _PROGRESS_INTERVAL_SECONDS = 0.4;

        private static readonly object _lock = new object();
        private static string _busyHandle;
        private static string _busyStatus;
        private static string _finishedHandle;
        private static string _finishedStatus;
        private static CancellationTokenSource _cancel;
        private static double _lastProgressEvent;

        /// <summary>Raised on the main thread when a download started, finished or failed.</summary>
        public static event Action Changed;

        /// <summary>Raised on the main thread a few times a second while a download reports progress; meant for a repaint, not a rescan.</summary>
        public static event Action ProgressChanged;

        /// <summary>True while a pack is being downloaded or imported.</summary>
        public static bool IsBusy { get { return _busyHandle != null; } }

        /// <summary>True while this pack is the one being downloaded.</summary>
        public static bool IsDownloading(string handle)
        {
            return _busyHandle == handle;
        }

        /// <summary>Progress of this pack's download, or the result of its last one; empty when there is nothing to say.</summary>
        public static string GetStatus(string handle)
        {
            lock (_lock)
            {
                if (_busyHandle == handle)
                    return _busyStatus;
                return _finishedHandle == handle ? _finishedStatus : string.Empty;
            }
        }

        /// <summary>Stops the running download.</summary>
        public static void Cancel()
        {
            if (_cancel != null)
                _cancel.Cancel();
        }

        /// <summary>The folder the downloaded Unity packages are kept in (per user, shared by all projects).</summary>
        public static string DownloadFolder
        {
            get { return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SyntyStudios"), "Downloads"); }
        }

        /// <summary>Downloads and imports the Unity package of the pack. Progress and errors are reported through <see cref="GetStatus"/>.</summary>
        public static async void DownloadAndImport(SyntyProduct product)
        {
            if (IsBusy)
                return;

            lock (_lock)
            {
                _busyHandle = product.Handle;
                _busyStatus = "Opening your Synty store library...";
                _finishedHandle = null;
            }
            _cancel = new CancellationTokenSource();
            RaiseChanged();

            bool openedByUs = !EditorWindow.HasOpenInstances<WebBrowserWindow>();
            WebBrowserWindow window = null;
            string result;
            try
            {
                window = WebBrowserWindow.OpenAt(SyntySettings.OrdersUrl, _PROFILE);
                if (!await WaitUntilAsync(() => window == null || (window.Screen != null && window.Screen.IsOpen), _OPEN_TIMEOUT_MS))
                    throw new WebBrowserException("The browser did not start. Is Chrome, Edge or Brave installed?");
                if (window == null)
                    throw new WebBrowserException("The browser tab was closed.");

                WebBrowserScreen screen = window.Screen;
                SetStatus("Waiting for your Synty store library... sign in in the browser tab if asked.");
                if (!await screen.Session.WaitForAsync(SyntyLibraryScripts.LibraryReady, _SIGN_IN_TIMEOUT_MS, 700, _cancel.Token))
                    throw new TimeoutException("No library was shown within " + _SIGN_IN_TIMEOUT_MS / 60000 + " minutes.");

                SetStatus("Finding " + product.Title + " in your library...");
                string packPath = FindPack(await screen.EvaluateAsync(SyntyLibraryScripts.ReadLibrary, 60000) as List<object>, product);
                if (packPath == null)
                    throw new WebBrowserException(product.Title + " is not in your Synty store library.");

                SetStatus("Opening the pack page...");
                await screen.NavigateAsync(new Uri(new Uri(SyntySettings.OrdersUrl), packPath).AbsoluteUri);
                if (!await screen.Session.WaitForAsync(SyntyLibraryScripts.PackPageReady, 60000, 500, _cancel.Token))
                    throw new WebBrowserException("The pack page shows no download links.");

                SyntyStoreFile? unityFile = SyntyUnityFileChooser.Choose(ReadFiles(await screen.EvaluateAsync(SyntyLibraryScripts.ReadPackFiles) as List<object>), Application.unityVersion);
                if (unityFile == null)
                    throw new WebBrowserException("The pack page lists no Unity file.");

                string fileName = unityFile.Value.Name;
                SetStatus("Downloading " + fileName + "...");
                string path = await screen.Session.DownloadAsync(unityFile.Value.Href, DownloadFolder, (received, total) => ReportProgress(fileName, received, total), _DOWNLOAD_TIMEOUT_MS, _cancel.Token);

                // The version comes from the file's entry on the pack page ("... | v1_7_3"); it is recorded as installed once Unity has imported the package.
                string version = SyntyUnityFileChooser.ParseVersion(fileName);
                SetStatus("Importing " + Path.GetFileName(path) + "...");
                result = ImportFile(path, product.Handle, version);
            }
            catch (OperationCanceledException)
            {
                result = "The download was stopped.";
            }
            catch (Exception exception) when (exception is WebBrowserException || exception is TimeoutException || exception is IOException || exception is InvalidDataException)
            {
                result = "Failed: " + exception.Message;
            }
            finally
            {
                if (_cancel != null)
                    _cancel.Dispose();
                _cancel = null;

                // Close the tab only when this download opened it; the sign-in stays in the profile either way.
                if (openedByUs && window != null)
                    window.Close();
            }

            lock (_lock)
            {
                _finishedHandle = product.Handle;
                _finishedStatus = result;
                _busyHandle = null;
                _busyStatus = null;
            }
            RaiseChanged();
        }

        /// <summary>The pack page path of the library entry that has this product's name.</summary>
        private static string FindPack(List<object> entries, SyntyProduct product)
        {
            if (entries == null)
                return null;

            foreach (object entry in entries)
            {
                var dictionary = entry as Dictionary<string, object>;
                if (dictionary != null && SyntyProduct.SameName(SimpleJson.GetString(dictionary, "name"), product.Title, false))
                    return SimpleJson.GetString(dictionary, "href");
            }
            return null;
        }

        private static List<SyntyStoreFile> ReadFiles(List<object> entries)
        {
            var files = new List<SyntyStoreFile>();
            if (entries != null)
            {
                foreach (object entry in entries)
                {
                    var dictionary = entry as Dictionary<string, object>;
                    if (dictionary != null)
                        files.Add(new SyntyStoreFile { Name = SimpleJson.GetString(dictionary, "name"), Href = SimpleJson.GetString(dictionary, "href") });
                }
            }
            return files;
        }

        /// <summary>Opens Unity's import window for the downloaded file: a .unitypackage directly, or the .unitypackage inside a .zip.</summary>
        private static string ImportFile(string path, string handle, string version)
        {
            string versionText = version.Length > 0 ? "version " + version : "an unknown version";
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".unitypackage")
            {
                SyntyImportTracker.Expect(path, handle, version);
                AssetDatabase.ImportPackage(path, true);
                return "Downloaded " + versionText + " and opened the import window; the version is recorded when the import is done.";
            }

            if (extension == ".zip")
            {
                using (ZipArchive archive = ZipFile.OpenRead(path))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (!entry.FullName.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string target = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, Path.GetFileName(entry.FullName));
                        entry.ExtractToFile(target, true);
                        SyntyImportTracker.Expect(target, handle, version);
                        AssetDatabase.ImportPackage(target, true);
                        return "Downloaded " + versionText + ", unpacked it and opened the import window; the version is recorded when the import is done.";
                    }
                }
            }

            EditorUtility.RevealInFinder(path);
            return "Downloaded " + Path.GetFileName(path) + ", but it is not a .unitypackage: it was shown in the file manager.";
        }

        private static void ReportProgress(string fileName, long received, long total)
        {
            string percent = total > 0 ? " " + (int)(received * 100L / total) + "% (" + received / (1024 * 1024) + " of " + total / (1024 * 1024) + " MB)" : " " + received / (1024 * 1024) + " MB";
            lock (_lock) { _busyStatus = "Downloading " + fileName + "..." + percent; }
        }

        private static void SetStatus(string text)
        {
            lock (_lock) { _busyStatus = text; }
            RaiseProgressChanged();
        }

        private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs)
        {
            DateTime end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!condition())
            {
                if (DateTime.UtcNow >= end)
                    return false;
                await Task.Delay(200);
            }
            return true;
        }

        private static void RaiseChanged()
        {
            if (Changed != null)
                Changed();
        }

        private static void RaiseProgressChanged()
        {
            if (ProgressChanged != null)
                ProgressChanged();
        }

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            // Progress arrives on a background thread; this timer turns it into repaints on the main thread.
            EditorApplication.update += () =>
            {
                if (_busyHandle != null && EditorApplication.timeSinceStartup - _lastProgressEvent >= _PROGRESS_INTERVAL_SECONDS)
                {
                    _lastProgressEvent = EditorApplication.timeSinceStartup;
                    RaiseProgressChanged();
                }
            };
        }
    }
}
