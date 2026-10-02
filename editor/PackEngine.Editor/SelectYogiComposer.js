// Explicit user click selects a composer. No draft/history/credentials are read.
(options => new Promise((resolve, reject) => {
    const origin = () => location.origin === 'https://chatgpt.com' && location.href === options.url;
    if (!origin()) { reject(new Error('YOGI_TARGET_CHANGED')); return; }
    const done = (error, node) => {
        document.removeEventListener('click', click, true); document.removeEventListener('keydown', key, true); clearTimeout(timer);
        if (error) reject(new Error(error)); else { node.setAttribute('data-confectory-yogi', options.target); resolve(options.target); }
    };
    const click = event => {
        if (!origin()) { done('YOGI_TARGET_CHANGED'); return; }
        const node = event.target.closest('textarea, [contenteditable="true"]');
        if (!node || !node.isConnected || node.disabled || node.getAttribute('aria-disabled') === 'true' || node.getClientRects().length === 0) return;
        if (!(node.closest('[data-type="unified-composer"], [data-testid="composer"]') || node.closest('form'))) return;
        done(null, node);
    };
    const key = event => { if (event.key === 'Escape') done('YOGI_SELECTION_CANCELLED'); };
    const timer = setTimeout(() => done('YOGI_SELECTION_TIMEOUT: 입력창을 지정하지 않았어. 다시 눌러줘.'), 30000);
    document.addEventListener('click', click, true); document.addEventListener('keydown', key, true);
}))
