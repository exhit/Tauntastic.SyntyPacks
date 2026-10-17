# Tauntastic.SyntyPacks

Lists the Synty Studios asset packs, shows which ones are in your project, and flags packs whose Unity version was bumped after you imported them.

- Package: `com.tauntastic.syntypacks` (v1.1.0, Unity 2023.2+)
- Assembly / namespace: `Tauntastic.SyntyPacks.Editor` (Editor-only)
- Dependencies: `com.tauntastic.webbrowser` (the browser tab used for the Synty store sign-in, library import and downloads)
- Window: **Tools > Tauntastic > Synty Packs**; preferences: **Preferences > Tools > Tauntastic > Synty Packs**
- AI agents: see [ARTIFICIAL_INTELLIGENCE_README.md](ARTIFICIAL_INTELLIGENCE_README.md)

This package was called `com.syntystudios.packages` before it moved into the Tauntastic packages. Saved preferences (`SyntyStudios.Packages.*` in the Editor preferences), the library file and the caches keep their old names and locations on purpose, so nothing you set up is lost.

## How it works

1. **Catalog.** It downloads the public product list of `syntystore.com/products.json` (no login), keeps the Unity packs and reads the highest `Version x.y.z (Unity)` from each product's change log. The list is cached in `Library/SyntyStudiosPackages.catalog` and refreshed at most every 24 hours (preference).
2. **Imported in the project.** A pack counts as imported when a folder under `Assets` (two levels deep) has the same name as the pack, ignoring case, spaces and punctuation: `Assets/PolygonBattleRoyale` matches "POLYGON - Battle Royale". Synty packs carry no version file, so the imported version is unknown until you press **Mark x.y.z as imported**; from then on a newer store version shows **Update available**. The mark is stored per project in EditorPrefs.
3. **Downloaded.** `.unitypackage` files of publisher folders containing "Synty" in Unity's Asset Store cache (`%APPDATA%/Unity/Asset Store-5.x`) are listed as downloaded, with a button to show the file.
4. **Getting a pack.** **Synty Store**, **Unity Store** and **My downloads** open the browser; for packs in your Asset Store purchases the window can also download and import them itself (see below).

## Categories

The second toolbar row has one toggle per store category (Animation, Polygon, Polygon Mini, Simple Series, Sidekick Pack, Interface, ...), **All** and **None** (every category off, which lists nothing). Only **Polygon** is on by default; the choice is remembered per machine. The list, the summary line, the title count and the search all respect it; Refresh and Rescan project work as before.

## Ownership toolbar

A third toolbar row filters by ownership: **All | Unity Store | Synty Store | Not Owned** (toggles like the categories, all on by default and remembered; **None** switches every button off, which lists nothing). With all three on nothing is filtered, including packs whose ownership is unknown; otherwise only packs owned in the Unity Asset Store, owned in the Synty Store (downloaded) or Not Owned are listed. An info box above the list counts the Synty packs you own per store, for the whole catalog.

## Rows, flags and installing

Click a pack name to fold or unfold its row; **Collapse all** / **Expand all** in the toolbar fold them to single lines that show the pack name and its flags: **Update**, **In Project**, and an ownership flag (**Own In Asset Store**, **Own In Synty Store**, **Not Owned** or **Own Unknown**). Ownership is detected, never typed in:

- **Own In Asset Store:** the pack is in your **My Assets** list. When the window opens (and on **Reload purchases**) it reads your whole My Assets list (100 purchases per request, so only a few requests) by calling the Package Manager's own internal services through reflection with the Unity account you are signed in with. Each Synty store pack name is then compared with the purchase names locally; a purchase counts when it has the same identifying words as the store name, ignoring case, punctuation, plurals and the filler words Pack, Free and "Art by Synty", so a changed publisher suffix does not matter. A pack in the Asset Store download cache counts too. Every finished scan is saved in `Library/SyntyStudiosPackages.myassets`; when the window opens and the saved scan is younger than **Hours between refreshes** (Preferences, default 24) it shows those results at once and sends no request. An older scan is replaced by a new one, and **Reload purchases** always scans.
- **Own In Synty Store:** the pack is in your Synty store library file (`SyntyStudios/SyntyStoreLibrary.json` in your user folder by default, a JSON list `{"exportedUtc": "...", "packs": ["POLYGON - Farm Pack", ...]}` of the names on your store "Your Library" page, matched to the catalog by name; set the path in Preferences), or a `.unitypackage` / `.zip` of it is in the Synty downloads folder (default: your Downloads folder, top level only, matched by file name). The store page is behind a login that Unity cannot use, so the library file is exported from that page while you are signed in and has to be exported again after buying a pack.
- **Not Owned:** My Assets was read and the pack is in neither place. A Synty store purchase you never downloaded also shows this.
- **Own Unknown:** My Assets has not been read (not signed in, or the reflection failed; the message above the list says which).

