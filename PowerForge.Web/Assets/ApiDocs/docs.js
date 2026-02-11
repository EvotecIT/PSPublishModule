(function() {
  var sidebar = document.querySelector('.api-sidebar');
  var toggle = document.querySelector('.sidebar-toggle');
  var overlay = document.querySelector('.sidebar-overlay');
  var filterInput = document.querySelector('.sidebar-search input');
  var clearButton = document.querySelector('.clear-search');
  var emptyLabel = document.querySelector('.sidebar-empty');
  var kindButtons = Array.prototype.slice.call(document.querySelectorAll('.filter-button'));
  var namespaceSelect = document.querySelector('#api-namespace');
  var namespaceCombo = null;
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

  function initNavDropdowns() {
    var dropdowns = Array.prototype.slice.call(document.querySelectorAll('.nav-dropdown'));
    if (!dropdowns.length) return;

    dropdowns.forEach(function(dropdown) {
      if (!(dropdown instanceof HTMLElement) || dropdown.dataset.bound === 'true') return;
      dropdown.dataset.bound = 'true';

      var trigger = dropdown.querySelector('.nav-dropdown-trigger');
      if (!(trigger instanceof HTMLElement)) return;

      trigger.setAttribute('aria-haspopup', 'menu');
      trigger.setAttribute('aria-expanded', 'false');

      function setExpanded(expanded) {
        trigger.setAttribute('aria-expanded', expanded ? 'true' : 'false');
        dropdown.classList.toggle('open', expanded);
      }

      dropdown.addEventListener('mouseenter', function() { setExpanded(true); });
      dropdown.addEventListener('mouseleave', function() { setExpanded(false); });
      dropdown.addEventListener('focusin', function() { setExpanded(true); });
      dropdown.addEventListener('focusout', function() {
        setTimeout(function() {
          setExpanded(dropdown.contains(document.activeElement));
        }, 0);
      });
      dropdown.addEventListener('keydown', function(event) {
        if (event.key !== 'Escape') return;
        setExpanded(false);
        trigger.focus();
      });
    });
  }

  function normalize(text) {
    return (text || '').toLowerCase();
  }

  function formatCount(value) {
    var n = parseInt(value, 10);
    if (!Number.isFinite(n)) return '0';
    return n.toLocaleString();
  }

  var activeKind = '';
  var activeNamespace = '';
  var activeMemberKind = '';
  var totalTypes = countLabel ? parseInt(countLabel.dataset.total || '0', 10) : 0;
  namespaceCombo = initNamespaceCombobox(namespaceSelect);

  function initNamespaceCombobox(select) {
    if (!select) return null;
    if (select.dataset.enhancedCombobox === 'true') return null;
    var options = Array.prototype.slice.call(select.options || []);
    if (!options.length) return null;

    var wrapper = document.createElement('div');
    wrapper.className = 'pf-combobox';
    var trigger = document.createElement('button');
    trigger.type = 'button';
    trigger.className = 'pf-combobox-trigger';
    trigger.setAttribute('aria-haspopup', 'listbox');
    trigger.setAttribute('aria-expanded', 'false');
    trigger.setAttribute('aria-label', 'Namespace filter');

    var panel = document.createElement('div');
    panel.className = 'pf-combobox-panel';
    panel.hidden = true;
    var list = document.createElement('div');
    list.className = 'pf-combobox-list';
    list.setAttribute('role', 'listbox');
    list.tabIndex = -1;
    panel.appendChild(list);

    var optionButtons = [];

    function closePanel() {
      wrapper.classList.remove('open');
      panel.hidden = true;
      trigger.setAttribute('aria-expanded', 'false');
    }

    function openPanel() {
      wrapper.classList.add('open');
      panel.hidden = false;
      trigger.setAttribute('aria-expanded', 'true');
      var selected = getSelectedOptionButton();
      if (selected) {
        selected.focus();
        selected.scrollIntoView({ block: 'nearest' });
      } else if (optionButtons.length) {
        optionButtons[0].focus();
      }
    }

    function getSelectedOptionButton() {
      for (var i = 0; i < optionButtons.length; i++) {
        if (optionButtons[i].classList.contains('active')) return optionButtons[i];
      }
      return null;
    }

    function moveFocus(direction) {
      if (!optionButtons.length) return;
      var current = document.activeElement;
      var index = optionButtons.indexOf(current);
      if (index < 0) {
        var selected = getSelectedOptionButton();
        index = selected ? optionButtons.indexOf(selected) : 0;
      }
      var next = index + direction;
      if (next < 0) next = optionButtons.length - 1;
      if (next >= optionButtons.length) next = 0;
      optionButtons[next].focus();
      optionButtons[next].scrollIntoView({ block: 'nearest' });
    }

    function syncFromSelect() {
      var selected = options.find(function(opt) { return opt.value === select.value; });
      if (!selected) {
        selected = select.options[select.selectedIndex] || options[0];
      }
      if (!selected) return;

      trigger.textContent = selected.textContent || selected.label || '';
      optionButtons.forEach(function(btn) {
        var isActive = btn.dataset.value === selected.value;
        btn.classList.toggle('active', isActive);
        btn.setAttribute('aria-selected', isActive ? 'true' : 'false');
      });
    }

    function setValue(value) {
      if (select.value === value) return;
      select.value = value;
      select.dispatchEvent(new Event('change', { bubbles: true }));
    }

    options.forEach(function(option) {
      var btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'pf-combobox-option';
      btn.dataset.value = option.value || '';
      btn.setAttribute('role', 'option');
      btn.setAttribute('aria-selected', 'false');
      btn.textContent = option.textContent || option.label || '';
      if (option.disabled) btn.disabled = true;
      btn.addEventListener('click', function() {
        if (btn.disabled) return;
        setValue(btn.dataset.value || '');
        closePanel();
        trigger.focus();
      });
      list.appendChild(btn);
      optionButtons.push(btn);
    });

    trigger.addEventListener('click', function() {
      if (wrapper.classList.contains('open')) {
        closePanel();
      } else {
        openPanel();
      }
    });

    trigger.addEventListener('keydown', function(event) {
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        if (!wrapper.classList.contains('open')) openPanel();
        moveFocus(event.key === 'ArrowDown' ? 1 : -1);
      }
      if (event.key === 'Enter' || event.key === ' ') {
        event.preventDefault();
        if (wrapper.classList.contains('open')) closePanel();
        else openPanel();
      }
      if (event.key === 'Escape') closePanel();
    });

    list.addEventListener('keydown', function(event) {
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        moveFocus(event.key === 'ArrowDown' ? 1 : -1);
      }
      if (event.key === 'Home') {
        event.preventDefault();
        if (optionButtons.length) optionButtons[0].focus();
      }
      if (event.key === 'End') {
        event.preventDefault();
        if (optionButtons.length) optionButtons[optionButtons.length - 1].focus();
      }
      if (event.key === 'Enter' || event.key === ' ') {
        var focused = document.activeElement;
        if (focused && focused.classList.contains('pf-combobox-option')) {
          event.preventDefault();
          focused.click();
        }
      }
      if (event.key === 'Escape') {
        event.preventDefault();
        closePanel();
        trigger.focus();
      }
    });

    document.addEventListener('click', function(event) {
      if (!wrapper.contains(event.target)) closePanel();
    });

    select.dataset.enhancedCombobox = 'true';
    select.classList.add('pf-enhanced-native');
    select.insertAdjacentElement('afterend', wrapper);
    wrapper.appendChild(trigger);
    wrapper.appendChild(panel);
    syncFromSelect();

    return { sync: syncFromSelect };
  }

  function syncNamespaceCombobox() {
    if (namespaceCombo && typeof namespaceCombo.sync === 'function') namespaceCombo.sync();
  }

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
      countLabel.textContent = 'Showing ' + formatCount(visibleCount) + ' of ' + formatCount(total) + ' types';
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
      syncNamespaceCombobox();
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
      syncNamespaceCombobox();
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
  syncNamespaceCombobox();
  if (memberKindButtons.length) {
    memberKindButtons.forEach(function(b) { b.classList.remove('active'); });
    var activeMemberBtn = memberKindButtons.find(function(b) { return (b.dataset.memberKind || '') === activeMemberKind; });
    (activeMemberBtn || memberKindButtons[0]).classList.add('active');
  }
  initNavDropdowns();
  applyFilter(filterInput ? filterInput.value : '');
  applyMemberFilter();
})();
