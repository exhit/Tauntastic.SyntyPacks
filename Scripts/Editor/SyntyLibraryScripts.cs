namespace Tauntastic.SyntyPacks.Editor
{
    /// <summary>JavaScript run inside the Synty store pages (through the Tauntastic.WebBrowser tab) to read the library and a pack's files, using the page's own sign-in.</summary>
    internal static class SyntyLibraryScripts
    {
        /// <summary>True once the library page shows at least one pack (so also after the sign-in).</summary>
        public const string LibraryReady = "document.querySelectorAll('.sky-pilot-list-item').length > 0";

        /// <summary>True once a pack page shows at least one download link.</summary>
        public const string PackPageReady = "document.querySelectorAll('a.sky-pilot-button').length > 0";

        /// <summary>Every pack of every page of the library as a list of { name, href } (href is the pack page path).</summary>
        public const string ReadLibrary =
@"(async function () {
  var packs = [];
  for (var page = 1; page <= 50; page++) {
    var url = new URL(location.href);
    url.hash = '';
    url.searchParams.set('line_items_page', page);
    var html = await (await fetch(url.toString(), { credentials: 'include' })).text();
    var doc = new DOMParser().parseFromString(html, 'text/html');
    var found = Array.prototype.map.call(doc.querySelectorAll('a.sky-pilot-list-item'), function (a) {
      var heading = a.querySelector('.sky-pilot-file-heading, .sky-pilot-folder-heading');
      return { name: heading ? heading.textContent.trim() : '', href: new URL(a.getAttribute('href'), location.href).pathname };
    }).filter(function (p) { return p.name; });
    if (!found.length) break;
    packs = packs.concat(found);
    var hasNext = Array.prototype.some.call(doc.querySelectorAll('a'), function (a) { return /Next/.test(a.textContent); });
    if (!hasNext) break;
  }
  return packs;
})()";

        /// <summary>The files of the pack page that is open, as a list of { name, href } (href is the download link, relative to the page).</summary>
        public const string ReadPackFiles =
@"(function () {
  return Array.prototype.map.call(document.querySelectorAll('a.sky-pilot-button'), function (a) {
    var wrapper = a.closest('.sky-pilot-file-wrapper');
    var heading = wrapper && wrapper.querySelector('.sky-pilot-file-heading');
    return { name: (heading ? heading.textContent : '').replace(/\s+/g, ' ').trim(), href: a.getAttribute('href') };
  });
})()";
    }
}
