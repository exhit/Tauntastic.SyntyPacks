using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tauntastic.SyntyPacks.Editor
{
    /// <summary>Editor window (Tools > SyntyStudios > Packs) that lists the Synty packs of the store with their status in this project: update available, imported, downloaded in the Asset Store cache, or not owned/imported.</summary>
    public class SyntyPackWindow : EditorWindow
    {
        private enum PackStatus { UpdateAvailable, Imported, Downloaded, NotImported }

        private sealed class Row
        {
            public SyntyProduct Product;
            public PackStatus Status;
            public string Folder;
            public string CachedFile;
            public string SyntyFile;
            public long ProductId;
            public string RecordedVersion;

            /// <summary>The version of the downloaded package (from the Asset Store cache or the file name), or an empty string when unknown.</summary>
            public string DownloadedVersion = string.Empty;
            public SyntyOwnership Ownership;

            public bool OwnedInAssetStore { get { return Ownership == SyntyOwnership.AssetStore || Ownership == SyntyOwnership.Both; } }
            public bool OwnedInSyntyStore { get { return Ownership == SyntyOwnership.SyntyStore || Ownership == SyntyOwnership.Both; } }
        }

        private const string _TITLE = "Synty Packs";

        private readonly List<Row> _rows = new List<Row>();
        private Vector2 _scroll;
        private string _search = string.Empty;
        private bool _showInstallable = true;

        /// <summary>Category buttons shown first, in this order; other categories found in the catalog follow alphabetically.</summary>
        private static readonly string[] _preferredCategories = { "Animation", "Polygon", "Polygon Mini", "Simple Series", "Sidekick Pack", "Interface" };

        private const string _OTHER = "Other";
        private HashSet<string> _selected;
        private const float _ROW_TINT_ALPHA = 0.18f;
        private static GUIStyle _flagStyle;
        private static GUIStyle _titleStyle;
        private static GUIStyle _wrappedMini;

        /// <summary>Handles of the packs whose row is collapsed to one line; saved in the preferences.</summary>
        private List<string> _collapsed = new List<string>();

        private static string CategoryOf(SyntyProduct product)
        {
            return string.IsNullOrWhiteSpace(product.ProductType) ? _OTHER : product.ProductType.Trim();
        }

        private List<string> Categories()
        {
            var found = new HashSet<string>(SyntyCatalog.Products.Select(CategoryOf));
            var list = _preferredCategories.ToList();
            list.AddRange(found.Where(c => !list.Contains(c)).OrderBy(c => c == _OTHER).ThenBy(c => c, StringComparer.OrdinalIgnoreCase));
            return list;
        }

        private HashSet<string> Selected
        {
            get
            {
                if (_selected == null)
                    _selected = new HashSet<string>(SyntySettings.Categories.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
                return _selected;
            }
        }

        private bool InSelectedCategory(Row row)
        {
            return Selected.Contains(CategoryOf(row.Product));
        }

        private const string _UNITY = "Unity", _SYNTY = "Synty", _NOT_OWNED = "NotOwned";
        private static readonly string[] _ownershipKeys = { _UNITY, _SYNTY, _NOT_OWNED };
        private static readonly string[] _ownershipLabels = { "Unity Store", "Synty Store", "Not Owned" };
        private HashSet<string> _ownSelected;

        private HashSet<string> OwnSelected
        {
            get
            {
                if (_ownSelected == null)
                    _ownSelected = new HashSet<string>(SyntySettings.OwnershipFilter.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
                return _ownSelected;
            }
        }

        /// <summary>With all three ownership buttons on nothing is filtered (that includes packs whose ownership is unknown); otherwise a row must match one of the selected ones.</summary>
        private bool InSelectedOwnership(Row row)
        {
            if (_ownershipKeys.All(OwnSelected.Contains))
                return true;
            return (OwnSelected.Contains(_UNITY) && row.OwnedInAssetStore) ||
                   (OwnSelected.Contains(_SYNTY) && row.OwnedInSyntyStore) ||
                   (OwnSelected.Contains(_NOT_OWNED) && row.Ownership == SyntyOwnership.NotOwned);
        }

        private void DrawOwnershipBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                bool all = _ownershipKeys.All(OwnSelected.Contains);
                if (GUILayout.Toggle(all, "All", EditorStyles.toolbarButton, GUILayout.Width(40f)) != all)
                {
                    OwnSelected.Clear();
                    if (!all)
                        OwnSelected.UnionWith(_ownershipKeys);
                    SaveOwnSelected();
                }

                bool none = _ownershipKeys.All(key => !OwnSelected.Contains(key));
                if (GUILayout.Toggle(none, "None", EditorStyles.toolbarButton, GUILayout.Width(45f)) != none)
                {
                    OwnSelected.Clear();
                    SaveOwnSelected();
                }

                for (int i = 0; i < _ownershipKeys.Length; i++)
                {
                    bool on = OwnSelected.Contains(_ownershipKeys[i]);
                    if (GUILayout.Toggle(on, _ownershipLabels[i], EditorStyles.toolbarButton) != on)
                    {
                        if (on) OwnSelected.Remove(_ownershipKeys[i]); else OwnSelected.Add(_ownershipKeys[i]);
                        SaveOwnSelected();
                    }
                }

                GUILayout.FlexibleSpace();

                if (SyntyLibraryImporter.IsRunning)
                {
                    if (GUILayout.Button(new GUIContent("Stop import", "Stops the import that waits for your Synty store library."), EditorStyles.toolbarButton, GUILayout.Width(80f)))
                    {
                        SyntyLibraryImporter.Stop();
                        Repaint();
                    }
                }
                else if (GUILayout.Button(
                    new GUIContent("Import Synty library", "Opens your Synty store library in a browser tab inside Unity; sign in there once and the packs you own are read automatically."),
                    EditorStyles.toolbarButton, GUILayout.Width(130f)))
                {
                    SyntyLibraryImporter.Start();
                }

                if (GUILayout.Button(new GUIContent("My downloads", "Opens your Synty store downloads page in the browser."), EditorStyles.toolbarButton, GUILayout.Width(95f)))
                    Application.OpenURL(SyntySettings.OrdersUrl);
            }
        }

        private void SaveOwnSelected()
        {
            SyntySettings.OwnershipFilter = string.Join("|", OwnSelected.ToArray());
            UpdateTitle();
        }

        private void DrawCategoryBar()
        {
            List<string> categories = Categories();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                bool all = categories.All(Selected.Contains);
                if (GUILayout.Toggle(all, "All", EditorStyles.toolbarButton, GUILayout.Width(40f)) != all)
                {
                    Selected.Clear();
                    if (!all)
                        Selected.UnionWith(categories);
                    SaveSelected();
                }

                bool none = categories.All(category => !Selected.Contains(category));
                if (GUILayout.Toggle(none, "None", EditorStyles.toolbarButton, GUILayout.Width(45f)) != none)
                {
                    Selected.Clear();
                    SaveSelected();
                }

                foreach (string category in categories)
                {
                    bool on = Selected.Contains(category);
                    if (GUILayout.Toggle(on, category, EditorStyles.toolbarButton) != on)
                    {
                        if (on) Selected.Remove(category); else Selected.Add(category);
                        SaveSelected();
                    }
                }
            }
        }

        private void SaveSelected()
        {
            SyntySettings.Categories = string.Join("|", Selected.ToArray());
            UpdateTitle();
        }

        private void UpdateTitle()
        {
            int updates = _rows.Count(r => r.Status == PackStatus.UpdateAvailable && InSelectedCategory(r));
            titleContent = new GUIContent(updates > 0 ? _TITLE + " (" + updates + ")" : _TITLE);
        }

        [MenuItem("Tools/Tauntastic/Synty Packs", false, 0)]
        public static void Open()
        {
            var window = GetWindow<SyntyPackWindow>(_TITLE);
            window.minSize = new Vector2(860f, 240f);
            window.Show();
        }

        /// <summary>Restores what was chosen last time: which rows are collapsed, the search text, "Show installable" and the scroll position (the category and ownership buttons load themselves).</summary>
        private void LoadViewState()
        {
            _collapsed = new List<string>(SyntySettings.CollapsedHandles.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
            _search = SyntySettings.SearchText;
            _showInstallable = SyntySettings.ShowInstallable;
            _scroll = new Vector2(0f, SyntySettings.ScrollY);
        }

        private void SaveCollapsed()
        {
            SyntySettings.CollapsedHandles = string.Join("|", _collapsed.ToArray());
        }

        private void OnLostFocus()
        {
            SyntySettings.ScrollY = _scroll.y;
        }

        private void OnEnable()
        {
            LoadViewState();
            SyntyCatalog.Changed += Rebuild;
            SyntyMyAssets.Changed += Rebuild;
            SyntyLibraryImporter.Changed += Rebuild;
            SyntyStoreDownloader.Changed += Rebuild;
            SyntyImportTracker.Recorded += Rebuild;
            SyntyProjectWatcher.ProjectChanged += Rebuild;
            SyntyStoreDownloader.ProgressChanged += Repaint;
            Rebuild();

            EditorApplication.delayCall += TryAutoLoad;
        }

        private static bool _autoLoadTried;

        /// <summary>Searching My Assets sends requests to the Unity servers, so it starts when this window is open and the catalog is known, once per session.</summary>
        private void TryAutoLoad()
        {
            // A saved scan younger than "Hours between refreshes" is already loaded (IsLoaded): show it and send no request.
            if (this == null || _autoLoadTried || SyntyMyAssets.IsLoaded || SyntyMyAssets.IsLoading || SyntyCatalog.IsFetching ||
                SyntyCatalog.Products.Count == 0 || !SyntySettings.Enabled)
                return;

            _autoLoadTried = true;
            LoadMyAssets();
        }

        /// <summary>Searches My Assets for every pack of the catalog, those in the selected categories first.</summary>
        private void LoadMyAssets()
        {
            _autoLoadTried = true;
            SyntyMyAssets.Load(SyntyCatalog.Products.OrderBy(p => Selected.Contains(CategoryOf(p)) ? 0 : 1).ToList());
        }

        private void OnDisable()
        {
            SyntySettings.ScrollY = _scroll.y;
            SyntyCatalog.Changed -= Rebuild;
            SyntyMyAssets.Changed -= Rebuild;
            SyntyLibraryImporter.Changed -= Rebuild;
            SyntyStoreDownloader.Changed -= Rebuild;
            SyntyImportTracker.Recorded -= Rebuild;
            SyntyProjectWatcher.ProjectChanged -= Rebuild;
            SyntyStoreDownloader.ProgressChanged -= Repaint;
        }

        /// <summary>Packs whose folder was found in the project at the last rescan; a pack that drops out of it was deleted.</summary>
        private static readonly HashSet<string> _installedHandles = new HashSet<string>();

        private void Rebuild()
        {
            Dictionary<string, string> folders = SyntyInstalledScanner.ScanProject();
            Dictionary<string, string> cached = SyntyInstalledScanner.ScanAssetStoreCache();
            Dictionary<string, string> syntyFiles = SyntyInstalledScanner.ScanSyntyDownloads(SyntyCatalog.Products, SyntySettings.DownloadsFolder);

            _rows.Clear();
            foreach (SyntyProduct product in SyntyCatalog.Products)
            {
                var row = new Row { Product = product, RecordedVersion = SyntySettings.GetRecordedVersion(product.Handle) };
                row.Folder = product.FindIn(folders);
                cached.TryGetValue(product.MatchKey, out row.CachedFile);

                // A pack that was in the project and is gone now (deleted by hand) no longer has an installed version: forget it, so a later import shows the right one.
                // (Only when the scan found folders at all, so a failed scan cannot look like "everything was deleted".)
                if (row.Folder == null && row.RecordedVersion.Length > 0 && folders.Count > 0 && _installedHandles.Contains(product.Handle))
                {
                    SyntySettings.SetRecordedVersion(product.Handle, string.Empty);
                    row.RecordedVersion = string.Empty;
                }
                syntyFiles.TryGetValue(product.Handle, out row.SyntyFile);
                long productId;
                if (SyntyMyAssets.TryGetProductId(product.Handle, out productId))
                    row.ProductId = productId;
                row.DownloadedVersion = row.ProductId != 0 && row.CachedFile != null
                    ? SyntyMyAssets.GetLocalVersion(row.ProductId)
                    : SyntyUnityFileChooser.ParseVersion(row.SyntyFile == null ? null : System.IO.Path.GetFileNameWithoutExtension(row.SyntyFile));
                row.Ownership = DetectOwnership(row.CachedFile != null || row.ProductId != 0, row.SyntyFile != null || SyntyStoreLibrary.Owns(product), SyntyMyAssets.IsLoaded);

                if (row.Folder != null)
                {
                    bool newer = row.RecordedVersion.Length > 0 && product.LatestUnityVersion.Length > 0 &&
                                 SyntyCatalog.IsNewer(product.LatestUnityVersion, row.RecordedVersion);
                    row.Status = newer ? PackStatus.UpdateAvailable : PackStatus.Imported;
                }
                else
                    row.Status = row.CachedFile != null ? PackStatus.Downloaded : PackStatus.NotImported;

                _rows.Add(row);
            }

            _rows.Sort((l, r) => l.Status != r.Status
                ? l.Status.CompareTo(r.Status)
                : string.Compare(l.Product.Title, r.Product.Title, StringComparison.OrdinalIgnoreCase));

            _installedHandles.Clear();
            foreach (Row row in _rows)
            {
                if (row.Folder != null)
                    _installedHandles.Add(row.Product.Handle);
            }

            UpdateTitle();
            Repaint();

            // The catalog may arrive after the window opened: start the My Assets search once it is there.
            EditorApplication.delayCall += TryAutoLoad;
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawCategoryBar();
            DrawOwnershipBar();

            // Whole catalog, independent of the toolbars: Synty packs only.
            DrawSummary();

            if (!string.IsNullOrEmpty(SyntyCatalog.Error))
                EditorGUILayout.HelpBox(SyntyCatalog.Error, MessageType.Warning);

            if (SyntyMyAssets.IsLoaded)
                EditorGUILayout.LabelField(SyntyMyAssets.Status, EditorStyles.miniLabel);
            else
                EditorGUILayout.HelpBox(SyntyMyAssets.Status, SyntyMyAssets.IsLoading ? MessageType.Info : MessageType.Warning);

            if (!string.IsNullOrEmpty(SyntyLibraryImporter.Status))
                EditorGUILayout.HelpBox(SyntyLibraryImporter.Status, SyntyLibraryImporter.IsRunning ? MessageType.Info : MessageType.None);

            EditorGUILayout.LabelField(
                SyntyStoreLibrary.IsAvailable
                    ? "Synty Store library: " + SyntyStoreLibrary.Count + " packs (exported " + SyntyStoreLibrary.ExportedUtc.ToLocalTime().ToString("g") + ")."
                    : "Synty Store library: no file at " + SyntySettings.LibraryFile + ", so Synty Store ownership is only detected from downloads.",
                EditorStyles.miniLabel);

            if (_rows.Count == 0)
            {
                EditorGUILayout.HelpBox(SyntyCatalog.IsFetching
                    ? "Checking..."
                    : "No catalog yet. Press Check now to load the Synty pack list.", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (Row row in _rows)
            {
                if (!Matches(row))
                    continue;
                DrawRow(row);
            }
            EditorGUILayout.EndScrollView();
        }

        private bool Matches(Row row)
        {
            if (!InSelectedCategory(row) || !InSelectedOwnership(row))
                return false;
            if (!_showInstallable && row.Folder == null && _search.Length == 0)
                return false;
            return _search.Length == 0 || row.Product.Title.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(SyntyCatalog.IsFetching))
                {
                    if (GUILayout.Button(SyntyCatalog.IsFetching ? "Loading..." : "Check now", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                        SyntyCatalog.Refresh();
                }

                if (GUILayout.Button("Rescan project", EditorStyles.toolbarButton, GUILayout.Width(95f)))
                    Rebuild();
                using (new EditorGUI.DisabledScope(SyntyMyAssets.IsLoading))
                {
                    if (GUILayout.Button("Reload purchases", EditorStyles.toolbarButton, GUILayout.Width(110f)))
                        LoadMyAssets();
                }

                bool showInstallable = GUILayout.Toggle(_showInstallable, "Show installable", EditorStyles.toolbarButton, GUILayout.Width(110f));
                if (showInstallable != _showInstallable)
                {
                    _showInstallable = showInstallable;
                    SyntySettings.ShowInstallable = showInstallable;
                }

                if (GUILayout.Button("Expand all", EditorStyles.toolbarButton, GUILayout.Width(75f)))
                {
                    _collapsed.Clear();
                    SaveCollapsed();
                }
                if (GUILayout.Button("Collapse all", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                    CollapseAll();

                GUILayout.FlexibleSpace();

                DateTime last = SyntyCatalog.LastRefreshUtc;
                string text = last == DateTime.MinValue ? "Never checked" : "Last check: " + last.ToLocalTime().ToString("g");
                GUILayout.Label(text, EditorStyles.miniLabel);

                string search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.Width(120f));
                if (search != _search)
                {
                    _search = search;
                    SyntySettings.SearchText = search;
                }
                if (GUILayout.Button("Preferences", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                    SettingsService.OpenUserPreferences(SyntySettings.PreferencesPath);
            }
        }

        /// <summary>True when the pack can be installed: it was found in My Assets or as a downloaded package.</summary>
        private static bool IsInstallable(Row row)
        {
            return row.Folder == null && (row.ProductId != 0 || row.CachedFile != null || row.SyntyFile != null);
        }

        /// <summary>The color of the row tint and of its left bar: update, up to date, in the project, installable, or grey.</summary>
        private static Color RowColor(Row row)
        {
            if (row.Status == PackStatus.UpdateAvailable)
                return SyntyPackStyle.UpdateAvailable;
            if (row.Folder != null)
                return row.RecordedVersion.Length > 0 ? SyntyPackStyle.UpToDate : SyntyPackStyle.InProject;
            return IsInstallable(row) ? SyntyPackStyle.Installable : SyntyPackStyle.Unknown;
        }

        /// <summary>The versions of a pack in a few characters, for the header line of a collapsed row.</summary>
        private static string VersionSummary(Row row)
        {
            string latest = row.Product.LatestUnityVersion.Length > 0 ? row.Product.LatestUnityVersion : "-";
            if (row.Folder != null && row.RecordedVersion.Length > 0)
                return row.Status == PackStatus.UpdateAvailable ? row.RecordedVersion + " > " + latest : row.RecordedVersion;
            return "latest " + latest;
        }

        private void DrawRow(Row row)
        {
            Color color = RowColor(row);

            Rect rowRect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rowRect, new Color(color.r, color.g, color.b, _ROW_TINT_ALPHA));
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 4f, rowRect.height), color);
            }

            if (_titleStyle == null)
                _titleStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
            if (_wrappedMini == null)
                _wrappedMini = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };

            string handle = row.Product.Handle;
            bool expanded = !_collapsed.Contains(handle);

            Rect headerRect = EditorGUILayout.BeginHorizontal();

            // The triangle is only drawn here; a click anywhere on the header line folds the row (handled below), so it is not a second control.
            Rect arrowRect = GUILayoutUtility.GetRect(14f, EditorGUIUtility.singleLineHeight, GUILayout.Width(14f));
            if (Event.current.type == EventType.Repaint)
                EditorStyles.foldout.Draw(arrowRect, GUIContent.none, false, false, expanded, false);

            // The title gets a rect exactly as wide as its text, so the version text drawn after it never lands on top of the name.
            var title = new GUIContent(row.Product.Title);
            float titleWidth = _titleStyle.CalcSize(title).x + 4f;
            Rect titleRect = GUILayoutUtility.GetRect(titleWidth, EditorGUIUtility.singleLineHeight, GUILayout.Width(titleWidth));
            GUI.Label(titleRect, title, _titleStyle);

            // A collapsed row is a single line, so show the versions next to the name.
            if (!expanded)
                GUILayout.Label(VersionSummary(row), EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            DrawFlags(row);
            EditorGUILayout.EndHorizontal();

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && headerRect.Contains(Event.current.mousePosition))
            {
                if (expanded) _collapsed.Add(handle); else _collapsed.Remove(handle);
                SaveCollapsed();
                Event.current.Use();
                expanded = !expanded;
            }

            if (!expanded)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            if (row.Folder != null)
            {
                EditorGUILayout.LabelField("Installed", row.RecordedVersion.Length > 0 ? row.RecordedVersion : "version not recorded (press Mark)");
                EditorGUILayout.LabelField("Folder", row.Folder);
            }
            else
                EditorGUILayout.LabelField("Installed", "not imported");
            EditorGUILayout.LabelField("Latest", row.Product.LatestUnityVersion.Length > 0 ? row.Product.LatestUnityVersion : "-");
            EditorGUILayout.LabelField("Category", CategoryOf(row.Product));
            if (row.CachedFile != null || row.SyntyFile != null)
                EditorGUILayout.LabelField("Downloaded", System.IO.Path.GetFileName(row.CachedFile ?? row.SyntyFile) +
                                                         (row.DownloadedVersion.Length > 0 ? "  (version " + row.DownloadedVersion + ")" : string.Empty));
            if (row.ProductId != 0 && SyntyMyAssets.GetInstallStatus(row.ProductId).Length > 0)
                EditorGUILayout.LabelField("Download", SyntyMyAssets.GetInstallStatus(row.ProductId));

            DrawButtons(row);

            EditorGUILayout.EndVertical();
        }

        /// <summary>The colored flags of a row: category, in project, update or up to date or installable, and where the pack is owned.</summary>
        private static void DrawFlags(Row row)
        {
            DrawFlag(CategoryOf(row.Product).ToUpperInvariant(), SyntyPackStyle.Category);
            if (row.Folder != null)
                DrawFlag(SyntyPackStyle.InProjectFlag, SyntyPackStyle.InProject);

            if (row.Status == PackStatus.UpdateAvailable)
                DrawFlag(SyntyPackStyle.UpdateAvailableFlag, SyntyPackStyle.UpdateAvailable);
            else if (row.Folder != null && row.RecordedVersion.Length > 0)
                DrawFlag(SyntyPackStyle.UpToDateFlag, SyntyPackStyle.UpToDate);
            else if (IsInstallable(row))
                DrawFlag(SyntyPackStyle.InstallableFlag, SyntyPackStyle.Installable);

            if (row.OwnedInAssetStore)
                DrawFlag(SyntyPackStyle.OwnInAssetStoreFlag, SyntyPackStyle.OwnInAssetStore);
            if (row.OwnedInSyntyStore)
                DrawFlag(SyntyPackStyle.OwnInSyntyStoreFlag, SyntyPackStyle.OwnInSyntyStore);
            if (row.Ownership == SyntyOwnership.NotOwned)
                DrawFlag(SyntyPackStyle.NotOwnedFlag, SyntyPackStyle.NotOwned);
            else if (row.Ownership == SyntyOwnership.Unknown)
                DrawFlag(SyntyPackStyle.OwnUnknownFlag, SyntyPackStyle.Unknown);
        }

        /// <summary>A strip of flags with the number of packs in each state that has any, for the whole catalog.</summary>
        private void DrawSummary()
        {
            if (_rows.Count == 0)
                return;

            int updates = _rows.Count(r => r.Status == PackStatus.UpdateAvailable);
            int inProject = _rows.Count(r => r.Folder != null);
            int installable = _rows.Count(IsInstallable);
            int assetStore = _rows.Count(r => r.OwnedInAssetStore);
            int syntyStore = _rows.Count(r => r.OwnedInSyntyStore);
            int notOwned = _rows.Count(r => r.Ownership == SyntyOwnership.NotOwned);
            int unknown = _rows.Count(r => r.Ownership == SyntyOwnership.Unknown);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (updates > 0) DrawFlag(updates + " " + SyntyPackStyle.UpdateAvailableFlag, SyntyPackStyle.UpdateAvailable);
                if (installable > 0) DrawFlag(installable + " " + SyntyPackStyle.InstallableFlag, SyntyPackStyle.Installable);
                if (inProject > 0) DrawFlag(inProject + " " + SyntyPackStyle.InProjectFlag, SyntyPackStyle.InProject);
                if (assetStore > 0) DrawFlag(assetStore + " " + SyntyPackStyle.OwnInAssetStoreFlag, SyntyPackStyle.OwnInAssetStore);
                if (syntyStore > 0) DrawFlag(syntyStore + " " + SyntyPackStyle.OwnInSyntyStoreFlag, SyntyPackStyle.OwnInSyntyStore);
                if (notOwned > 0) DrawFlag(notOwned + " " + SyntyPackStyle.NotOwnedFlag, SyntyPackStyle.NotOwned);
                if (unknown > 0) DrawFlag(unknown + " " + SyntyPackStyle.OwnUnknownFlag, SyntyPackStyle.Unknown);
                GUILayout.FlexibleSpace();
            }
        }

        /// <summary>Collapses every pack to its header line.</summary>
        private void CollapseAll()
        {
            _collapsed.Clear();
            foreach (Row row in _rows)
                _collapsed.Add(row.Product.Handle);
            SaveCollapsed();
        }

        /// <summary>Draws a small colored badge with white text.</summary>
        private static void DrawFlag(string text, Color color)
        {
            if (_flagStyle == null)
            {
                _flagStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(6, 6, 2, 2),
                };
                _flagStyle.normal.textColor = Color.white;
            }

            var content = new GUIContent(text);
            float width = _flagStyle.CalcSize(content).x + 4f;
            Rect rect = GUILayoutUtility.GetRect(width, 18f, GUILayout.Width(width));

            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, color);
            GUI.Label(rect, content, _flagStyle);
        }

        /// <summary>
        /// The row's buttons: Synty Store and Unity Store open the pack's pages; Download, Import and Download &amp; Import work on the Asset Store purchase
        /// (through the Package Manager's own services) or on a downloaded .unitypackage, and are disabled when they do not apply.
        /// </summary>
        private void DrawButtons(Row row)
        {
            string importFile = row.CachedFile ??
                                (row.SyntyFile != null && row.SyntyFile.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase) ? row.SyntyFile : null);
            bool downloaded = importFile != null;
            bool purchased = row.ProductId != 0;
            bool busy = purchased && SyntyMyAssets.IsInstalling(row.ProductId);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Synty Store", "Opens the pack's page on syntystore.com."), GUILayout.Width(90f)))
                    Application.OpenURL(row.Product.Url);
                if (GUILayout.Button(new GUIContent("Unity Store", "Searches the Unity Asset Store for this pack."), GUILayout.Width(90f)))
                    Application.OpenURL("https://assetstore.unity.com/search#q=" + Uri.EscapeDataString(row.Product.SearchText));

                if (row.Folder != null && GUILayout.Button("Ping folder", GUILayout.Width(90f)))
                    EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(row.Folder));

                if (row.Folder != null && row.Product.LatestUnityVersion.Length > 0 &&
                    row.RecordedVersion != row.Product.LatestUnityVersion &&
                    GUILayout.Button("Mark " + row.Product.LatestUnityVersion + " as imported", GUILayout.Width(170f)))
                {
                    SyntySettings.SetRecordedVersion(row.Product.Handle, row.Product.LatestUnityVersion);
                    Rebuild();
                    GUIUtility.ExitGUI();
                }
            }

            // Packs owned in the Unity Asset Store: download and import through the Package Manager's services.
            if (row.OwnedInAssetStore)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!purchased || downloaded || busy))
                    {
                        if (GUILayout.Button(new GUIContent("Download", "Downloads the pack from your Asset Store purchases into the Asset Store cache, without importing it."), GUILayout.Width(90f)))
                            SyntyMyAssets.Download(row.ProductId);
                    }

                    using (new EditorGUI.DisabledScope(!downloaded))
                    {
                        if (GUILayout.Button(new GUIContent("Import", "Imports the downloaded .unitypackage (opens Unity's import window) and records its version as installed when the import is done."), GUILayout.Width(90f)))
                        {
                            SyntyImportTracker.Expect(importFile, row.Product.Handle, row.DownloadedVersion);
                            AssetDatabase.ImportPackage(importFile, true);
                        }
                    }

                    using (new EditorGUI.DisabledScope(!purchased || busy))
                    {
                        if (GUILayout.Button(new GUIContent("Download & Import", "Downloads the pack from your Asset Store purchases if needed, then opens Unity's import window."), GUILayout.Width(130f)))
                            SyntyMyAssets.Install(row.ProductId);
                    }
                }
            }

            // Packs owned in the Synty store: the Unity file of the pack's page in your Synty store library, downloaded by the browser tab.
            if (row.OwnedInSyntyStore)
            {
                string handle = row.Product.Handle;
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(SyntyStoreDownloader.IsBusy))
                    {
                        if (GUILayout.Button(new GUIContent("Download & Import (Synty Store)",
                            "Opens your Synty store library in the Unity browser tab, takes the Unity file listed on this pack's page, downloads it and opens Unity's import window. Sign in once in the tab if asked."),
                            GUILayout.Width(210f)))
                            SyntyStoreDownloader.DownloadAndImport(row.Product);
                    }

                    if (SyntyStoreDownloader.IsDownloading(handle) && GUILayout.Button("Stop", GUILayout.Width(50f)))
                        SyntyStoreDownloader.Cancel();

                    string status = SyntyStoreDownloader.GetStatus(handle);
                    if (status.Length > 0)
                        GUILayout.Label(status, _wrappedMini);
                }
            }
        }

        /// <summary>
        /// Asset Store ownership comes from your My Assets list (or the Asset Store download cache); Synty store ownership from a download in the downloads folder,
        /// because the Synty store cannot be queried without its login. Once My Assets is read, a pack in neither place is Not Owned, which can also be a Synty store
        /// purchase you have not downloaded yet; until then it is Unknown.
        /// </summary>
        private static SyntyOwnership DetectOwnership(bool inAssetStore, bool inSyntyDownloads, bool assetStoreListRead)
        {
            if (inAssetStore && inSyntyDownloads)
                return SyntyOwnership.Both;
            if (inAssetStore)
                return SyntyOwnership.AssetStore;
            if (inSyntyDownloads)
                return SyntyOwnership.SyntyStore;
            return assetStoreListRead ? SyntyOwnership.NotOwned : SyntyOwnership.Unknown;
        }
    }
}
