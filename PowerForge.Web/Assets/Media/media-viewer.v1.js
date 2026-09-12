(() => {
  'use strict';
  if (window.powerForgeMediaViewer || !window.HTMLDialogElement?.prototype.showModal) return;
  const selector = (document.currentScript?.dataset.pfMediaSelector || '[data-pf-media]') + ',[data-pf-media-context]';
  try { document.querySelector(selector); } catch { return; }
  const styles = document.querySelector('link[data-pf-media-styles]');
  if (styles) styles.rel = 'stylesheet';
  window.powerForgeMediaViewer = true;
  const language = ["pl", "fr", "de", "es"].indexOf(document.documentElement.lang.toLowerCase().split("-")[0]);
  const translations = {"Image viewer": ["Przeglądarka obrazów", "Visionneuse d’images", "Bildbetrachter", "Visor de imágenes"], "Previous image": ["Poprzedni obraz", "Image précédente", "Vorheriges Bild", "Imagen anterior"], "Next image": ["Następny obraz", "Image suivante", "Nächstes Bild", "Imagen siguiente"], "Fit image to window": ["Dopasuj do okna", "Ajuster à la fenêtre", "An Fenster anpassen", "Ajustar a la ventana"], "Fit": ["Dopasuj", "Ajuster", "Einpassen", "Ajustar"], "Show image at actual size": ["Rozmiar rzeczywisty", "Taille réelle", "Originalgröße", "Tamaño real"], "Zoom out": ["Pomniejsz", "Réduire", "Verkleinern", "Alejar"], "Zoom in": ["Powiększ", "Agrandir", "Vergrößern", "Acercar"], "Image background": ["Tło obrazu", "Arrière-plan", "Bildhintergrund", "Fondo de imagen"], "Dark": ["Ciemne", "Sombre", "Dunkel", "Oscuro"], "Light": ["Jasne", "Clair", "Hell", "Claro"], "Transparency": ["Przezroczystość", "Transparence", "Transparenz", "Transparencia"], "Image variant": ["Wariant obrazu", "Variante", "Bildvariante", "Variante de imagen"], "Open original": ["Otwórz oryginał", "Ouvrir l’original", "Original öffnen", "Abrir original"], "Close image viewer": ["Zamknij przeglądarkę", "Fermer la visionneuse", "Bildbetrachter schließen", "Cerrar visor"], "Loading image…": ["Wczytywanie obrazu…", "Chargement de l’image…", "Bild wird geladen…", "Cargando imagen…"], "Original": ["Oryginał", "Original", "Original", "Original"], "Light appearance": ["Jasny wygląd", "Apparence claire", "Helle Darstellung", "Apariencia clara"], "Dark appearance": ["Ciemny wygląd", "Apparence sombre", "Dunkle Darstellung", "Apariencia oscura"], "Mobile": ["Telefon", "Mobile", "Mobil", "Móvil"], "Desktop": ["Komputer", "Ordinateur", "Desktop", "Escritorio"]};
  const translate = value => language < 0 ? value : translations[value]?.[language] || value;
  translations['Image preview. Use zoom controls and scroll to explore.'] = ['Podgląd obrazu. Użyj powiększenia i przewijania, aby obejrzeć szczegóły.', 'Aperçu de l’image. Utilisez le zoom et le défilement pour explorer.', 'Bildvorschau. Verwenden Sie Zoom und Bildlauf zum Erkunden.', 'Vista previa de la imagen. Utiliza el zoom y el desplazamiento para explorar.'];
  translations['Image could not be loaded. Use Open original to try the source.'] = ['Nie udało się wczytać obrazu. Wybierz Otwórz oryginał, aby sprawdzić źródło.', 'Impossible de charger l’image. Sélectionnez Ouvrir l’original pour essayer la source.', 'Das Bild konnte nicht geladen werden. Wählen Sie Original öffnen, um die Quelle aufzurufen.', 'No se pudo cargar la imagen. Selecciona Abrir original para acceder a la fuente.'];
  translations['Back to page'] = ['Wróć do strony', 'Retour à la page', 'Zurück zur Seite', 'Volver a la página'];
  translations['Image preview'] = ['Podgląd obrazu', 'Aperçu de l’image', 'Bildvorschau', 'Vista previa'];
  translations['Image gallery'] = ['Galeria obrazów', 'Galerie d’images', 'Bildergalerie', 'Galería de imágenes'];
  translations['Display settings'] = ['Ustawienia wyświetlania', 'Options d’affichage', 'Anzeigeoptionen', 'Opciones de visualización'];
  translations['Show in page'] = ['Pokaż na stronie', 'Voir dans la page', 'Auf der Seite zeigen', 'Ver en la página'];
  let title, navigation, settings, variantField, contextButton, returnTarget;
  let dialog, stage, canvas, picture, caption, status, announcement, errorMessage, count, previous, next, original, variant, thumbnails, zoomValue, fitButton, actualButton;
  let items = [], index = 0, opener, scale = 1, fitMode = true, drag;
  const safeSource = value => {
    if (!value) return null;
    try {
      const url = new URL(value, document.baseURI);
      return url.protocol === 'https:' || (url.protocol === 'http:' && location.protocol === 'http:' && url.origin === location.origin) ? url.href : null;
    } catch { return null; }
  };
  function source(item) {
    const image = imageFor(item);
    return safeSource(item.dataset.pfMediaSrc || item.getAttribute('href') || image?.currentSrc || image?.src);
  }
  function eligible(item) {
    return item && !dialog?.contains(item) && imageFor(item) && item.dataset.pfMedia !== 'off' && !item.hasAttribute('download') && source(item);
  }
  function imageFor(item) {
    const image = item.querySelector('img') || document.getElementById(item.dataset.pfMediaFor || '');
    return image instanceof HTMLImageElement ? image : null;
  }
  const groupFor = item => item.closest('[data-pf-media-group]')?.dataset.pfMediaGroup;
  const visible = item => !item.closest('[hidden], [inert], [aria-hidden="true"]') && item.getClientRects().length > 0 && getComputedStyle(item).visibility === 'visible';
  function refreshGroupLinks(root = document) {
    const links = root.querySelectorAll('[data-pf-media-open-group]');
    if (!links.length) return;
    const candidates = Array.from(root.querySelectorAll(selector));
    for (const link of links) {
      const available = candidates.some(item => groupFor(item) === link.dataset.pfMediaOpenGroup && eligible(item) && visible(item));
      const disabled = String(!available);
      if (link.getAttribute('aria-disabled') !== disabled) link.setAttribute('aria-disabled', disabled);
    }
  }
  // Keep mutation work local to changed elements and newly inserted subtrees.
  function refreshTrigger(item) {
    if (!(item instanceof Element) || dialog?.contains(item)) return;
    if (item.matches(selector) && eligible(item)) {
      if (!item.hasAttribute('data-pf-media-trigger')) item.setAttribute('data-pf-media-trigger', '');
    } else if (item.hasAttribute('data-pf-media-trigger')) item.removeAttribute('data-pf-media-trigger');
  }
  function refreshSubtree(root) {
    refreshTrigger(root);
    root.querySelectorAll(selector + ',[data-pf-media-trigger]').forEach(refreshTrigger);
  }
  refreshSubtree(document.documentElement);
  refreshGroupLinks();
  const watchedAttributes = new Set(['href', 'src', 'srcset', 'data-pf-media', 'data-pf-media-src', 'data-pf-media-for', 'id', 'download', 'hidden', 'inert', 'aria-hidden', 'class', 'style']);
  // A custom selector may depend on attributes beyond the source/opt-out contract.
  for (const match of selector.matchAll(/\[\s*([^\s~|^$*!=\]]+)/g)) watchedAttributes.add(match[1].toLowerCase());
  if (selector.includes('.')) watchedAttributes.add('class');
  if (selector.includes('#')) watchedAttributes.add('id');
  if (selector.includes(':')) {
    // Form/language state can affect pseudo-classes without an explicit [attribute].
    for (const name of ['disabled', 'checked', 'selected', 'required', 'readonly', 'multiple',
      'type', 'value', 'min', 'max', 'step', 'pattern', 'placeholder', 'lang', 'dir',
      'open', 'hidden', 'inert', 'popover']) watchedAttributes.add(name);
  }
  // Relational selectors can change a target outside the mutation's own subtree.
  const needsDocumentScope = selector.toLowerCase().includes(':has(') || selector.toLowerCase().includes(':scope') || /[+~]/.test(selector);
  const pendingRoots = new Set();
  const pendingElements = new Set();
  const pendingGroupRoots = new Set();
  let triggerRefreshPending = false;
  new MutationObserver(records => {
    for (const record of records) {
      if (dialog?.contains(record.target)) continue;
      const scope = record.target.closest?.('[data-pf-media-scope]');
      if (scope) pendingGroupRoots.add(scope);
      else if (record.target.querySelector?.('[data-pf-media-open-group]')) pendingGroupRoots.add(record.target);
      if (needsDocumentScope) {
        pendingRoots.add(document.documentElement);
      } else if (record.type === 'attributes') {
        // Include siblings for selectors such as .selected + a or :has(...).
        pendingRoots.add(record.target.parentElement || record.target);
      } else {
        if (record.target instanceof Element) pendingRoots.add(record.target);
      }
      for (let node = record.target; node instanceof Element; node = node.parentElement) pendingElements.add(node);
    }
    if (triggerRefreshPending || (!pendingRoots.size && !pendingElements.size)) return;
    triggerRefreshPending = true;
    requestAnimationFrame(() => {
      triggerRefreshPending = false;
      for (const root of pendingRoots) if (root.isConnected) refreshSubtree(root);
      for (const item of pendingElements) if (item.isConnected) refreshTrigger(item);
      pendingRoots.clear(); pendingElements.clear();
      for (const root of pendingGroupRoots) if (root.isConnected) refreshGroupLinks(root);
      pendingGroupRoots.clear();
    });
  }).observe(document.documentElement, { childList:true, subtree:true, attributes:true, attributeFilter:[...watchedAttributes] });
  function button(label, text, action) {
    const result = document.createElement('button');
    result.type = 'button'; result.textContent = translate(text); result.setAttribute('aria-label', translate(label));
    result.title = translate(label); result.addEventListener('click', action); return result;
  }
  // Inline strokes keep controls consistent without an external icon dependency.
  function icon(name) {
    const paths = {
      back: 'M19 12H5m6-6-6 6 6 6', next: 'M5 12h14m-6-6 6 6-6 6',
      minus: 'M5 12h14', plus: 'M5 12h14M12 5v14',
      fit: 'M8 4H4v4m12-4h4v4M4 16v4h4m12-4v4h-4',
      external: 'M14 4h6v6m0-6L10 14M10 4H5a1 1 0 0 0-1 1v14a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-5',
      context: 'M6 3h12v18H6zM9 7h6M9 11h6m-4 4 2 2 2-2',
      settings: 'M4 7h9m4 0h3M4 17h3m4 0h9M13 4v6M7 14v6'
    };
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('viewBox', '0 0 24 24'); svg.setAttribute('aria-hidden', 'true');
    svg.setAttribute('fill', 'none'); svg.setAttribute('stroke', 'currentColor');
    svg.setAttribute('stroke-width', '1.6'); svg.setAttribute('stroke-linecap', 'round'); svg.setAttribute('stroke-linejoin', 'round');
    const path = document.createElementNS(svg.namespaceURI, 'path'); path.setAttribute('d', paths[name]); svg.append(path);
    return svg;
  }
  function sizeFrame(image) {
    dialog.style.setProperty('--pf-media-image-width', `${image.naturalWidth || 960}px`);
    dialog.style.setProperty('--pf-media-image-height', `${image.naturalHeight || 640}px`);
  }
  function create() {
    dialog = document.createElement('dialog'); dialog.className = 'pf-media-viewer';
    dialog.setAttribute('aria-label', translate('Image viewer'));
    const header = document.createElement('div'); header.className = 'pf-media-viewer__header';
    const heading = document.createElement('div'); heading.className = 'pf-media-viewer__heading';
    title = document.createElement('span'); title.textContent = translate('Image viewer');
    caption = document.createElement('p'); heading.append(title, caption);
    navigation = document.createElement('div'); navigation.className = 'pf-media-viewer__navigation';
    const toolbar = document.createElement('div'); toolbar.className = 'pf-media-viewer__toolbar';
    const zoomControls = document.createElement('div'); zoomControls.className = 'pf-media-viewer__zoom';
    settings = document.createElement('details'); settings.className = 'pf-media-viewer__settings';
    const settingsToggle = document.createElement('summary'); settingsToggle.title = translate('Display settings');
    settingsToggle.setAttribute('aria-label', translate('Display settings')); settingsToggle.append(icon('settings'));
    const appearance = document.createElement('div'); appearance.className = 'pf-media-viewer__appearance';
    const settingsTitle = document.createElement('strong'); settingsTitle.textContent = translate('Display settings');
    appearance.append(settingsTitle); settings.append(settingsToggle, appearance);
    previous = button(translate('Previous image'), '←', () => show(index - 1));
    next = button(translate('Next image'), '→', () => show(index + 1));
    previous.replaceChildren(icon('back')); next.replaceChildren(icon('next'));
    count = document.createElement('span'); count.className = 'pf-media-viewer__count';
    const fit = button(translate('Fit image to window'), translate('Fit'), () => { fitMode = true; resize(); });
    fitButton = fit; fit.className = 'pf-media-viewer__fit';
    const fitLabel = document.createElement('span'); fitLabel.textContent = translate('Fit'); fit.replaceChildren(icon('fit'), fitLabel);
    const actual = button(translate('Show image at actual size'), '1:1', () => zoom(1));
    actualButton = actual;
    zoomValue = document.createElement('span'); zoomValue.className = 'pf-media-viewer__zoom-value';
    const minus = button(translate('Zoom out'), '−', () => zoom(scale / 1.4));
    const plus = button(translate('Zoom in'), '+', () => zoom(scale * 1.4));
    minus.replaceChildren(icon('minus')); plus.replaceChildren(icon('plus'));
    const background = document.createElement('select'); background.setAttribute('aria-label', translate('Image background'));
    for (const [value, label] of [['dark', translate('Dark')], ['light', translate('Light')], ['checkerboard', translate('Transparency')]]) {
      background.add(new Option(label, value));
    }
    background.addEventListener('change', () => { stage.dataset.background = background.value; });
    variant = document.createElement('select'); variant.setAttribute('aria-label', translate('Image variant'));
    variant.addEventListener('change', load);
    original = document.createElement('a'); original.className = 'pf-media-viewer__original';
    original.setAttribute('aria-label', translate('Open original'));
    const originalLabel = document.createElement('span'); originalLabel.textContent = translate('Open original');
    original.append(icon('external'), originalLabel);
    original.target = '_blank'; original.rel = 'noopener'; original.title = translate('Open original');
    const close = button('Back to page', 'Back to page', () => dialog.close()); close.className = 'pf-media-viewer__close';
    close.prepend(icon('back'));
    navigation.append(previous, count, next);
    header.append(heading, navigation, close);
    zoomControls.append(minus, zoomValue, plus, fit, actual);
    const backgroundField = document.createElement('label'); backgroundField.textContent = translate('Image background');
    backgroundField.append(background);
    variantField = document.createElement('label'); variantField.textContent = translate('Image variant'); variantField.append(variant);
    appearance.append(backgroundField, variantField);
    contextButton = button('Show in page', 'Show in page', () => { returnTarget = items[index]; dialog.close(); });
    contextButton.className = 'pf-media-viewer__context';
    const contextLabel = document.createElement('span'); contextLabel.textContent = translate('Show in page');
    contextButton.replaceChildren(icon('context'), contextLabel);
    const actions = document.createElement('div'); actions.className = 'pf-media-viewer__actions'; actions.append(contextButton, settings, original);
    toolbar.append(zoomControls, actions);
    thumbnails = document.createElement('div'); thumbnails.className = 'pf-media-viewer__thumbnails';
    thumbnails.setAttribute('role', 'group'); thumbnails.setAttribute('aria-label', translate('Image viewer'));
    stage = document.createElement('div'); stage.className = 'pf-media-viewer__stage'; stage.tabIndex = 0;
    stage.setAttribute('aria-label', translate('Image preview. Use zoom controls and scroll to explore.'));
    errorMessage = document.createElement('p'); errorMessage.className = 'pf-media-viewer__error'; errorMessage.hidden = true;
    errorMessage.setAttribute('role', 'alert');
    canvas = document.createElement('div'); canvas.className = 'pf-media-viewer__canvas';
    picture = document.createElement('img'); picture.className = 'pf-media-viewer__image'; picture.draggable = false;
    picture.addEventListener('load', () => { stage.dataset.loading = 'false'; picture.hidden = false; sizeFrame(picture); resize(); });
    picture.addEventListener('error', () => { stage.dataset.loading = 'false'; picture.hidden = true; status.textContent = ''; errorMessage.hidden = false; errorMessage.textContent = translate('Image could not be loaded. Use Open original to try the source.'); });
    canvas.append(picture, errorMessage); stage.append(canvas);
    const footer = document.createElement('div'); footer.className = 'pf-media-viewer__footer';
    status = document.createElement('p');
    status.className = 'pf-media-viewer__status'; status.setAttribute('role', 'status');
    announcement = document.createElement('p'); announcement.className = 'pf-media-viewer__announcement';
    announcement.setAttribute('role', 'status'); announcement.setAttribute('aria-atomic', 'true');
    appearance.append(status);
    footer.append(thumbnails, toolbar, announcement); dialog.append(header, stage, footer); document.body.append(dialog);
    dialog.addEventListener('click', event => {
      if (event.target === dialog) dialog.close();
      if (!settings.contains(event.target)) settings.open = false;
    });
    dialog.addEventListener('cancel', event => {
      if (settings.open) { event.preventDefault(); settings.open = false; settingsToggle.focus(); }
    });
    dialog.addEventListener('keydown', event => {
      if (event.target.closest('select, input, textarea') || event.ctrlKey || event.metaKey || event.altKey) return;
      if (event.key === 'ArrowLeft' && items.length > 1 && event.target !== stage) { event.preventDefault(); show(index - 1); }
      if (event.key === 'ArrowRight' && items.length > 1 && event.target !== stage) { event.preventDefault(); show(index + 1); }
      if (event.key === '+') { event.preventDefault(); zoom(scale * 1.4); }
      if (event.key === '-') { event.preventDefault(); zoom(scale / 1.4); }
    });
    dialog.addEventListener('close', () => {
      document.documentElement.classList.remove('pf-media-open');
      picture.removeAttribute('src'); picture.hidden = true; original.removeAttribute('href');
      settings.open = false; thumbnails.replaceChildren(); items = []; drag = null;
      const target = returnTarget || opener; target?.focus({ preventScroll: true });
      if (returnTarget) returnTarget.scrollIntoView({ block: 'center', behavior: 'instant' });
      returnTarget = null;
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
    const style = getComputedStyle(stage);
    const availableWidth = stage.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
    const availableHeight = stage.clientHeight - parseFloat(style.paddingTop) - parseFloat(style.paddingBottom);
    const fitScale = Math.min(1, Math.max(1, availableWidth - 1) / picture.naturalWidth, Math.max(1, availableHeight - 1) / picture.naturalHeight);
    if (fitMode) scale = Math.max(.01, fitScale);
    stage.dataset.zoomed = String(scale > fitScale + .001);
    fitButton.setAttribute('aria-pressed', String(fitMode));
    actualButton.setAttribute('aria-pressed', String(!fitMode && scale === 1));
    zoomValue.textContent = `${Math.round(scale * 100)}%`;
    picture.style.width = `${Math.max(1, picture.naturalWidth * scale)}px`;
    picture.style.height = `${Math.max(1, picture.naturalHeight * scale)}px`;
    status.textContent = `${items.length > 1 ? `${index + 1} / ${items.length} · ` : ""}${picture.naturalWidth} × ${picture.naturalHeight} · ${Math.round(scale * 100)}%`;
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
    fitMode = true; picture.hidden = true; errorMessage.hidden = true; errorMessage.textContent = ''; stage.dataset.loading = 'true'; zoomValue.textContent = '—'; status.textContent = translate('Loading image…');
    picture.alt = imageFor(item)?.alt || item.getAttribute('aria-label') || '';
    announcement.textContent = `${items.length > 1 ? `${index + 1} / ${items.length}. ` : ""}${picture.alt}${caption.textContent !== picture.alt ? '. ' + caption.textContent : ''}${variant.options.length > 1 ? '. ' + variant.selectedOptions[0].textContent : ''}`;
    original.href = url; picture.src = url; stage.scrollTo(0, 0);
  }
  function show(requested) {
    if (!items.length) return;
    index = (requested + items.length) % items.length;
    count.textContent = `${index + 1} / ${items.length}`;
    previous.disabled = next.disabled = items.length < 2;
    const item = items[index];
    caption.textContent = item.dataset.pfMediaCaption || item.closest('figure')?.querySelector('figcaption')?.textContent || imageFor(item)?.alt || '';
    contextButton.hidden = !item.hasAttribute('data-pf-media-context');
    caption.title = caption.textContent;
    variant.replaceChildren(new Option(translate('Original'), 'original'));
    for (const [key, label] of [['pfMediaLight', translate('Light appearance')], ['pfMediaDark', translate('Dark appearance')], ['pfMediaMobile', translate('Mobile')], ['pfMediaDesktop', translate('Desktop')]]) {
      if (item.dataset[key] && safeSource(item.dataset[key])) variant.add(new Option(label, key));
    }
    variantField.hidden = variant.hidden = variant.options.length === 1;
    Array.from(thumbnails.children).forEach((thumbnail, position) => thumbnail.setAttribute('aria-current', String(position === index)));
    const selected = thumbnails.children[index];
    if (selected && thumbnails.scrollWidth > thumbnails.clientWidth)
      thumbnails.scrollLeft = selected.offsetLeft - thumbnails.offsetLeft - (thumbnails.clientWidth - selected.clientWidth) / 2;
    load();
  }
  document.addEventListener('click', event => {
    if (dialog?.contains(event.target)) return;
    if (styles && !styles.sheet) return;
    if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    const groupLink = event.target.closest?.('[data-pf-media-open-group]');
    if (!groupLink && !eligible(event.target.closest?.(selector))) return;
    const candidates = Array.from(document.querySelectorAll(selector));
    const item = groupLink ? candidates.find(candidate => groupFor(candidate) === groupLink.dataset.pfMediaOpenGroup && eligible(candidate) && visible(candidate)) : event.target.closest?.(selector);
    if (groupLink && !item) { event.preventDefault(); return; }
    if (!eligible(item)) return;
    const group = groupFor(item);
    items = group ? candidates.filter(candidate => groupFor(candidate) === group && eligible(candidate) && visible(candidate)) : [item];
    if (!items.includes(item)) return;
    event.preventDefault(); opener = groupLink || item;
    if (!dialog) create();
    document.documentElement.classList.add('pf-media-open');
    thumbnails.replaceChildren();
    const single = items.length < 2;
    dialog.dataset.mode = single ? 'single' : 'gallery';
    title.textContent = translate(single ? 'Image preview' : 'Image gallery');
    dialog.setAttribute('aria-label', title.textContent);
    navigation.hidden = thumbnails.hidden = single;
    items.forEach((entry, position) => {
      const sourceImage = imageFor(entry);
      const thumbnail = button(`${position + 1} / ${items.length}. ${sourceImage.alt || ''}`, '', () => show(position));
      const image = document.createElement('img'); image.alt = ''; image.loading = 'lazy';
      image.src = safeSource(sourceImage.currentSrc || sourceImage.src) || source(entry);
      thumbnail.append(image); thumbnails.append(thumbnail);
    });
    sizeFrame(imageFor(item));
    dialog.showModal(); show(items.indexOf(item));
    dialog.querySelector('.pf-media-viewer__close').focus();
  });
})();
