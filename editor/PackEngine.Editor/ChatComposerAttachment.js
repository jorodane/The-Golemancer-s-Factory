// This adapter touches only the current composer. It never submits, reads chat history,
// calls ChatGPT's internal APIs, or exports authentication state.
(options => {
    const fail = (code, message) => { throw new Error('YOGI_' + code + ': ' + message); };
    const origin = () => location.origin === 'https://chatgpt.com' && location.href === options.url;
    if (!origin()) fail('TARGET_CHANGED', '대화가 바뀌었어. 첨부할 대화에서 다시 눌러줘.');
    const visible = node => !!node && node.isConnected && node.getClientRects().length > 0 && getComputedStyle(node).visibility !== 'hidden' && getComputedStyle(node).display !== 'none';
    const editable = node => visible(node) && (node.isContentEditable || node.tagName === 'TEXTAREA') && !node.disabled && node.getAttribute('aria-disabled') !== 'true';
    const selector = options.target && /^[a-f0-9]{32}$/.test(options.target) ? '[data-confectory-yogi="' + options.target + '"]' :
        '#prompt-textarea, textarea[data-testid="prompt-textarea"], [contenteditable="true"][data-testid="composer"], [contenteditable="true"][role="textbox"]';
    const candidates = [...document.querySelectorAll(selector)].filter(editable);
    const primary = candidates.filter(node => node.id === 'prompt-textarea');
    const eligible = primary.length === 1 ? primary : candidates;
    if (eligible.length === 0) fail('COMPOSER_MISSING', '현재 웹 화면에서 활성 입력창을 찾지 못했어. 로딩을 기다리거나 ‘입력창 직접 지정’을 눌러줘.');
    if (eligible.length !== 1) fail('COMPOSER_AMBIGUOUS', '입력창 후보가 여러 개야. ‘입력창 직접 지정’을 눌러줘.');
    const prompt = eligible[0];
    const composer = prompt.closest('[data-type="unified-composer"], [data-testid="composer"]') || prompt.closest('form');
    if (!composer) fail('COMPOSER_CONTAINER', '입력창의 첨부 영역을 찾지 못했어. 파일 복사나 끌어 넣기로 첨부해줘.');
    const outsidePrompt = node => node !== prompt && !prompt.contains(node) && !node.contains(prompt);
    const named = () => [...composer.querySelectorAll('[title], [aria-label], [data-filename], img[alt], span, p')].some(node =>
        outsidePrompt(node) && visible(node) && [node.getAttribute('title'), node.getAttribute('aria-label'), node.getAttribute('data-filename'), node.getAttribute('alt'), node.textContent]
            .some(value => value && value.includes(options.name)));
    const pictures = () => [...composer.querySelectorAll('img')].filter(node => outsidePrompt(node) && visible(node) &&
        (node.src.startsWith('blob:') || node.src.startsWith('data:image/') || (node.naturalWidth >= 48 && node.naturalHeight >= 48 && node.width >= 40 && node.height >= 40)));
    const baselinePictures = new Set(pictures().map(node => node.src));
    const accepts = input => !input.accept || input.accept.split(',').some(value => {
        value = value.trim().toLowerCase();
        return value === '*/*' || value === options.mime || (value.endsWith('/*') && options.mime.startsWith(value.slice(0, -1))) ||
            (value.startsWith('.') && options.name.toLowerCase().endsWith(value));
    });
    const inputs = [...composer.querySelectorAll('input[type="file"]')].filter(input => !input.disabled && accepts(input) && input.files.length === 0);
    const input = inputs.length === 1 ? inputs[0] : null;
    let delivered = false;
    const check = () => {
        if (!origin() || !editable(prompt) || !composer.isConnected)
            fail('COMPOSER_CHANGED', '대화 입력창이 바뀌었어. 현재 첨부 목록을 확인해줘.');
    };
    return {
        input,
        status() {
            check();
            return named() || (delivered && options.mime === 'image/png' && pictures().some(node => !baselinePictures.has(node.src))) ? 'visible' : 'pending';
        },
        fileInput() { check(); return input && input.isConnected && !input.disabled && input.files.length === 0 ? input : null; },
        markDelivered() { check(); delivered = true; },
        paste(base64) {
            check();
            const bytes = Uint8Array.from(atob(base64), character => character.charCodeAt(0));
            const transfer = new DataTransfer();
            transfer.items.add(new File([bytes], options.name, { type: options.mime }));
            // A paste event is a fallback for composers which mount the file input only after opening a menu.
            // No clipboard is read or changed, and no text is inserted into the user's draft.
            prompt.dispatchEvent(new ClipboardEvent('paste', { clipboardData: transfer, bubbles: true, cancelable: true, composed: true }));
            delivered = true;
        },
        focus() { check(); prompt.focus(); }
    };
})
