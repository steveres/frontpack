// index.html: the FrontPack home page, over cycling photos that cross-fade every few seconds.

import { mountNavigation } from '../components/navigation.js';

const BACKGROUNDS = ['/images/background1.jpg', '/images/background2.jpg', '/images/background3.jpg'];
const BACKGROUND_MS = 5000;

void mountNavigation();

// Two layers: the next photo goes into the hidden one, which then fades in over the other.
let [shown, hidden] = document.querySelectorAll<HTMLElement>('.home__background');
let index = 0;
shown.style.backgroundImage = `url(${BACKGROUNDS[0]})`;
shown.classList.add('home__background--shown');

if (!matchMedia('(prefers-reduced-motion: reduce)').matches) {
  for (const src of BACKGROUNDS.slice(1)) new Image().src = src; // loaded before their turn
  setInterval(() => {
    index = (index + 1) % BACKGROUNDS.length;
    hidden.style.backgroundImage = `url(${BACKGROUNDS[index]})`;
    hidden.classList.add('home__background--shown');
    shown.classList.remove('home__background--shown');
    [shown, hidden] = [hidden, shown];
  }, BACKGROUND_MS);
}
