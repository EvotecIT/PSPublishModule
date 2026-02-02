(function() {
  var sidebar = document.querySelector('.api-sidebar');
  var toggle = document.querySelector('.sidebar-toggle');
  var overlay = document.querySelector('.sidebar-overlay');
  var filterInput = document.querySelector('.sidebar-search input');
  var clearButton = document.querySelector('.clear-search');

  function setSidebar(open) {
    if (!sidebar || !overlay) return;
    sidebar.classList.toggle('sidebar-open', open);
    overlay.classList.toggle('active', open);
  }

  if (toggle) {
    toggle.addEventListener('click', function() {
      var open = !sidebar || !sidebar.classList.contains('sidebar-open');
      setSidebar(open);
    });
  }

  if (overlay) {
    overlay.addEventListener('click', function() {
      setSidebar(false);
    });
  }

  function normalize(text) {
    return (text || '').toLowerCase();
  }

  function applyFilter(query) {
    var q = normalize(query);
    var items = Array.prototype.slice.call(document.querySelectorAll('.type-item'));
    var chips = Array.prototype.slice.call(document.querySelectorAll('.type-chip'));
    var sections = Array.prototype.slice.call(document.querySelectorAll('.nav-section'));

    items.forEach(function(item) {
      var hay = normalize(item.dataset.search || item.textContent);
      var match = !q || hay.indexOf(q) !== -1;
      item.style.display = match ? '' : 'none';
    });

    chips.forEach(function(chip) {
      var hay = normalize(chip.dataset.search || chip.textContent);
      var match = !q || hay.indexOf(q) !== -1;
      chip.style.display = match ? '' : 'none';
    });

    sections.forEach(function(section) {
      var hasVisible = false;
      section.querySelectorAll('.type-item').forEach(function(item) {
        if (item.style.display !== 'none') {
          hasVisible = true;
        }
      });
      section.style.display = hasVisible ? '' : 'none';
    });
  }

  if (filterInput) {
    filterInput.addEventListener('input', function() {
      applyFilter(filterInput.value);
      if (clearButton) {
        clearButton.style.display = filterInput.value ? 'inline-flex' : 'none';
      }
    });
  }

  if (clearButton && filterInput) {
    clearButton.addEventListener('click', function() {
      filterInput.value = '';
      applyFilter('');
      clearButton.style.display = 'none';
      filterInput.focus();
    });
    clearButton.style.display = filterInput.value ? 'inline-flex' : 'none';
  }

  document.querySelectorAll('.nav-section-header').forEach(function(header) {
    header.addEventListener('click', function() {
      var section = header.closest('.nav-section');
      if (!section) return;
      var content = section.querySelector('.nav-section-content');
      var chevron = header.querySelector('.chevron');
      if (!content) return;
      var collapsed = content.classList.toggle('collapsed');
      content.hidden = collapsed;
      if (chevron) {
        chevron.classList.toggle('expanded', !collapsed);
      }
    });
  });
})();
