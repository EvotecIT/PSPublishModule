async () => {
    await document.fonts.ready;
    const images = [...document.images];
    await Promise.all(images.map(image => image.decode()));
    if (images.some(image => !image.naturalWidth)) throw new Error('Capture did not decode.');
    const headline = document.querySelector('h1');
    const unit = Number(getComputedStyle(document.documentElement).getPropertyValue('--u'));
    const floor = 88 * unit;
    const header = document.querySelector('header');
    // Two headline lines are the frame grammar; reserve the upper third for all captions.
    const fits = () => header.scrollWidth <= header.clientWidth + 1 &&
        [...header.querySelectorAll('p')].every(p => p.scrollWidth <= p.clientWidth + 1) &&
        headline.scrollWidth <= headline.clientWidth + 1 &&
        headline.getBoundingClientRect().height <= 2.05 * parseFloat(getComputedStyle(headline).lineHeight) &&
        header.getBoundingClientRect().bottom <= innerHeight * (document.body.classList.contains('mac-size') ? .52 : .36);
    let size = Math.max(floor, parseFloat(getComputedStyle(headline).fontSize));
    headline.style.fontSize = `${size}px`;
    while (!fits() && size > floor) {
        size = Math.max(floor, size - unit);
        headline.style.fontSize = `${size}px`;
    }
    if (!fits()) throw new Error('Headline or caption block overflows at the 88px headline floor.');
    const mac = document.querySelector('.mac');
    if (mac) {
        const image = mac.querySelector('img');
        const gap = 48 * unit;
        const availableHeight = innerHeight - header.getBoundingClientRect().bottom - gap + innerWidth * .03;
        if (availableHeight <= 0) throw new Error('No room remains below the Mac captions.');
        const width = Math.min(image.getBoundingClientRect().width, availableHeight * image.naturalWidth / image.naturalHeight);
        mac.style.setProperty('--w', `${width}px`);
        if (image.getBoundingClientRect().top < header.getBoundingClientRect().bottom + gap - 1)
            throw new Error('Mac window overlaps the caption block.');
    }
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    return JSON.stringify({ fontSize: size, fonts: document.fonts.status });
}
