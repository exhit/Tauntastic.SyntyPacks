using System;
using System.Collections.Generic;
using System.Text;

namespace Tauntastic.SyntyPacks.Editor
{
    /// <summary>An import started from this package: when Unity reports the package as imported, <see cref="Version"/> is recorded as the installed version of the pack.</summary>
    public sealed class SyntyPendingImport
    {
        /// <summary>Name of the .unitypackage without extension, as Unity reports it in the import events.</summary>
        public string PackageName;

        /// <summary>Synty store handle of the pack.</summary>
        public string Handle;

        /// <summary>The version of the package that is being imported, for example "1.7.3".</summary>
        public string Version;

        /// <summary>True once Unity started importing it (the import window was confirmed).</summary>
        public bool Started;

        /// <summary>When the import was announced (UTC ticks); old entries are dropped.</summary>
        public long Ticks;
    }

    /// <summary>The list of pending imports and how it is saved: as text, so it survives the script reload that follows an import.</summary>
    public static class SyntyPendingImports
    {
        /// <summary>One line per import: package name, handle, version, started (0 or 1) and ticks, separated by tabs.</summary>
        public static string Serialize(IEnumerable<SyntyPendingImport> imports)
        {
            var builder = new StringBuilder();
            foreach (SyntyPendingImport item in imports)
                builder.Append(item.PackageName).Append('\t').Append(item.Handle).Append('\t').Append(item.Version).Append('\t').Append(item.Started ? '1' : '0').Append('\t').Append(item.Ticks).Append('\n');
            return builder.ToString();
        }

        /// <summary>Reads the text of <see cref="Serialize"/>; broken lines and entries older than <paramref name="maxAge"/> are skipped.</summary>
        public static List<SyntyPendingImport> Parse(string text, long nowTicks, TimeSpan maxAge)
        {
            var list = new List<SyntyPendingImport>();
            foreach (string line in (text ?? string.Empty).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = line.Split('\t');
                long ticks;
                if (fields.Length != 5 || !long.TryParse(fields[4], out ticks) || nowTicks - ticks > maxAge.Ticks)
                    continue;

                list.Add(new SyntyPendingImport { PackageName = fields[0], Handle = fields[1], Version = fields[2], Started = fields[3] == "1", Ticks = ticks });
            }
            return list;
        }

        /// <summary>
        /// The pending import of a package name reported by Unity. Names are compared ignoring case; when Unity reports a name that matches nothing (it may add or drop
        /// parts of the file name) and exactly one import is waiting, that one is meant. <paramref name="startedOnly"/> limits the fallback to imports that have started.
        /// </summary>
        public static SyntyPendingImport Find(List<SyntyPendingImport> imports, string packageName, bool startedOnly)
        {
            foreach (SyntyPendingImport item in imports)
            {
                if (string.Equals(item.PackageName, packageName, StringComparison.OrdinalIgnoreCase))
                    return item;
            }

            SyntyPendingImport only = null;
            foreach (SyntyPendingImport item in imports)
            {
                if (startedOnly && !item.Started)
                    continue;
                if (only != null)
                    return null;
                only = item;
            }
            return only;
        }
    }
}
