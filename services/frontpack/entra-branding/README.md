# Sign-in page branding (Entra External ID)

These files give the sign-in pages (on `slideshowmap.ciamlogin.com`) FrontPack's look:
- a photo of people cycling down a country road (from the home page), darkened
- the logo
- the site's amber buttons

They are uploaded by hand in the Entra admin center; nothing deploys them.

| File | Where it goes | Size (limit) |
|---|---|---|
| `favicon.png` | Basics → Favicon | 32×32 (5 KB) |
| `background.jpg` | Basics → Background image | 1920×1080 (300 KB) |
| `header-logo.png` | Header → Header logo | 90×36, within 245×36 (10 KB) |
| `banner-logo.png` | Sign-in form → Banner logo | 90×36, within 245×36 (50 KB) |
| `square-logo.png` | Sign-in form → Square logo (light theme) | 240×240 (50 KB) |
| `custom.css` | Layout → Custom CSS | (25 KB) |

The images are made from the website's own: `frontpack-logo.png`, `favicon.png` and `background2.jpg` (the riders on a
country road, cropped to 16:9). Remake them if those change.

## Upload (once; again after changing a file)

Sign in to the [Entra admin center](https://entra.microsoft.com) and switch to the **slideshowmap** tenant (the
External ID tenant: Settings icon → Directories + subscriptions).

1. **Tenant name:** Overview → **Properties** → Name: `FrontPack` → Save.
2. **Company branding** → Default sign-in → **Edit**.
   - **Basics:** Favicon `favicon.png`; Background image `background.jpg`; Page background colour `#14181d`
     (shown while the photo loads).
   - **Layout:** Template *Full-screen background*. Tick **Show header** and **Show footer**. Custom CSS
     `custom.css`.
   - **Header:** Logo `header-logo.png`.
   - **Footer:** optionally a link, e.g. text *FrontPack*, URL `https://www.thefrontpack.com`.
   - **Sign-in form:**
     - Banner logo `banner-logo.png`; Square logo (light theme) `square-logo.png`.
     - Username hint `Email address`.
     - Sign-in page text: `Sign in to FrontPack to see and share your albums.`
   - **Review + save.** Changes take a few minutes to show.
3. **Optional, wording:** External Identities → User flows → (the sign-up/sign-in flow) → **Languages** →
   English → override strings, e.g. the page title.

## Check

Open https://www.thefrontpack.com/login.html in a private window and click **Sign in**. You should see:
- the darkened photo
- the logo in the header and on the white box
- amber buttons with dark text
- the FrontPack favicon

If a style doesn't apply, compare the class names in `custom.css` with Microsoft's CSS template (Company
branding → Layout → *Download CSS template*). Entra ignores class names it doesn't use.
