using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Tauntastic.SyntyPacks.Editor
{
    /// <summary>Where the user owns a pack, detected from the Asset Store My Assets list and from downloads; Unknown until My Assets was read.</summary>
    public enum SyntyOwnership
    {
        Unknown,
        NotOwned,
        AssetStore,
        SyntyStore,
        Both
    }

    /// <summary>One Synty asset pack as listed in the public Synty store catalog.</summary>
    public sealed class SyntyProduct
    {
        /// <summary>Words that carry no pack identity: "Pack", "FREE" and the publisher credit that the Asset Store adds ("- Art by Synty").</summary>
        private static readonly HashSet<string> _fillerWords = new HashSet<string> { "pack", "free", "art", "by", "synty", "studios" };

        public string Handle;
        public string Title;
        public string ProductType;

        /// <summary>Highest "Version x.y.z (Unity)" found in the product's change log, or an empty string.</summary>
        public string LatestUnityVersion;

        public string Url { get { return SyntySettings.StoreBaseUrl + "/products/" + Handle; } }

        /// <summary>Lower-case letters and digits of the title (or folder name) without a trailing "pack", so "POLYGON - Prototype Pack" matches the folder PolygonPrototype.</summary>
        public string MatchKey { get { return MakeKey(Title); } }

        /// <summary>The title as typed into the My Assets search: without a "| FREE" style tail.</summary>
        public string SearchText
        {
            get
            {
                int bar = Title.IndexOf('|');
                return (bar > 0 ? Title.Substring(0, bar) : Title).Trim();
            }
        }

        public static string MakeKey(string text)
        {
            var builder = new StringBuilder();
            foreach (char c in text ?? string.Empty)
            {
                if (char.IsLetterOrDigit(c))
                    builder.Append(char.ToLowerInvariant(c));
            }

            string key = builder.ToString();

            // "POLYGON - Starter Pack | FREE" has the folder PolygonStarter: the FREE tail is not part of the name.
            if (key.Length > 8 && key.EndsWith("free"))
                key = key.Substring(0, key.Length - 4);

            if (key.Length > 4 && key.EndsWith("pack"))
                key = key.Substring(0, key.Length - 4);
            return key;
        }

        /// <summary>
        /// The value for this product in a dictionary keyed like <see cref="MakeKey"/> (project folders by folder name, for example). The exact key wins; otherwise a trailing "kit"
        /// is dropped, because Synty names the folder of "POLYGON - Snow Kit" PolygonSnow. Returns null when nothing matches.
        /// </summary>
        public string FindIn(IDictionary<string, string> keyed)
        {
            string key = MatchKey;
            string value;
            if (keyed.TryGetValue(key, out value))
                return value;

            if (key.Length > 7 && key.EndsWith("kit") && keyed.TryGetValue(key.Substring(0, key.Length - 3), out value))
                return value;
            return null;
        }

        /// <summary>
        /// True when two pack names have the same identifying words, ignoring case, punctuation, plurals and the filler words (Pack, Free, Art by Synty).
        /// "POLYGON - Western Pack" equals "POLYGON - Western Pack - Art by Synty" but not "POLYGON - Western Frontier Pack".
        /// </summary>
        public static bool SameName(string a, string b, bool allowPlural = true)
        {
            List<string> first = Words(a);
            List<string> second = Words(b);
            if (first.Count == 0 || second.Count == 0)
                return false;
            return first.All(x => second.Any(y => SameWord(x, y, allowPlural))) && second.All(y => first.Any(x => SameWord(x, y, allowPlural)));
        }

        private static bool SameWord(string x, string y, bool allowPlural)
        {
            return x == y || (allowPlural && (x + "s" == y || y + "s" == x));
        }

        private static List<string> Words(string text)
        {
            var words = new List<string>();
            var current = new StringBuilder();
            foreach (char c in (text ?? string.Empty) + " ")
            {
                if (char.IsLetterOrDigit(c))
                {
                    current.Append(char.ToLowerInvariant(c));
                    continue;
                }

                if (current.Length > 0 && !_fillerWords.Contains(current.ToString()))
                    words.Add(current.ToString());
                current.Length = 0;
            }
            return words;
        }
    }
}
