using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;

namespace Tauntastic.SyntyPacks.Editor
{
    /// <summary>
    /// Finds which Synty packs the signed-in user owns in the Unity Asset Store (Package Manager > My Assets) and downloads and imports a purchased pack,
    /// by calling the Package Manager's own internal services through reflection (UnityEditor.PackageManager.UI.Internal: ServicesContainer, AssetStoreClientV2,
    /// AssetStoreCache, AssetStoreDownloadManager, AssetStorePackageInstaller). The whole purchase list is read in pages of 100 and each Synty store pack
    /// name is compared with the purchase names locally; a purchase counts when it has the same identifying words. Unity offers no public API for this, so every step is
    /// guarded: if a Unity version renames something, <see cref="Status"/> explains it and the rest of the package keeps working from local files.
    /// </summary>
    public static class SyntyMyAssets
    {
        private const string _NAMESPACE = "UnityEditor.PackageManager.UI.Internal.";
        private const BindingFlags _ALL = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const int _PAGE_SIZE = 100;
        private const double _SEARCH_TIMEOUT_SECONDS = 30d;
        private const double _NOT_STARTED_SECONDS = 8d;
        private const double _DOWNLOAD_TIMEOUT_SECONDS = 1800d;

        private static readonly string _savedScanPath = Path.Combine("Library", "SyntyStudiosPackages.myassets");
        private static object _client, _cache, _download, _installer, _connect;
        private static readonly Dictionary<string, long> _owned = new Dictionary<string, long>();
        private static readonly Dictionary<long, double> _pendingDownloads = new Dictionary<long, double>();
        private static readonly Dictionary<long, string> _installStatus = new Dictionary<long, string>();
        private static readonly HashSet<long> _importAfterDownload = new HashSet<long>();
        private static readonly Dictionary<long, string> _purchases = new Dictionary<long, string>();
        private static List<SyntyProduct> _queue;
        private static int _startIndex;
        private static object _lastResult;
        private static long _timestampBefore;
        private static double _requestedAt;

        /// <summary>True once every Synty pack was searched in My Assets.</summary>
        public static bool IsLoaded { get; private set; }

        public static bool IsLoading { get; private set; }

        /// <summary>Human readable state or error of the last attempt.</summary>
        public static string Status { get; private set; }

        /// <summary>Number of Synty packs found in My Assets.</summary>
        public static int OwnedCount { get { return _owned.Count; } }

        /// <summary>Raised on the main thread when the search progressed or an install state changed.</summary>
        public static event Action Changed;

        /// <summary>When the last completed scan finished (UTC); MinValue when there is none.</summary>
        public static DateTime LastScanUtc { get; private set; }

        static SyntyMyAssets()
        {
            Status = "My Assets not read yet.";
            LoadSavedScan();
        }

        /// <summary>Saves the result of a finished scan: purchase count, the Synty packs found (store handle and product id) and the time.</summary>
        private static void SaveScan()
        {
            LastScanUtc = DateTime.UtcNow;
            var builder = new StringBuilder();
            builder.Append("v1\t").Append(LastScanUtc.Ticks).Append('\t').Append(_purchases.Count).Append('\n');
            foreach (KeyValuePair<string, long> pair in _owned)
                builder.Append(pair.Key).Append('\t').Append(pair.Value).Append('\n');
            try { File.WriteAllText(_savedScanPath, builder.ToString()); }
            catch (IOException) { }
        }

