(() => {
  'use strict';
  if (window.powerForgeMediaViewer || !window.HTMLDialogElement?.prototype.showModal) return;
  const selector = document.currentScript?.dataset.pfMediaSelector || '[data-pf-media]';
  try { document.querySelector(selector); } catch { return; }
  window.powerForgeMediaViewer = true;
  const language = ["pl", "fr", "de", "es"].indexOf(document.documentElement.lang.toLowerCase().split("-")[0]);
  const translations = {"Image viewer": ["Przeglądarka obrazów", "Visionneuse d’images", "Bildbetrachter", "Visor de imágenes"], "Previous image": ["Poprzedni obraz", "Image précédente", "Vorheriges Bild", "Imagen anterior"], "Next image": ["Następny obraz", "Image suivante", "Nächstes Bild", "Imagen siguiente"], "Fit image to window": ["Dopasuj do okna", "Ajuster à la fenêtre", "An Fenster anpassen", "Ajustar a la ventana"], "Fit": ["Dopasuj", "Ajuster", "Einpassen", "Ajustar"], "Show image at actual size": ["Rozmiar rzeczywisty", "Taille réelle", "Originalgröße", "Tamaño real"], "Zoom out": ["Pomniejsz", "Réduire", "Verkleinern", "Alejar"], "Zoom in": ["Powiększ", "Agrandir", "Vergrößern", "Acercar"], "Image background": ["Tło obrazu", "Arrière-plan", "Bildhintergrund", "Fondo de imagen"], "Dark": ["Ciemne", "Sombre", "Dunkel", "Oscuro"], "Light": ["Jasne", "Clair", "Hell", "Claro"], "Transparency": ["Przezroczystość", "Transparence", "Transparenz", "Transparencia"], "Image variant": ["Wariant obrazu", "Variante", "Bildvariante", "Variante de imagen"], "Open original": ["Otwórz oryginał", "Ouvrir l’original", "Original öffnen", "Abrir original"], "Close image viewer": ["Zamknij przeglądarkę", "Fermer la visionneuse", "Bildbetrachter schließen", "Cerrar visor"], "Loading image…": ["Wczytywanie obrazu…", "Chargement de l’image…", "Bild wird geladen…", "Cargando imagen…"], "Original": ["Oryginał", "Original", "Original", "Original"], "Light appearance": ["Jasny wygląd", "Apparence claire", "Helle Darstellung", "Apariencia clara"], "Dark appearance": ["Ciemny wygląd", "Apparence sombre", "Dunkle Darstellung", "Apariencia oscura"], "Mobile": ["Telefon", "Mobile", "Mobil", "Móvil"], "Desktop": ["Komputer", "Ordinateur", "Desktop", "Escritorio"]};
  const translate = value => language < 0 ? value : translations[value]?.[language] || value;
  let dialog, stage, canvas, picture, caption, status, announcement, count, previous, next, original, variant;
  let items = [], index = 0, opener, scale = 1, fitMode = true, drag;
  const safeSource = value => {
    if (!value) return null;
    try {
      const url = new URL(value, document.baseURI);
      return /^(https?:|blob:)$/.test(url.protocol) ? url.href : null;
    } catch { return null; }
  };
  function source(item) {
    const image = item.querySelector('img');
    return safeSource(item.dataset.pfMediaSrc || item.getAttribute('href') || image?.currentSrc || image?.src);
  }
  function button(label, text, action) {
    const result = document.createElement('button');
    result.type = 'button'; result.textContent = translate(text); result.setAttribute('aria-label', translate(label));
    result.addEventListener('click', action); return result;
  }
  function create() {
    dialog = document.createElement('dialog'); dialog.className = 'pf-media-viewer';
    dialog.setAttribute('aria-label', translate('Image viewer'));
    const toolbar = document.createElement('div'); toolbar.className = 'pf-media-viewer__toolbar';
    previous = button(translate('Previous image'), '←', () => show(index - 1));
    next = button(translate('Next image'), '→', () => show(index + 1));
    count = document.createElement('span'); count.className = 'pf-media-viewer__count';
    const fit = button(translate('Fit image to window'), translate('Fit'), () => { fitMode = true; resize(); });
    const actual = button(translate('Show image at actual size'), '1:1', () => zoom(1));
    const minus = button(translate('Zoom out'), '−', () => zoom(scale / 1.4));
    const plus = button(translate('Zoom in'), '+', () => zoom(scale * 1.4));
    const background = document.createElement('select'); background.setAttribute('aria-label', translate('Image background'));
    for (const [value, label] of [['dark', translate('Dark')], ['light', translate('Light')], ['checkerboard', translate('Transparency')]]) {
      background.add(new Option(label, value));
    }
    background.addEventListener('change', () => { stage.dataset.background = background.value; });
    variant = document.createElement('select'); variant.setAttribute('aria-label', translate('Image variant'));
    variant.addEventListener('change', load);
    original = document.createElement('a'); original.textContent = translate('Open original');
    original.target = '_blank'; original.rel = 'noopener';
    const close = button(translate('Close image viewer'), '✕', () => dialog.close()); close.className = 'pf-media-viewer__close';
    toolbar.append(previous, count, next, minus, plus, fit, actual, background, variant, original, close);
    stage = document.createElement('div'); stage.className = 'pf-media-viewer__stage'; stage.tabIndex = 0;
    stage.setAttribute('aria-label', 'Image preview. Use zoom controls and scroll to explore.');
    canvas = document.createElement('div'); canvas.className = 'pf-media-viewer__canvas';
    picture = document.createElement('img'); picture.className = 'pf-media-viewer__image'; picture.draggable = false;
    picture.addEventListener('load', () => { picture.hidden = false; resize(); });
    picture.addEventListener('error', () => { picture.hidden = true; status.textContent = 'Image could not be loaded. Use Open original to try the source.'; });
    canvas.append(picture); stage.append(canvas);
    const footer = document.createElement('div'); footer.className = 'pf-media-viewer__footer';
    caption = document.createElement('p'); status = document.createElement('p');
    status.className = 'pf-media-viewer__status'; status.setAttribute('role', 'status');
    announcement = document.createElement('p'); announcement.className = 'pf-media-viewer__announcement';
    announcement.setAttribute('role', 'status'); announcement.setAttribute('aria-atomic', 'true');
    footer.append(caption, status, announcement); dialog.append(toolbar, stage, footer); document.body.append(dialog);
    dialog.addEventListener('click', event => { if (event.target === dialog) dialog.close(); });
    dialog.addEventListener('keydown', event => {
      if (event.target.closest('select, input, textarea') || event.ctrlKey || event.metaKey || event.altKey) return;
      if (event.key === 'ArrowLeft' && event.target !== stage) { event.preventDefault(); show(index - 1); }
      if (event.key === 'ArrowRight' && event.target !== stage) { event.preventDefault(); show(index + 1); }
      if (event.key === '+') { event.preventDefault(); zoom(scale * 1.4); }
      if (event.key === '-') { event.preventDefault(); zoom(scale / 1.4); }
    });
    dialog.addEventListener('close', () => {
      document.documentElement.classList.remove('pf-media-open');
      picture.removeAttribute('src'); picture.hidden = true; original.removeAttribute('href');
      items = []; drag = null; opener?.focus({ preventScroll: true });
    });
    stage.addEventListener('pointerdown', event => {
      if (event.pointerType !== 'mouse' || event.button !== 0) return;
      drag = { x: event.clientX, y: event.clientY, left: stage.scrollLeft, top: stage.scrollTop };
      stage.setPointerCapture(event.pointerId); event.preventDefault(); stage.focus({ preventScroll: true });
    });
    stage.addEventListener('pointermove', event => {
      if (drag) { stage.scrollLeft = drag.left + drag.x - event.clientX; stage.scrollTop = drag.top + drag.y - event.clientY; }
    });
    stage.addEventListener('lostpointercapture', () => { drag = null; });
    stage.addEventListener('pointerup', event => { drag = null; if (stage.hasPointerCapture(event.pointerId)) stage.releasePointerCapture(event.pointerId); });
    new ResizeObserver(() => { if (dialog.open && fitMode) resize(); }).observe(stage);
  }
  function resize() {
    if (!dialog.open || !picture.naturalWidth || picture.hidden) return;
    if (fitMode) scale = Math.min(1, stage.clientWidth / picture.naturalWidth, stage.clientHeight / picture.naturalHeight);
    picture.style.width = `${Math.max(1, picture.naturalWidth * scale)}px`;
    picture.style.height = `${Math.max(1, picture.naturalHeight * scale)}px`;
    status.textContent = `${index + 1} / ${items.length} · ${picture.naturalWidth} × ${picture.naturalHeight} · ${Math.round(scale * 100)}%`;
  }
  function zoom(value) {
    if (picture.hidden || !picture.naturalWidth) return;
    const x = (stage.scrollLeft + stage.clientWidth / 2) / Math.max(1, stage.scrollWidth);
    const y = (stage.scrollTop + stage.clientHeight / 2) / Math.max(1, stage.scrollHeight);
    fitMode = false; scale = Math.max(.05, Math.min(8, value)); resize();
    stage.scrollLeft = x * stage.scrollWidth - stage.clientWidth / 2;
    stage.scrollTop = y * stage.scrollHeight - stage.clientHeight / 2;
  }
  function load() {
    const item = items[index];
    const url = variant.value === 'original' ? source(item) : safeSource(item.dataset[variant.value]);
    if (!url) return;
    fitMode = true; picture.hidden = true; status.textContent = translate('Loading image…');
    picture.alt = item.querySelector('img')?.alt || item.getAttribute('aria-label') || '';
    announcement.textContent = `${index + 1} / ${items.length}. ${picture.alt}${caption.textContent !== picture.alt ? '. ' + caption.textContent : ''}${variant.options.length > 1 ? '. ' + variant.selectedOptions[0].textContent : ''}`;
    original.href = url; picture.src = url; stage.scrollTo(0, 0);
  }
  function show(requested) {
    if (!items.length) return;
    index = (requested + items.length) % items.length;
    count.textContent = `${index + 1} / ${items.length}`;
    previous.disabled = next.disabled = items.length < 2;
    const item = items[index];
    caption.textContent = item.dataset.pfMediaCaption || item.closest('figure')?.querySelector('figcaption')?.textContent || item.querySelector('img')?.alt || '';
    variant.replaceChildren(new Option(translate('Original'), 'original'));
    for (const [key, label] of [['pfMediaLight', translate('Light appearance')], ['pfMediaDark', translate('Dark appearance')], ['pfMediaMobile', translate('Mobile')], ['pfMediaDesktop', translate('Desktop')]]) {
      if (item.dataset[key] && safeSource(item.dataset[key])) variant.add(new Option(label, key));
    }
    variant.hidden = variant.options.length === 1; load();
  }
  document.addEventListener('click', event => {
    if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    const item = event.target.closest?.(selector);
    if (!item || !item.querySelector('img') || item.dataset.pfMedia === 'off' || item.hasAttribute('download') || !source(item)) return;
    const group = item.dataset.pfMediaGroup || item.closest('[data-pf-media-group]')?.dataset.pfMediaGroup;
    items = group ? Array.from(document.querySelectorAll(selector)).filter(candidate =>
      (candidate.dataset.pfMediaGroup || candidate.closest('[data-pf-media-group]')?.dataset.pfMediaGroup) === group &&
      candidate.dataset.pfMedia !== 'off' && !candidate.hasAttribute('download') && candidate.querySelector('img') && source(candidate)) : [item];
    event.preventDefault(); opener = item;
    if (!dialog) create();
    document.documentElement.classList.add('pf-media-open');
    dialog.showModal(); show(items.indexOf(item));
    dialog.querySelector('.pf-media-viewer__close').focus();
  });
})();
