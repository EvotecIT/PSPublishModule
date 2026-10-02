(function (selectors) {
  var seen = new Set();
  var findings = [];
  selectors.forEach(function (selector) {
    document.querySelectorAll(selector).forEach(function (element) {
      if (seen.has(element) || findings.length >= 10) return;
      seen.add(element);
      var box = element.getBoundingClientRect();
      var style = getComputedStyle(element);
      if (box.width === 0 || box.height === 0 || style.visibility === 'hidden' ||
          element.closest('[hidden], [inert]')) return;
      if (box.left < -1 || box.right > window.innerWidth + 1) {
        findings.push({ selector: selector, left: Math.round(box.left), right: Math.round(box.right),
          viewportWidth: window.innerWidth, text: (element.textContent || '').trim().slice(0, 80) });
      }
    });
  });
  return JSON.stringify(findings);
})
