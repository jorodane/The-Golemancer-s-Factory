// Explicit DOM fixtures for the composer-only adapter; no ChatGPT session or browser is simulated as real.
const fs = require('fs'), vm = require('vm'), assert = require('assert');
const adapter = fs.readFileSync('editor/Confectory.Editor/ChatComposerAttachment.js', 'utf8');
const selector = fs.readFileSync('editor/Confectory.Editor/SelectYogiComposer.js', 'utf8');
let checks = 0;
const check = (condition, name) => { assert.ok(condition, name); checks++; process.stdout.write('PASS: ' + name + '\n'); };
function fixture() {
    const events = new Map(), attributes = new Map();
    const container = { isConnected: true, querySelectorAll: () => [] };
    const prompt = { id: 'prompt-textarea', tagName: 'DIV', isContentEditable: true, isConnected: true, disabled: false,
        getClientRects: () => [1], closest: () => container, getAttribute: name => attributes.get(name),
        setAttribute: (name, value) => attributes.set(name, value), contains: node => node === prompt,
        dispatchEvent: event => { events.set('paste', event); }, focus: () => events.set('focus', true) };
    Object.defineProperty(prompt, 'textContent', { get: () => { throw new Error('The draft must not be read.'); } });
    const hidden = { ...prompt, getClientRects: () => [] };
    const document = { querySelectorAll: query => query.includes('data-confectory-yogi') ? attributes.size ? [prompt] : [] : [hidden, prompt],
        addEventListener: (name, fn) => events.set(name, fn), removeEventListener: name => events.delete(name) };
    const context = { document, location: { origin: 'https://chatgpt.com', href: 'https://chatgpt.com/c/fixture' },
        getComputedStyle: () => ({ visibility: 'visible', display: 'block' }),
        setTimeout: fn => { events.set('timeout', fn); return 1; }, clearTimeout: () => events.delete('timeout'),
        Uint8Array, atob: text => Buffer.from(text, 'base64').toString('binary'),
        DataTransfer: class { constructor() { this.items = { add: value => { this.file = value; } }; } },
        File: class { constructor(bytes, name, options) { this.bytes = bytes; this.name = name; this.type = options.type; } },
        ClipboardEvent: class { constructor(type, options) { this.type = type; Object.assign(this, options); } },
        fetch: () => { throw new Error('No network/API access is allowed.'); } };
    return { context, prompt, hidden, container, events };
}
const options = { url: 'https://chatgpt.com/c/fixture', name: 'fixture.json', mime: 'application/json' };
function run(code, context, args) { return vm.runInNewContext(code + '(' + JSON.stringify(args) + ')', context); }
(async () => {
    const f = fixture(), prepared = run(adapter, f.context, options);
    check(prepared.status() === 'pending', 'a hidden legacy #prompt-textarea does not mask the visible composer');
    prepared.paste(Buffer.from('EXPLICIT FROZEN ATTACHMENT').toString('base64'));
    check(f.events.get('paste').clipboardData.file.name === options.name, 'fallback dispatches only the approved attachment into the selected composer');
    prepared.focus(); check(f.events.get('focus'), 'successful attachment focus stays on the same composer');
    f.prompt.isConnected = false;
    assert.throws(() => prepared.status(), /YOGI_COMPOSER_CHANGED/); check(true, 'DOM replacement after preparation invalidates the attachment target');
    const navigated = fixture(), staged = run(adapter, navigated.context, options); navigated.context.location.href = 'https://chatgpt.com/c/another';
    assert.throws(() => staged.paste('YQ=='), /YOGI_COMPOSER_CHANGED/); check(true, 'navigation cannot redirect frozen editor data into another chat');
    const otherOrigin = fixture(); otherOrigin.context.location.origin = 'https://example.org';
    assert.throws(() => run(adapter, otherOrigin.context, options), /YOGI_TARGET_CHANGED/); check(true, 'foreign web origins are rejected before composer access');
    const ambiguous = fixture(); ambiguous.prompt.id = ''; ambiguous.hidden.getClientRects = () => [1]; ambiguous.hidden.id = '';
    assert.throws(() => run(adapter, ambiguous.context, options), /YOGI_COMPOSER_AMBIGUOUS/); check(true, 'multiple active candidates request explicit selection instead of guessing');
    const absent = fixture(); absent.context.document.querySelectorAll = () => [];
    assert.throws(() => run(adapter, absent.context, options), /YOGI_COMPOSER_MISSING/); check(true, 'missing composer is a distinct stage and does not fabricate a login diagnosis');
    const manual = fixture(), target = '0123456789abcdef0123456789abcdef';
    const selection = run(selector, manual.context, { ...options, target });
    manual.events.get('click')({ target: { closest: () => manual.prompt } });
    check(await selection === target && !manual.events.has('click') && !manual.events.has('timeout'), 'explicit click selects one composer and releases temporary listeners/timer');
    check(run(adapter, manual.context, { ...options, target }).status() === 'pending', 'manual target selection feeds the same origin-bound attachment adapter');
    const cancelled = fixture(), cancellation = run(selector, cancelled.context, { ...options, target });
    cancelled.events.get('keydown')({ key: 'Escape' });
    await assert.rejects(cancellation, /YOGI_SELECTION_CANCELLED/); check(!cancelled.events.has('click'), 'manual selection cancellation performs no attachment and releases its listener');
    console.log('YOGI_DOM_FIXTURE_PASS ' + checks + ' checks; real web UI, login and upload are separate manual checks.');
})().catch(error => { console.error(error); process.exitCode = 1; });