Every row has five buttons: **Synty Store** and **Unity Store** open the pack's pages in the browser; **Download** downloads an Asset Store purchase into the Asset Store cache through Package Manager's download manager (with progress) without importing it; **Import** imports an already downloaded `.unitypackage` (opens Unity's import window); **Download & Import** does both. Download and Download & Import need the pack in your My Assets list, Import needs a downloaded package; buttons that do not apply are greyed out.

## Importing your Synty store library

The Synty store has no public list of your purchases, so the packs you own there come from a small file (see **Own In Synty Store** above). The **Import Synty library** button fills it with the help of the [Tauntastic.WebBrowser](https://github.com/exhit/Tauntastic.Webbrowser) package, which this package depends on:

1. Press **Import Synty library**. Your Synty store library opens in a **browser tab inside Unity** (a hidden Chrome, Edge or Brave renders it into the window; one of them must be installed).
2. If you are not signed in, sign in in that tab (use the e-mail code option; pop-up windows, such as "Sign in with Shop", are not shown). The sign-in is kept in the `SyntyStore` browser profile, so later imports need no sign-in and finish by themselves.
3. As soon as the library shows, the pack names of all its pages are read and saved to the library file, the tab closes (if the import opened it), and the Packs window updates its flags. **Stop import** cancels while it waits.

The page is read inside the browser with your own sign-in; Unity never sees your password or cookies, and the browser profile is separate from your everyday browser. Re-import after buying a pack. If Synty changes their library page the import finds no packs and says so.

## Downloading and importing a pack

The buttons depend on where you own the pack:

- **Owned in the Unity Asset Store** (flag **Own In Asset Store**): **Download**, **Import** and **Download & Import** work through the Package Manager's own services, as before.
- **Owned in the Synty store** (flag **Own In Synty Store**): **Download & Import (Synty Store)** opens your Synty store library in the Unity browser tab (the `SyntyStore` profile, so you sign in only once), finds the pack, opens its page, takes the **Unity file** among the files listed there (Source files, Unreal, Godot and Unity are listed side by side; the one for your Unity version is chosen, for example `POLYGON_FarmUnity_2022_3`), lets the browser download it, and opens Unity's import window. Progress shows in the row and **Stop** cancels. The file is kept in `%LOCALAPPDATA%/SyntyStudios/Downloads`; a download that turns out to be a `.zip` is unpacked and its `.unitypackage` imported, anything else is shown in the file manager. One download runs at a time.

A pack you own in both stores shows both groups of buttons.

## Which version you have

The package always knows which version of a pack it downloaded and records it as the installed version by itself, so the **Mark x.y.z as imported** button is only needed for packs you imported some other way:

- **Synty store download:** the version is read from the file's entry on the pack page (`POLYGON_FarmUnity_2022_3 | v1_7_3` is version 1.7.3).
- **Asset Store download:** the version is the one the Package Manager recorded for the downloaded package.
- **Import button on an already downloaded package:** the version is taken the same way, from the Asset Store cache or from a version in the file name (`..._v1_7_3`).

The version is recorded when Unity reports the import as finished, not when the download ends, so cancelling the import window records nothing. The row then shows **Installed** with that version, flags **Update** as soon as the store lists a newer Unity version, and its details show the version of the downloaded file. Importing a pack makes Unity reload scripts; a waiting import is kept for that (in the Editor session state, for at most 6 hours) and resolved afterwards.

## The window follows the project

The Packs window rescans by itself when files in the project are imported, deleted or moved (half a second after the last change, so an import of thousands of assets causes one rescan), so a pack you imported or deleted by hand shows up or disappears without pressing **Rescan project**. When a pack's folder disappears this way, its recorded installed version is forgotten, so a later import shows the right version.

A pack counts as imported when a folder under `Assets` (two levels deep) is named like it, ignoring case and punctuation. Synty drops some words from folder names, so the match also accepts `PolygonSnow` for "POLYGON - Snow Kit" (a trailing "Kit") and ignores a trailing "| FREE" and "Pack" of the title.

## Remembered view

The Packs window remembers its state between sessions (per machine, in the Editor preferences): the category and ownership buttons, which rows are collapsed, the search text, **Show installable** and the scroll position.

## Limits

- The My Assets and download features use Unity's **internal** Package Manager API (`UnityEditor.PackageManager.UI.Internal`), which is not public and can change in any Unity version. Every step is guarded: on failure the window shows the reason and the package keeps working from local files. It was written against the Unity 6000.3 editor.
- Reading My Assets sends a purchases query to the Unity servers and reuses the Package Manager's list state, so an open My Assets tab may refresh.
- Purchases are matched to the store name by words (see above); a pack whose Asset Store title uses different words than its Synty store title is not found.
- Matching installed packs is by folder name; a renamed folder is not found.
- The latest version is only as good as the store change log text; packs without a `(Unity)` entry show "unknown".
