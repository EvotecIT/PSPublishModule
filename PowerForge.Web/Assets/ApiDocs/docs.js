(function() {
  var sidebar = document.querySelector('.api-sidebar');
  var toggle = document.querySelector('.sidebar-toggle');
  var overlay = document.querySelector('.sidebar-overlay');
  var filterInput = document.querySelector('.sidebar-search input');
  var clearButton = document.querySelector('.clear-search');
  var emptyLabel = document.querySelector('.sidebar-empty');
  var kindButtons = Array.prototype.slice.call(document.querySelectorAll('.filter-button'));
  var namespaceSelect = document.querySelector('#api-namespace');
  var countLabel = document.querySelector('.sidebar-count');
  var expandAllBtn = document.querySelector('.sidebar-expand-all');
  var collapseAllBtn = document.querySelector('.sidebar-collapse-all');
  var resetButton = document.querySelector('.sidebar-reset');
  var memberFilter = document.querySelector('#api-member-filter');
  var memberKindButtons = Array.prototype.slice.call(document.querySelectorAll('.member-kind'));
  var inheritedToggle = document.querySelector('#api-show-inherited');
  var memberExpandAll = document.querySelector('.member-expand-all');
  var memberCollapseAll = document.querySelector('.member-collapse-all');
  var memberReset = document.querySelector('.member-reset');
  var tocToggle = document.querySelector('.type-toc-toggle');
  var memberSectionToggles = Array.prototype.slice.call(document.querySelectorAll('.member-section-toggle'));

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
  var activeMemberKind = '';
  var totalTypes = countLabel ? parseInt(countLabel.dataset.total || '0', 10) : 0;

  function loadState() {
    var hash = window.location.hash || '';
    if (!hash) return;
    var query = hash.indexOf('?') >= 0 ? hash.split('?')[1] : hash.slice(1);
    if (!query) return;
    query.split('&').forEach(function(pair) {
      var parts = pair.split('=');
      if (parts.length < 2) return;
      var key = decodeURIComponent(parts[0]);
      var value = decodeURIComponent(parts.slice(1).join('='));
      if (key === 'k') activeKind = value;
      if (key === 'ns') activeNamespace = value;
      if (key === 'q' && filterInput) filterInput.value = value;
      if (key === 'mk') activeMemberKind = value;
      if (key === 'mq' && memberFilter) memberFilter.value = value;
      if (key === 'mi' && inheritedToggle) inheritedToggle.checked = value === '1';
      if (key === 'mc') window.__pfMemberCollapsed = value;
      if (key === 'tc') window.__pfTocCollapsed = value === '1';
    });
  }

  function saveState() {
    var parts = [];
    if (activeKind) parts.push('k=' + encodeURIComponent(activeKind));
    if (activeNamespace) parts.push('ns=' + encodeURIComponent(activeNamespace));
    if (filterInput && filterInput.value) parts.push('q=' + encodeURIComponent(filterInput.value));
    if (activeMemberKind) parts.push('mk=' + encodeURIComponent(activeMemberKind));
    if (memberFilter && memberFilter.value) parts.push('mq=' + encodeURIComponent(memberFilter.value));
    if (inheritedToggle && inheritedToggle.checked) parts.push('mi=1');
    if (window.__pfMemberCollapsed) parts.push('mc=' + encodeURIComponent(window.__pfMemberCollapsed));
    if (window.__pfTocCollapsed) parts.push('tc=1');
    if (!parts.length) {
      history.replaceState(null, '', window.location.pathname + window.location.search);
      return;
    }
    history.replaceState(null, '', window.location.pathname + window.location.search + '#' + parts.join('&'));
  }

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

    if (countLabel) {
      var visibleCount = items.filter(function(item) { return item.style.display !== 'none'; }).length;
      var total = totalTypes || items.length;
      countLabel.textContent = 'Showing ' + visibleCount + ' of ' + total + ' types';
    }
    saveState();
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

  if (resetButton) {
    resetButton.addEventListener('click', function() {
      activeKind = '';
      activeNamespace = '';
      if (filterInput) {
        filterInput.value = '';
      }
      if (namespaceSelect) {
        namespaceSelect.value = '';
      }
      kindButtons.forEach(function(b) { b.classList.remove('active'); });
      if (kindButtons.length) {
        kindButtons[0].classList.add('active');
      }
      if (clearButton) {
        clearButton.style.display = 'none';
      }
      applyFilter('');
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

  function setAllSections(collapsed) {
    var sections = Array.prototype.slice.call(document.querySelectorAll('.nav-section'));
    sections.forEach(function(section) {
      var content = section.querySelector('.nav-section-content');
      if (!content) return;
      var chevron = section.querySelector('.chevron');
      if (collapsed) {
        content.classList.add('collapsed');
        content.hidden = true;
        if (chevron) chevron.classList.remove('expanded');
      } else {
        content.classList.remove('collapsed');
        content.hidden = false;
        if (chevron) chevron.classList.add('expanded');
      }
    });
  }

  if (expandAllBtn) {
    expandAllBtn.addEventListener('click', function() {
      setAllSections(false);
    });
  }

  if (collapseAllBtn) {
    collapseAllBtn.addEventListener('click', function() {
      setAllSections(true);
    });
  }

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
    saveState();
  }

  if (memberFilter) {
    memberFilter.addEventListener('input', function() {
      applyMemberFilter();
      saveState();
    });
  }

  if (memberKindButtons.length) {
    memberKindButtons.forEach(function(btn) {
      btn.addEventListener('click', function() {
        memberKindButtons.forEach(function(b) { b.classList.remove('active'); });
        btn.classList.add('active');
        activeMemberKind = btn.dataset.memberKind || '';
        applyMemberFilter();
        saveState();
      });
    });
  }

  if (inheritedToggle) {
    inheritedToggle.addEventListener('change', function() {
      applyMemberFilter();
      saveState();
    });
  }

  function updateMemberCollapsedState() {
    var collapsedKeys = [];
    document.querySelectorAll('.member-section').forEach(function(section) {
      if (section.classList.contains('collapsed')) {
        var id = section.getAttribute('id');
        if (id) collapsedKeys.push(id);
      }
    });
    window.__pfMemberCollapsed = collapsedKeys.join(',');
  }

  memberSectionToggles.forEach(function(btn) {
    btn.addEventListener('click', function() {
      var section = btn.closest('.member-section');
      if (!section) return;
      var body = section.querySelector('.member-section-body');
      if (!body) return;
      var collapsed = section.classList.toggle('collapsed');
      body.hidden = collapsed;
      updateMemberCollapsedState();
      saveState();
    });
  });

  function setMemberSections(collapsed) {
    document.querySelectorAll('.member-section').forEach(function(section) {
      var body = section.querySelector('.member-section-body');
      if (!body) return;
      if (collapsed) {
        section.classList.add('collapsed');
        body.hidden = true;
      } else {
        section.classList.remove('collapsed');
        body.hidden = false;
      }
    });
    updateMemberCollapsedState();
    saveState();
  }

  if (memberExpandAll) {
    memberExpandAll.addEventListener('click', function() {
      setMemberSections(false);
    });
  }

  if (memberCollapseAll) {
    memberCollapseAll.addEventListener('click', function() {
      setMemberSections(true);
    });
  }

  if (memberReset) {
    memberReset.addEventListener('click', function() {
      activeMemberKind = '';
      if (memberFilter) memberFilter.value = '';
      if (memberKindButtons.length) {
        memberKindButtons.forEach(function(b) { b.classList.remove('active'); });
        memberKindButtons[0].classList.add('active');
      }
      if (inheritedToggle) inheritedToggle.checked = false;
      applyMemberFilter();
      saveState();
    });
  }

  if (tocToggle) {
    tocToggle.addEventListener('click', function() {
      var toc = tocToggle.closest('.type-toc');
      if (!toc) return;
      toc.classList.toggle('collapsed');
      window.__pfTocCollapsed = toc.classList.contains('collapsed');
      saveState();
    });
  }

  loadState();
  if (window.__pfTocCollapsed && tocToggle) {
    var toc = tocToggle.closest('.type-toc');
    if (toc) toc.classList.add('collapsed');
  }
  if (window.__pfMemberCollapsed) {
    var collapsedIds = window.__pfMemberCollapsed.split(',').filter(Boolean);
    collapsedIds.forEach(function(id) {
      var section = document.getElementById(id);
      if (!section) return;
      var body = section.querySelector('.member-section-body');
      if (!body) return;
      section.classList.add('collapsed');
      body.hidden = true;
    });
  }
  if (kindButtons.length) {
    kindButtons.forEach(function(b) { b.classList.remove('active'); });
    var activeBtn = kindButtons.find(function(b) { return (b.dataset.kind || '') === activeKind; });
    (activeBtn || kindButtons[0]).classList.add('active');
  }
  if (namespaceSelect) namespaceSelect.value = activeNamespace || '';
  if (memberKindButtons.length) {
    memberKindButtons.forEach(function(b) { b.classList.remove('active'); });
    var activeMemberBtn = memberKindButtons.find(function(b) { return (b.dataset.memberKind || '') === activeMemberKind; });
    (activeMemberBtn || memberKindButtons[0]).classList.add('active');
  }
  applyFilter(filterInput ? filterInput.value : '');
  applyMemberFilter();
})();
