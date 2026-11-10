// Site header: loads components/navigation.html into #navigation-placeholder, adds the services' menu
// links and shows who is signed in. Also adds the site footer (Terms of Use, Privacy Policy) at the end of the page.
// On small screens the links and sign-in fold into a panel opened by the menu button (CSS decides when).

import { APP_CONFIG } from '../config.js';
import { getAuth, loginUrl } from '../auth/auth.js';
import { signOut } from '../auth/session.js';
import { query } from '../utils/dom.js';

export async function mountNavigation(): Promise<void> {
  const placeholder = document.getElementById('navigation-placeholder');
  if (!placeholder) return;
  mountFooter();
  placeholder.innerHTML = await (await fetch('/components/navigation.html')).text();

  // The menu: each service's pages, from app-config.json.
  const menu = query(placeholder, '.nav__menu');
  for (const { label, href, also } of APP_CONFIG.nav) {
    const link = Object.assign(document.createElement('a'), { href, className: 'nav__link', textContent: label });
    if (also?.length) link.dataset.also = also.join(' ');
    const item = document.createElement('li');
    item.append(link);
    menu.append(item);
  }

  // Mark the link for the current page (data-also: other pages in the same section).
  for (const link of placeholder.querySelectorAll<HTMLAnchorElement>('.nav__link')) {
    const pages = [link.pathname, ...(link.dataset.also?.split(' ') ?? [])];
    if (pages.includes(location.pathname)) link.setAttribute('aria-current', 'page');
  }
  mountMenuToggle(query(placeholder, '.nav'));

  const auth = await getAuth();
  const account = auth.account();
  const signIn = query<HTMLAnchorElement>(placeholder, '.nav__sign-in');
  const accountLink = query<HTMLAnchorElement>(placeholder, '.nav__account');
  const signOutBtn = query<HTMLButtonElement>(placeholder, '.nav__sign-out');

  if (account) {
    accountLink.textContent = account.name;
    accountLink.hidden = false;
    signOutBtn.hidden = false;
    signOutBtn.onclick = async () => { signOutBtn.disabled = true; await signOut(); };
  } else if (location.pathname !== '/login.html') {
    signIn.href = loginUrl();
    signIn.hidden = false;
  }
}

/** The footer on every page with the header: the company and the legal pages. */
function mountFooter(): void {
  if (document.querySelector('.site-footer')) return;
  const footer = document.createElement('footer');
  footer.className = 'site-footer';
  footer.innerHTML = `
    <span>© ${new Date().getFullYear()} FrontPack, Inc.</span>
    <a href="/terms.html">Terms of Use</a>
    <a href="/privacy.html">Privacy Policy</a>`;
  for (const link of footer.querySelectorAll('a')) if (link.pathname === location.pathname) link.setAttribute('aria-current', 'page');
  document.body.append(footer);
}

/** The menu button: opens and closes the panel; Escape, a click elsewhere or a wider screen closes it. */
function mountMenuToggle(nav: HTMLElement): void {
  const toggle = query<HTMLButtonElement>(nav, '.nav__toggle');
  const setOpen = (open: boolean) => {
    nav.classList.toggle('nav--open', open);
    toggle.setAttribute('aria-expanded', String(open));
    toggle.setAttribute('aria-label', open ? 'Close menu' : 'Menu');
  };
  const isOpen = () => nav.classList.contains('nav--open');

  toggle.onclick = () => setOpen(!isOpen());
  document.addEventListener('keydown', e => {
    if (e.key === 'Escape' && isOpen()) { setOpen(false); toggle.focus(); }
  });
  document.addEventListener('click', e => {
    if (isOpen() && !nav.contains(e.target as Node)) setOpen(false);
  });
  matchMedia('(max-width: 640px)').addEventListener('change', e => { if (!e.matches) setOpen(false); });
}
