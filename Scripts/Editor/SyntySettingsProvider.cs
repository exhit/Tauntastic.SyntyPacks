using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Tauntastic.SyntyPacks.Editor
{
    /// <summary>Preferences page (Preferences > Tools > Tauntastic > Synty Packs, its own tab next to Packages): automatic catalog refresh and its interval, the downloads folder, the Synty library file and the library page.</summary>
    public static class SyntySettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider(SyntySettings.PreferencesPath, SettingsScope.User)
            {
                label = "Synty Packs",
                keywords = new HashSet<string> { "synty", "synty studios", "polygon", "package", "asset store", "update", "library", "download" },
                guiHandler = _ =>
                {
                    EditorGUILayout.Space();
                    SyntySettings.Enabled = EditorGUILayout.Toggle("Refresh catalog on start", SyntySettings.Enabled);
                    using (new EditorGUI.DisabledScope(!SyntySettings.Enabled))
                        SyntySettings.IntervalHours = EditorGUILayout.IntField("Hours between refreshes", SyntySettings.IntervalHours);

                    SyntySettings.DownloadsFolder = EditorGUILayout.TextField(
                        new GUIContent("Synty downloads folder", "Searched for .unitypackage / .zip files downloaded from the Synty store, to detect which packs you own there."),
                        SyntySettings.DownloadsFolder);
                    SyntySettings.LibraryFile = EditorGUILayout.TextField(
                        new GUIContent("Synty library file", "JSON list of the packs you own in the Synty store (exported from your Library page), read to flag Own In Synty Store."),
                        SyntySettings.LibraryFile);
                    SyntySettings.OrdersUrl = EditorGUILayout.TextField("My downloads page", SyntySettings.OrdersUrl);
                    EditorGUILayout.HelpBox(
                        "The catalog comes from the public product list of syntystore.com; no login or password is used or stored. " +
                        "The downloads page opens in your browser, where you are signed in.",
                        MessageType.Info);
                }
            };
        }
    }
}
