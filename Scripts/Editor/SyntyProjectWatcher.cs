using System;
using UnityEditor;

namespace Tauntastic.SyntyPacks.Editor
{
    /// <summary>
    /// Tells the Packs window when files in the project were imported, deleted or moved (a pack imported, a pack folder deleted by hand), so the flags follow the project without
    /// pressing Rescan. Bursts of changes, such as the thousands of assets of an import, are merged into one notification half a second after the last one.
    /// </summary>
    public class SyntyProjectWatcher : AssetPostprocessor
    {
        private const double _DELAY_SECONDS = 0.5;
        private static double _fireAt;
        private static bool _waiting;

        /// <summary>Raised on the main thread after the project's files changed.</summary>
        public static event Action ProjectChanged;

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Length == 0 && deleted.Length == 0 && moved.Length == 0)
                return;

            _fireAt = EditorApplication.timeSinceStartup + _DELAY_SECONDS;
            if (_waiting)
                return;

            _waiting = true;
            EditorApplication.update += Wait;
        }

        private static void Wait()
        {
            if (EditorApplication.timeSinceStartup < _fireAt)
                return;

            EditorApplication.update -= Wait;
            _waiting = false;
            Action handler = ProjectChanged;
            if (handler != null)
                handler();
        }
    }
}
