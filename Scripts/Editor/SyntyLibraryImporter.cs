using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tauntastic.WebBrowser;
using Tauntastic.WebBrowser.Editor;
using UnityEditor;

namespace Tauntastic.SyntyPacks.Editor
{
    /// <summary>
    /// Imports the packs you own in the Synty store using the Tauntastic.WebBrowser package: the store's "Your Library" page opens in a browser tab inside Unity, you sign in there
    /// once (the sign-in stays in the "SyntyStore" browser profile, so later imports need no sign-in), the pack names are read from the page and saved to the Synty library file
    /// (<see cref="SyntyStoreLibrary"/>). No password or cookie is ever seen by this package.
    /// </summary>
    public static class SyntyLibraryImporter
    {
        private const string _PROFILE = "SyntyStore";
        private const int _OPEN_TIMEOUT_MS = 30000;
        private const int _SIGN_IN_TIMEOUT_MS = 10 * 60 * 1000;
        private const int _MAX_PACKS = 1000;
        private const int _MAX_NAME_LENGTH = 200;

        private static CancellationTokenSource _cancel;

        /// <summary>True while an import waits for the browser tab.</summary>
        public static bool IsRunning { get; private set; }

        /// <summary>Human readable state or result of the last import.</summary>
        public static string Status { get; private set; }

        /// <summary>Raised on the main thread when the state changed or packs were imported.</summary>
        public static event Action Changed;

        /// <summary>Opens the Synty store library in the Unity browser tab, waits for you to be signed in, reads the pack names and saves them.</summary>
        public static async void Start()
        {
            if (IsRunning)
            {
                if (EditorWindow.HasOpenInstances<WebBrowserWindow>())
                    EditorWindow.GetWindow<WebBrowserWindow>().Focus();
                return;
            }

            IsRunning = true;
            _cancel = new CancellationTokenSource();
            Status = "Opening your Synty store library in the browser tab. Sign in there if asked (use the e-mail code option; pop-up windows are not shown).";
            RaiseChanged();

            bool openedByUs = !EditorWindow.HasOpenInstances<WebBrowserWindow>();
            WebBrowserWindow window = null;
            try
            {
                window = WebBrowserWindow.OpenAt(SyntySettings.OrdersUrl, _PROFILE);
                if (!await WaitUntilAsync(() => window == null || (window.Screen != null && window.Screen.IsOpen), _OPEN_TIMEOUT_MS))
                    throw new WebBrowserException("The browser did not start. Is Chrome, Edge or Brave installed?");
                if (window == null)
                    throw new WebBrowserException("The browser tab was closed.");

                WebBrowserScreen screen = window.Screen;
                Status = "Waiting for your Synty store library... sign in in the browser tab if asked.";
                RaiseChanged();

                bool ready = await screen.Session.WaitForAsync(SyntyLibraryScripts.LibraryReady, _SIGN_IN_TIMEOUT_MS, 700, _cancel.Token);
                if (!ready)
                    throw new TimeoutException("No library was shown within " + _SIGN_IN_TIMEOUT_MS / 60000 + " minutes.");

                object result = await screen.EvaluateAsync(SyntyLibraryScripts.ReadLibrary, 60000);
                Import(result as List<object>);
            }
            catch (OperationCanceledException)
            {
                Status = "The import was stopped.";
            }
            catch (Exception exception) when (exception is WebBrowserException || exception is TimeoutException || exception is IOException)
            {
                Status = "The import failed: " + exception.Message;
            }
            finally
            {
                IsRunning = false;
                if (_cancel != null)
                    _cancel.Dispose();
                _cancel = null;

                // Close the tab only when this import opened it; the sign-in stays in the profile either way.
                if (openedByUs && window != null)
                    window.Close();
                RaiseChanged();
            }
        }

        /// <summary>Stops a running import.</summary>
        public static void Stop()
        {
            if (_cancel != null)
                _cancel.Cancel();
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

        /// <summary>Validates the pack names read from the page and writes them to the library file.</summary>
        private static void Import(List<object> list)
        {
            var names = new List<string>();
            if (list != null)
            {
                foreach (object item in list)
                {
                    string name = SimpleJson.GetString(item as Dictionary<string, object>, "name").Trim();
                    if (name.Length > 0 && name.Length <= _MAX_NAME_LENGTH && !names.Contains(name) && names.Count < _MAX_PACKS)
                        names.Add(name);
                }
            }

            if (names.Count == 0)
            {
                Status = "The library page showed no pack names.";
                return;
            }

            var builder = new StringBuilder();
            builder.Append("{\n  \"source\": \"syntystore.com Your Library (Unity browser import)\",\n  \"exportedUtc\": \"")
                   .Append(DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")).Append("\",\n  \"packs\": [\n");
            for (int i = 0; i < names.Count; i++)
                builder.Append("    ").Append(JsonText.Quote(names[i])).Append(i < names.Count - 1 ? ",\n" : "\n");
            builder.Append("  ]\n}\n");

            string path = SyntySettings.LibraryFile;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, builder.ToString());
            Status = "Imported " + names.Count + " packs from your Synty store library.";
        }

        private static void RaiseChanged()
        {
            if (Changed != null)
                Changed();
        }
    }
}
