// A music library track's credit, as Pixabay suggests it: "Music by <artist> from <Pixabay>", the artist linking
// to their page and the site to the track's own page. Used by the slideshow and the Music credits page.

/** What's needed to credit a track. */
export interface TrackCredit { title: string; artist: string; artistUrl: string | null; sourceUrl: string | null; }

function link(text: string, href: string | null): Node {
  if (!href) return document.createTextNode(text);
  return Object.assign(document.createElement('a'), { textContent: text, href, target: '_blank', rel: 'noopener' });
}

/** "pixabay.com" → "Pixabay"; null when there's no source. */
export function sourceName(url: string | null): string | null {
  if (!url) return null;
  try {
    const host = new URL(url).hostname.replace(/^www\./, '');
    const name = host.split('.').slice(-2, -1)[0] ?? host;
    return name.charAt(0).toUpperCase() + name.slice(1);
  } catch { return null; }
}

/** "Music by dimmysad from Pixabay" (links included), without the title. */
export function musicBy(c: TrackCredit): DocumentFragment {
  const f = document.createDocumentFragment();
  f.append('Music by ', link(c.artist, c.artistUrl));
  const site = sourceName(c.sourceUrl);
  if (site) f.append(' from ', link(site, c.sourceUrl));
  return f;
}