        /// <summary>Restores the saved scan when it is younger than the "Hours between refreshes" preference, so the window opens with those results and sends no request.</summary>
        private static void LoadSavedScan()
        {
            LastScanUtc = DateTime.MinValue;
            try
            {
                if (!File.Exists(_savedScanPath))
                    return;

                string[] lines = File.ReadAllLines(_savedScanPath);
                string[] head = lines.Length > 0 ? lines[0].Split('\t') : new string[0];
                long ticks;
                int purchases;
                if (head.Length < 3 || head[0] != "v1" || !long.TryParse(head[1], out ticks) || !int.TryParse(head[2], out purchases))
                    return;

                var scanned = new DateTime(ticks, DateTimeKind.Utc);
                if (DateTime.UtcNow - scanned >= TimeSpan.FromHours(SyntySettings.IntervalHours))
                    return;

                _owned.Clear();
                for (int i = 1; i < lines.Length; i++)
                {
                    string[] fields = lines[i].Split('\t');
                    long id;
                    if (fields.Length == 2 && long.TryParse(fields[1], out id))
                        _owned[fields[0]] = id;
                }

                LastScanUtc = scanned;
                IsLoaded = true;
                Status = "Asset Store: " + purchases + " purchases read, " + _owned.Count + " of them are Synty packs (saved scan from " +
                         scanned.ToLocalTime().ToString("g") + ").";
            }
            catch (Exception exception) when (exception is IOException || exception is FormatException || exception is UnauthorizedAccessException)
            {
                _owned.Clear();
            }
        }

        /// <summary>The Asset Store product id of the owned pack with this Synty store handle.</summary>
        public static bool TryGetProductId(string handle, out long productId)
        {
            return _owned.TryGetValue(handle, out productId);
        }

        /// <summary>Progress or error text of a download/import started with <see cref="Install"/>, or an empty string.</summary>
        public static string GetInstallStatus(long productId)
        {
            string text;
            return _installStatus.TryGetValue(productId, out text) ? text : string.Empty;
        }

        public static bool IsInstalling(long productId)
        {
            return _pendingDownloads.ContainsKey(productId);
        }

