// about.html: the FrontPack story. Each chapter rises into view the first time it is scrolled to.

import { mountNavigation } from '../components/navigation.js';

void mountNavigation();

if (!matchMedia('(prefers-reduced-motion: reduce)').matches) {
  const observer = new IntersectionObserver(entries => {
    for (const entry of entries) {
      if (!entry.isIntersecting) continue;
      entry.target.classList.remove('about__chapter--waiting');
      observer.unobserve(entry.target);
    }
  }, { threshold: 0.2 });
  for (const chapter of document.querySelectorAll('.about__chapter')) {
    chapter.classList.add('about__chapter--waiting');
    observer.observe(chapter);
  }
}
