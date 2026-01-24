(() => {
  const input = document.querySelector('[data-pf-search-input]');
  const results = document.querySelector('[data-pf-search-results]');
  if (!input || !results) return;

  let index = [];
  const render = (items) => {
    if (!items.length) {
      results.innerHTML = '<div class="pf-search-empty">No results</div>';
      return;
    }
    results.innerHTML = items.map(item => {
      const tags = (item.tags || []).map(t => `<span class="pf-chip">${t}</span>`).join('');
      return `
        <a class="pf-search-item" href="${item.url}">
          <div class="pf-search-title">${item.title}</div>
          <div class="pf-search-snippet">${item.snippet || item.description || ''}</div>
          <div class="pf-search-tags">${tags}</div>
        </a>
      `;
    }).join('');
  };

  fetch('/search/index.json')
    .then(r => r.ok ? r.json() : [])
    .then(data => { index = data || []; })
    .catch(() => { index = []; });

  input.addEventListener('input', () => {
    const q = input.value.trim().toLowerCase();
    if (!q) {
      results.innerHTML = '';
      return;
    }
    const hits = index.filter(item => {
      const hay = `${item.title} ${item.description} ${item.snippet} ${(item.tags || []).join(' ')}`.toLowerCase();
      return hay.includes(q);
    }).slice(0, 10);
    render(hits);
  });
})();