        /// <summary>Searches My Assets for each of the packs, in the given order; does nothing while a search is running.</summary>
        public static void Load(IEnumerable<SyntyProduct> products)
        {
            if (IsLoading)
                return;

            try
            {
                if (!Resolve())
                {
                    Fail("The Package Manager internals were not found in this Unity version, so My Assets cannot be read.");
                    return;
                }

                if (!(bool)GetMember(_connect, "isUserLoggedIn"))
                {
                    IsLoaded = false;
                    Status = "Not signed in to Unity (or still signing in): sign in via the Unity Hub or the account button, then press Reload purchases.";
                    RaiseChanged();
                    return;
                }

                _queue = new List<SyntyProduct>(products);
                if (_queue.Count == 0)
                {
                    Status = "The Synty catalog is empty; press Refresh first.";
                    RaiseChanged();
                    return;
                }

                _owned.Clear();
                _purchases.Clear();
                _startIndex = 0;
                IsLoaded = false;
                IsLoading = true;
                RequestPage();
                EditorApplication.update += PollSearch;
                RaiseChanged();
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        /// <summary>Downloads the pack if it is not downloaded yet, then opens Unity's import window for it.</summary>
        public static void Install(long productId)
        {
            Begin(productId, true);
        }

        /// <summary>Downloads the pack into the Asset Store cache without importing it.</summary>
        public static void Download(long productId)
        {
            Begin(productId, false);
        }

        private static void Begin(long productId, bool import)
        {
            try
            {
                if (!Resolve())
                    throw new MissingMemberException("Package Manager internals not found.");

                object local = Call(_cache, "GetLocalInfo", productId);
                var path = local == null ? null : GetMember(local, "packagePath") as string;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    if (import)
                    {
                        _installStatus.Remove(productId);
                        ExpectImport(productId);
                        Call(_installer, "Install", productId, true);
                    }
                    else
                    {
                        _installStatus[productId] = "Already downloaded.";
                        RaiseChanged();
                    }
                    return;
                }

                if (import)
                    _importAfterDownload.Add(productId);
                else
                    _importAfterDownload.Remove(productId);

                _installStatus[productId] = "Starting download...";
                _pendingDownloads[productId] = EditorApplication.timeSinceStartup;
                Call(_download, "Download", new List<long> { productId });
                EditorApplication.update -= PollDownloads;
                EditorApplication.update += PollDownloads;
                RaiseChanged();
            }
            catch (Exception exception)
            {
                _pendingDownloads.Remove(productId);
                _installStatus[productId] = "Failed: " + Describe(exception);
                RaiseChanged();
            }
        }

        /// <summary>The version of the downloaded Asset Store package of this purchase (as the Package Manager recorded it when downloading), or an empty string when it is not downloaded.</summary>
        public static string GetLocalVersion(long productId)
        {
            try
            {
                if (!Resolve())
                    return string.Empty;

                object local = Call(_cache, "GetLocalInfo", productId);
                return local == null ? string.Empty : (GetMember(local, "versionString") as string ?? string.Empty).Trim();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>Tells the import tracker which version of which pack is about to be imported from the Asset Store cache, so it is recorded as installed when Unity finishes.</summary>
        private static void ExpectImport(long productId)
        {
            try
            {
                object local = Call(_cache, "GetLocalInfo", productId);
                var path = local == null ? null : GetMember(local, "packagePath") as string;
                string version = GetLocalVersion(productId);

                foreach (KeyValuePair<string, long> owned in _owned)
                {
                    if (owned.Value == productId)
                    {
                        SyntyImportTracker.Expect(path, owned.Key, version);
                        return;
                    }
                }
            }
            catch (Exception)
            {
                // Recording the version is a convenience: the import itself goes on without it.
            }
        }

        /// <summary>Requests the next page of the purchase list (all of My Assets, no search text).</summary>
        private static void RequestPage()
        {
            Status = "Reading your Asset Store purchases... (" + _purchases.Count + " so far)";

            // PurchasesQueryArgs(int startIndex, int limit, string searchText, PageFilters filters); the last two may be null (no search, no filter).
            Type argsType = FindType("PurchasesQueryArgs");
            object args = Activator.CreateInstance(argsType, _ALL, null, new object[] { _startIndex, _PAGE_SIZE, null, null }, null);

            object operation = GetMember(_client, "m_ListOperation");
            _timestampBefore = operation == null ? 0L : Convert.ToInt64(GetMember(operation, "timestamp"));
            _lastResult = operation == null ? null : GetMember(operation, "result");
            _requestedAt = EditorApplication.timeSinceStartup;
            Call(_client, "ListPurchases", args);
        }

        private static void PollSearch()
        {
            try
            {
                object operation = GetMember(_client, "m_ListOperation");
                bool busy = operation != null && (bool)GetMember(operation, "isInProgress");
                object result = operation == null ? null : GetMember(operation, "result");
                long timestamp = operation == null ? 0L : Convert.ToInt64(GetMember(operation, "timestamp"));
                double waited = EditorApplication.timeSinceStartup - _requestedAt;

                bool started = busy || timestamp != _timestampBefore || !ReferenceEquals(result, _lastResult);
                if (busy)
                {
                    if (waited > _SEARCH_TIMEOUT_SECONDS)
                        FinishLoading("Timed out while searching My Assets.");
                    return;
                }

                // A search that returned nothing may leave no new result object; give up waiting for a start after a few seconds.
                if (!started && waited < _NOT_STARTED_SECONDS)
                    return;

                int before = _purchases.Count;
                long total = 0;
                if (started && result != null && !ReferenceEquals(result, _lastResult))
                {
                    total = Convert.ToInt64(GetMember(result, "total"));
                    CollectPurchases(result);
                }

                if (_purchases.Count == before || _purchases.Count >= total)
                {
                    MatchAll();
                    FinishLoading(null);
                    return;
                }

                _startIndex = _purchases.Count;
                RequestPage();
                RaiseChanged();
            }
            catch (Exception exception)
            {
                FinishLoading("Could not search My Assets: " + Describe(exception));
            }
        }

        /// <summary>Adds the purchases of one result page (id and name) to the full list; results that repeat earlier pages change nothing.</summary>
        private static void CollectPurchases(object result)
        {
            var list = GetMember(result, "list") as IEnumerable;
            if (list == null)
                return;

            foreach (object purchase in list)
                _purchases[Convert.ToInt64(GetMember(purchase, "productId"))] = GetMember(purchase, "displayName") as string ?? string.Empty;
        }

        /// <summary>Matches the Synty store pack names against the purchase names, locally, using the shared identifying-words comparison.</summary>
        private static void MatchAll()
        {
            foreach (SyntyProduct product in _queue)
            {
                foreach (KeyValuePair<long, string> purchase in _purchases)
                {
                    if (!SyntyProduct.SameName(purchase.Value, product.Title))
                        continue;

                    // "Dungeons Pack" counts for "Dungeon Pack", but not when it exactly is another pack's name ("SIMPLE Cars" is not "SIMPLE Car").
                    bool exact = SyntyProduct.SameName(purchase.Value, product.Title, false);
                    if (!exact && _queue.Exists(other => other != product && SyntyProduct.SameName(purchase.Value, other.Title, false)))
                        continue;

                    _owned[product.Handle] = purchase.Key;
                    break;
                }
            }
        }

        private static void FinishLoading(string error)
        {
            EditorApplication.update -= PollSearch;
            IsLoading = false;
            IsLoaded = error == null;
            if (error == null)
                SaveScan();
            Status = error ?? ("Asset Store: " + _purchases.Count + " purchases read, " + _owned.Count + " of them are Synty packs (scanned " +
                               LastScanUtc.ToLocalTime().ToString("g") + ").");
            RaiseChanged();
        }

        private static void PollDownloads()
        {
            var finished = new List<long>();
            foreach (KeyValuePair<long, double> pending in _pendingDownloads)
            {
                long id = pending.Key;
                double waited = EditorApplication.timeSinceStartup - pending.Value;
                try
                {
                    object operation = Call(_download, "GetDownloadOperation", id);
                    if (operation == null)
                    {
                        if (waited > 15d)
                        {
                            _installStatus[id] = "Failed: the download did not start (accept the Asset Store terms in Package Manager once, then retry).";
                            finished.Add(id);
                        }
                        continue;
                    }

                    var error = GetMember(operation, "errorMessage") as string;
                    if (!string.IsNullOrEmpty(error))
                    {
                        _installStatus[id] = "Failed: " + error;
                        finished.Add(id);
                    }
                    else if ((bool)GetMember(operation, "isInProgress"))
                    {
                        float percent = Convert.ToSingle(GetMember(operation, "progressPercentage"));
                        _installStatus[id] = "Downloading " + (int)(percent * 100f) + "%";
                        if (waited > _DOWNLOAD_TIMEOUT_SECONDS)
                        {
                            _installStatus[id] = "Failed: download timed out.";
                            finished.Add(id);
                        }
                    }
                    else if (waited > 2d)
                    {
                        finished.Add(id);
                        if (_importAfterDownload.Remove(id))
                        {
                            _installStatus.Remove(id);
                            ExpectImport(id);
                            Call(_installer, "Install", id, true);
                        }
                        else
                        {
                            string version = GetLocalVersion(id);
                            _installStatus[id] = version.Length > 0 ? "Downloaded version " + version + "." : "Downloaded.";
                        }
                    }
                }
                catch (Exception exception)
                {
                    _installStatus[id] = "Failed: " + Describe(exception);
                    finished.Add(id);
                }
            }

            foreach (long id in finished)
                _pendingDownloads.Remove(id);
            if (_pendingDownloads.Count == 0)
                EditorApplication.update -= PollDownloads;
            RaiseChanged();
        }

        private static bool Resolve()
        {
            if (_client != null && _cache != null && _download != null && _installer != null && _connect != null)
                return true;

            Type containerType = FindType("ServicesContainer");
            if (containerType == null)
                return false;

            PropertyInfo instance = typeof(ScriptableSingleton<>).MakeGenericType(containerType).GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
            object container = instance == null ? null : instance.GetValue(null, null);
            if (container == null)
                return false;

            if (!Assign(container))
            {
                Call(container, "Reload");
                Assign(container);
            }

            return _client != null && _cache != null && _download != null && _installer != null && _connect != null;
        }

        private static bool Assign(object container)
        {
            _client = Service(container, "AssetStoreClientV2", "m_SerializedAssetStoreClient");
            _cache = Service(container, "AssetStoreCache", "m_SerializedAssetStoreCache");
            _download = Service(container, "AssetStoreDownloadManager", "m_SerializedAssetStoreDownloadManager");
            _installer = Service(container, "AssetStorePackageInstaller", null);
            _connect = Service(container, "UnityConnectProxy", "m_SerializedUnityConnectProxy");
            return _client != null && _cache != null && _download != null && _installer != null && _connect != null;
        }

        /// <summary>Finds a service in the container's registry by type name, falling back to its serialized field.</summary>
        private static object Service(object container, string typeName, string fieldName)
        {
            var registry = GetMember(container, "m_RegisteredServices") as IDictionary;
            if (registry != null)
            {
                foreach (object service in registry.Values)
                {
                    if (service != null && service.GetType().Name == typeName)
                        return service;
                }
            }

            return fieldName == null ? null : GetMember(container, fieldName);
        }

        private static Type FindType(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.GetName().Name.StartsWith("UnityEditor", StringComparison.Ordinal))
                    continue;
                Type type = assembly.GetType(_NAMESPACE + name, false);
                if (type != null)
                    return type;
            }
            return null;
        }

        /// <summary>Reads a field or property (public or not, declared on the type or a base type).</summary>
        private static object GetMember(object target, string name)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, _ALL | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field.GetValue(target);
                PropertyInfo property = type.GetProperty(name, _ALL | BindingFlags.DeclaredOnly);
                if (property != null && property.GetIndexParameters().Length == 0)
                    return property.GetValue(target, null);
            }
            throw new MissingMemberException(target.GetType().Name, name);
        }

