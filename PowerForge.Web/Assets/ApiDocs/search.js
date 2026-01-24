(() => {
  const input = document.getElementById('api-search');
  const results = document.getElementById('api-results');
  if (!input || !results) return;

  let data = [];
  fetch('search.json')
    .then((r) => r.json())
    .then((items) => {
      data = items || [];
    });

  const render = (items) => {
    results.innerHTML = '';
    if (!items.length) {
      results.innerHTML = '<div class="pf-api-empty">No results</div>';
      return;
    }

    const frag = document.createDocumentFragment();
    items.slice(0, 24).forEach((item) => {
      const link = document.createElement('a');
      link.className = 'pf-api-result';
      const slug = item.slug || '';
      link.href = 'types/' + slug + '.html';
      link.innerHTML = '<strong>' + (item.title || '') + '</strong><span>' + (item.summary || '') + '</span>';
      frag.appendChild(link);
    });
    results.appendChild(frag);
  };

  input.addEventListener('input', () => {
    const q = input.value.trim().toLowerCase();
    if (!q) {
      results.innerHTML = '';
      return;
    }
    const filtered = data.filter((x) => (x.title || '').toLowerCase().includes(q) || (x.summary || '').toLowerCase().includes(q));
    render(filtered);
  });
})();
