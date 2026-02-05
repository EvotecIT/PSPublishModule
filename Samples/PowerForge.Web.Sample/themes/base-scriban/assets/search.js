(() => {
  const input = document.querySelector('[data-pf-search-input]');
  const results = document.querySelector('[data-pf-search-results]');
  const collectionFilter = document.querySelector('[data-pf-search-collection]');
  const projectFilter = document.querySelector('[data-pf-search-project]');
  const tagFilter = document.querySelector('[data-pf-search-tag]');
  if (!input || !results) return;

  let index = [];
  const render = (items) => {
    if (!items.length) {
      results.innerHTML = '<div class="pf-search-empty">No results</div>';
      return;
    }
    results.innerHTML = items.map(item => {
      const tags = (item.tags || []).map(t => `<span class="pf-chip">${t}</span>`).join('');
      const meta = item.project ? `<span class="pf-chip">${item.project}</span>` : '';
      return `
        <a class="pf-search-item" href="${item.url}">
          <div class="pf-search-title">${item.title}</div>
          <div class="pf-search-snippet">${item.snippet || item.description || ''}</div>
          <div class="pf-search-tags">${meta}${tags}</div>
        </a>
      `;
    }).join('');
  };

  fetch('/search/index.json')
    .then(r => r.ok ? r.json() : [])
    .then(data => { index = data || []; })
    .catch(() => { index = []; });

  const applyFilters = (items) => {
    const collectionValue = collectionFilter ? collectionFilter.value : '';
    const projectValue = projectFilter ? projectFilter.value : '';
    const tagValue = tagFilter ? tagFilter.value.trim().toLowerCase() : '';

    return items.filter(item => {
      if (collectionValue && item.collection !== collectionValue) return false;
      if (projectValue && item.project !== projectValue) return false;
      if (tagValue) {
        const tags = (item.tags || []).map(t => (t || '').toLowerCase());
        if (!tags.includes(tagValue)) return false;
      }
      return true;
    });
  };

  const updateResults = () => {
    const q = input.value.trim().toLowerCase();
    if (!q) {
      results.innerHTML = '';
      return;
    }
    const hits = index.filter(item => {
      const hay = `${item.title} ${item.description} ${item.snippet} ${(item.tags || []).join(' ')}`.toLowerCase();
      return hay.includes(q);
    });
    render(applyFilters(hits).slice(0, 10));
  };

  input.addEventListener('input', updateResults);
  [collectionFilter, projectFilter, tagFilter].forEach(el => {
    if (!el) return;
    el.addEventListener('change', updateResults);
  });
})();