        /// <summary>Calls a method by name and argument count (public or not, declared on the type or a base type).</summary>
        private static object Call(object target, string name, params object[] arguments)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                foreach (MethodInfo method in type.GetMethods(_ALL | BindingFlags.DeclaredOnly))
                {
                    if (method.Name == name && Accepts(method.GetParameters(), arguments))
                        return method.Invoke(target, arguments);
                }
            }
            throw new MissingMethodException(target.GetType().Name, name);
        }

        /// <summary>True when every argument can be passed to its parameter. Overloads with the same argument count (Download(long) and Download(List<long>)) are told apart by their types.</summary>
        private static bool Accepts(ParameterInfo[] parameters, object[] arguments)
        {
            if (parameters.Length != arguments.Length)
                return false;
            for (int i = 0; i < parameters.Length; i++)
            {
                Type type = parameters[i].ParameterType;
                if (arguments[i] == null ? type.IsValueType : !type.IsInstanceOfType(arguments[i]))
                    return false;
            }
            return true;
        }

        private static string Describe(Exception exception)
        {
            Exception inner = exception is TargetInvocationException && exception.InnerException != null ? exception.InnerException : exception;
            return inner.GetType().Name + ": " + inner.Message;
        }

        private static void Fail(Exception exception)
        {
            Fail("Reading My Assets failed (Unity's internal Package Manager API may have changed in this version): " + Describe(exception));
        }

        private static void Fail(string message)
        {
            IsLoading = false;
            IsLoaded = false;
            Status = message;
            RaiseChanged();
        }

        private static void RaiseChanged()
        {
            if (Changed != null)
                Changed();
        }
    }
}
