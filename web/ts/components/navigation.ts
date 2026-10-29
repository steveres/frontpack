// Site header: loads components/navigation.html into #navigation-placeholder and shows who is signed in.
// On small screens the links and sign-in fold into a panel opened by the menu button (CSS decides when).

import { getAuth, loginUrl } from '../auth/auth.js';
import { signOut } from '../auth/session.js';
import { query } from '../utils/dom.js';

export async function mountNavigation(): Promise<void> {
  const placeholder = document.getElementById('navigation-placeholder');
  if (!placeholder) return;
  placeholder.innerHTML = await (await fetch('/components/navigation.html')).text();

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
