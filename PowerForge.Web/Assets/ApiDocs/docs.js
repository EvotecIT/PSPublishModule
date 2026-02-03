(function() {
  var sidebar = document.querySelector('.api-sidebar');
  var toggle = document.querySelector('.sidebar-toggle');
  var overlay = document.querySelector('.sidebar-overlay');
  var filterInput = document.querySelector('.sidebar-search input');
  var clearButton = document.querySelector('.clear-search');
  var emptyLabel = document.querySelector('.sidebar-empty');
  var kindButtons = Array.prototype.slice.call(document.querySelectorAll('.filter-button'));
  var namespaceSelect = document.querySelector('#api-namespace');
  var memberFilter = document.querySelector('#api-member-filter');
  var memberKindButtons = Array.prototype.slice.call(document.querySelectorAll('.member-kind'));
  var inheritedToggle = document.querySelector('#api-show-inherited');

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

  var activeKind = '';
  var activeNamespace = '';

  function applyFilter(query) {
    var q = normalize(query);
    var items = Array.prototype.slice.call(document.querySelectorAll('.type-item'));
    var chips = Array.prototype.slice.call(document.querySelectorAll('.type-chip'));
    var sections = Array.prototype.slice.call(document.querySelectorAll('.nav-section'));

    items.forEach(function(item) {
      var hay = normalize(item.dataset.search || item.textContent);
      var matchSearch = !q || hay.indexOf(q) !== -1;
      var matchKind = !activeKind || item.dataset.kind === activeKind;
      var matchNamespace = !activeNamespace || item.dataset.namespace === activeNamespace;
      var match = matchSearch && matchKind && matchNamespace;
      item.style.display = match ? '' : 'none';
    });

    chips.forEach(function(chip) {
      var hay = normalize(chip.dataset.search || chip.textContent);
      var matchSearch = !q || hay.indexOf(q) !== -1;
      var matchKind = !activeKind || chip.dataset.kind === activeKind;
      var matchNamespace = !activeNamespace || chip.dataset.namespace === activeNamespace;
      var match = matchSearch && matchKind && matchNamespace;
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

    if (emptyLabel) {
      var anyVisible = items.some(function(item) { return item.style.display !== 'none'; });
      emptyLabel.hidden = anyVisible;
    }
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

  if (kindButtons.length) {
    kindButtons.forEach(function(btn) {
      btn.addEventListener('click', function() {
        kindButtons.forEach(function(b) { b.classList.remove('active'); });
        btn.classList.add('active');
        activeKind = btn.dataset.kind || '';
        applyFilter(filterInput ? filterInput.value : '');
      });
    });
  }

  if (namespaceSelect) {
    namespaceSelect.addEventListener('change', function() {
      activeNamespace = namespaceSelect.value || '';
      applyFilter(filterInput ? filterInput.value : '');
    });
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

  var activeMemberKind = '';

  function applyMemberFilter() {
    var q = normalize(memberFilter ? memberFilter.value : '');
    var showInherited = inheritedToggle ? inheritedToggle.checked : true;
    var cards = Array.prototype.slice.call(document.querySelectorAll('.member-card'));
    var sections = Array.prototype.slice.call(document.querySelectorAll('.member-section'));
    var groups = Array.prototype.slice.call(document.querySelectorAll('.member-group'));

    cards.forEach(function(card) {
      var hay = normalize(card.dataset.search || card.textContent);
      var matchSearch = !q || hay.indexOf(q) !== -1;
      var matchKind = !activeMemberKind || card.dataset.kind === activeMemberKind;
      var matchInherited = showInherited || card.dataset.inherited !== 'true';
      var match = matchSearch && matchKind && matchInherited;
      card.style.display = match ? '' : 'none';
    });

    groups.forEach(function(group) {
      var hasVisible = false;
      group.querySelectorAll('.member-card').forEach(function(card) {
        if (card.style.display !== 'none') hasVisible = true;
      });
      group.style.display = hasVisible ? '' : 'none';
    });

    sections.forEach(function(section) {
      var visible = false;
      section.querySelectorAll('.member-card').forEach(function(card) {
        if (card.style.display !== 'none') visible = true;
      });
      section.style.display = visible ? '' : 'none';
    });
  }

  if (memberFilter) {
    memberFilter.addEventListener('input', applyMemberFilter);
  }

  if (memberKindButtons.length) {
    memberKindButtons.forEach(function(btn) {
      btn.addEventListener('click', function() {
        memberKindButtons.forEach(function(b) { b.classList.remove('active'); });
        btn.classList.add('active');
        activeMemberKind = btn.dataset.memberKind || '';
        applyMemberFilter();
      });
    });
  }

  if (inheritedToggle) {
    inheritedToggle.addEventListener('change', applyMemberFilter);
  }

  document.querySelectorAll('.member-section-toggle').forEach(function(btn) {
    btn.addEventListener('click', function() {
      var section = btn.closest('.member-section');
      if (!section) return;
      var body = section.querySelector('.member-section-body');
      if (!body) return;
      var collapsed = section.classList.toggle('collapsed');
      body.hidden = collapsed;
    });
  });

  applyFilter(filterInput ? filterInput.value : '');
  applyMemberFilter();
})();
