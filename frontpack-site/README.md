# FrontPack website (being merged in)

The pages of www.thefrontpack.com (Home and About), imported from the `frontpack-website` repository
with their history (`git log -- frontpack-site`). They are kept here as the source to port into the
single-page app in [web/](../web/), after which this folder is removed.

Not included: the Routes feature (route list, detail and editor) and FrontPack's own email/password
sign-in (login, register, verify email). Sign-in moves to Entra External ID for the whole site; Routes
will come back later from the archived `frontpack-website` repository. Also left out: the local HTTPS
dev server and its certificate config, which the old deployment published along with the pages.

This folder is not built or deployed. The live FrontPack site still runs from the old repository's
deployment until DNS points www.thefrontpack.com at the new site.
