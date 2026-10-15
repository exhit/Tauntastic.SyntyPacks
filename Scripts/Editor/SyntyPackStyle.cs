using UnityEngine;

namespace Tauntastic.SyntyPacks.Editor
{
    /// <summary>Colors and flag texts used to show the state of a pack in the Packs window; the same palette as the Tauntastic Packages window (one color and one flag per state).</summary>
    public static class SyntyPackStyle
    {
        public static readonly Color Unknown = new Color(0.45f, 0.45f, 0.45f);
        public static readonly Color UpToDate = new Color(0.20f, 0.55f, 0.30f);
        public static readonly Color UpdateAvailable = new Color(0.90f, 0.55f, 0.10f);
        public static readonly Color Installable = new Color(0.10f, 0.58f, 0.65f);
        public static readonly Color Category = new Color(0.35f, 0.35f, 0.35f);
        public static readonly Color InProject = new Color(0.17f, 0.60f, 0.45f);
        public static readonly Color OwnInAssetStore = new Color(0.25f, 0.45f, 0.75f);
        public static readonly Color OwnInSyntyStore = new Color(0.60f, 0.28f, 0.70f);
        public static readonly Color NotOwned = new Color(0.75f, 0.22f, 0.22f);

        public const string UpdateAvailableFlag = "UPDATE AVAILABLE";
        public const string UpToDateFlag = "UP TO DATE";
        public const string InstallableFlag = "INSTALLABLE";
        public const string InProjectFlag = "IN PROJECT";
        public const string OwnInAssetStoreFlag = "OWN IN ASSET STORE";
        public const string OwnInSyntyStoreFlag = "OWN IN SYNTY STORE";
        public const string NotOwnedFlag = "NOT OWNED";
        public const string OwnUnknownFlag = "OWN UNKNOWN";
    }
}
