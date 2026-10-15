using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace Tauntastic.SyntyPacks.Editor
{
    /// <summary>
    /// Knows which version of a pack was imported. Whenever this package downloads a pack (Synty store or Asset Store) it says which version that file is with <see cref="Expect"/>;
    /// when Unity then reports the import as finished, that version is recorded as the installed version of the pack in this project, so no "Mark as imported" is needed.
    /// A cancelled or failed import records nothing. The waiting imports are kept in the session state, because importing a pack makes Unity reload the scripts.
    /// </summary>
    [InitializeOnLoad]
    public static class SyntyImportTracker
    {
        private const string _SESSION_KEY = "SyntyStudios.Packages.PendingImports";
        private static readonly TimeSpan _maxAge = TimeSpan.FromHours(6);

        /// <summary>Raised on the main thread after a version was recorded.</summary>
        public static event Action Recorded;

        static SyntyImportTracker()
        {
            AssetDatabase.importPackageStarted += OnStarted;
            AssetDatabase.importPackageCompleted += OnCompleted;
            AssetDatabase.importPackageCancelled += OnEnded;
            AssetDatabase.importPackageFailed += (name, error) => OnEnded(name);

            // Scripts reload after an import that adds code; an import that had started and now leaves a reload behind is finished.
            EditorApplication.delayCall += AfterReload;
        }

        /// <summary>
        /// Announces an import of <paramref name="packagePath"/> that is about to start (call it right before AssetDatabase.ImportPackage). Without a handle or a version
        /// there is nothing to record and nothing is tracked.
        /// </summary>
        public static void Expect(string packagePath, string handle, string version)
        {
            if (string.IsNullOrEmpty(packagePath) || string.IsNullOrEmpty(handle) || string.IsNullOrEmpty(version))
                return;

            List<SyntyPendingImport> pending = Load();
            pending.RemoveAll(item => item.Handle == handle);
            pending.Add(new SyntyPendingImport
            {
                PackageName = Path.GetFileNameWithoutExtension(packagePath),
                Handle = handle,
                Version = version,
                Ticks = DateTime.UtcNow.Ticks
            });
            Save(pending);
        }

        private static void OnStarted(string packageName)
        {
            List<SyntyPendingImport> pending = Load();
            var unstarted = pending.FindAll(item => !item.Started);
            SyntyPendingImport match = SyntyPendingImports.Find(unstarted, packageName, false);
            if (match == null)
                return;

            match.Started = true;
            Save(pending);
        }

        private static void OnCompleted(string packageName)
        {
            List<SyntyPendingImport> pending = Load();
            SyntyPendingImport match = SyntyPendingImports.Find(pending, packageName, true);
            if (match == null)
                return;

            pending.Remove(match);
            Save(pending);
            Record(match);
        }

        private static void OnEnded(string packageName)
        {
            List<SyntyPendingImport> pending = Load();
            SyntyPendingImport match = SyntyPendingImports.Find(pending, packageName, false);
            if (match == null)
                return;

            pending.Remove(match);
            Save(pending);
        }

        private static void AfterReload()
        {
            List<SyntyPendingImport> pending = Load();
            bool any = false;
            foreach (SyntyPendingImport item in pending.FindAll(entry => entry.Started))
            {
                pending.Remove(item);
                Record(item);
                any = true;
            }

            if (any)
                Save(pending);
        }

        private static void Record(SyntyPendingImport item)
        {
            SyntySettings.SetRecordedVersion(item.Handle, item.Version);
            Action handler = Recorded;
            if (handler != null)
                handler();
        }

        private static List<SyntyPendingImport> Load()
        {
            return SyntyPendingImports.Parse(SessionState.GetString(_SESSION_KEY, string.Empty), DateTime.UtcNow.Ticks, _maxAge);
        }

        private static void Save(List<SyntyPendingImport> pending)
        {
            SessionState.SetString(_SESSION_KEY, SyntyPendingImports.Serialize(pending));
        }
    }
}
