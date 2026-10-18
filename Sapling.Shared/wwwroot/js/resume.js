// Resume editor: the LaTeX text lives here, not in .NET, so keystrokes never cross the SignalR circuit.
// .NET pulls the text with getText() when it saves, compiles or asks the AI, and pushes PDF bytes to showPdf().
(() => {
    // One URL only: importing CodeMirror sub-packages from several URLs loads duplicate copies that refuse to mix.
    const CODEMIRROR_URL = 'https://esm.sh/codemirror@6.0.1';
    const IDLE_MS = 1500;

    let state = null;

    const loadCodeMirror = () => Promise.race([
        import(CODEMIRROR_URL),
        new Promise((_, reject) => setTimeout(() => reject(new Error('timeout')), 8000)),
    ]);

    function notify(method, ...args) {
        if (!state?.ref) return;
        state.ref.invokeMethodAsync(method, ...args).catch(() => { /* circuit gone */ });
    }

    function changed() {
        if (!state || state.silent) return;
        if (!state.dirty) {
            state.dirty = true;
            notify('OnDirty');
        }
        clearTimeout(state.idle);
        state.idle = setTimeout(() => notify('OnIdle'), IDLE_MS);
    }

    function onKey(e) {
        if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') {
            e.preventDefault();
            clearTimeout(state?.idle);
            notify('OnSaveRequested');
        }
    }

    function onBeforeUnload(e) {
        if (state?.dirty) {
            e.preventDefault();
            e.returnValue = '';
        }
    }

    function mountTextarea(host, text) {
        const area = document.createElement('textarea');
        area.className = 'rs-plain';
        area.spellcheck = false;
        area.value = text;
        area.setAttribute('aria-label', 'LaTeX source');
        area.addEventListener('input', changed);
        area.addEventListener('keydown', e => {
            if (e.key === 'Tab' && !e.shiftKey) {
                e.preventDefault();
                area.setRangeText('  ', area.selectionStart, area.selectionEnd, 'end');
                changed();
            }
        });
        host.appendChild(area);
        return {
            get: () => area.value,
            set: t => { area.value = t; },
            destroy: () => area.remove(),
        };
    }

    async function mountCodeMirror(host, text) {
        const { EditorView, basicSetup } = await loadCodeMirror();
        const view = new EditorView({
            doc: text,
            parent: host,
            extensions: [
                basicSetup,
                EditorView.lineWrapping,
                EditorView.contentAttributes.of({ 'aria-label': 'LaTeX source', spellcheck: 'false' }),
                EditorView.updateListener.of(u => { if (u.docChanged) changed(); }),
                EditorView.domEventHandlers({
                    keydown(e, v) {
                        if (e.key === 'Tab' && !e.shiftKey && !e.ctrlKey && !e.altKey) {
                            e.preventDefault();
                            v.dispatch(v.state.replaceSelection('  '));
                            return true;
                        }
                        return false;
                    },
                }),
            ],
        });
        return {
            get: () => view.state.doc.toString(),
            // One transaction, so the editor's own undo (Ctrl+Z) can step back over an AI change too.
            set: t => view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: t } }),
            destroy: () => view.destroy(),
        };
    }

    function setPdf(bytes) {
        if (!state) return;
        const blob = new Blob([bytes], { type: 'application/pdf' });
        const url = URL.createObjectURL(blob);
        if (!state.frame) {
            state.frame = document.createElement('iframe');
            state.frame.title = 'Resume PDF preview';
            state.previewHost.appendChild(state.frame);
        }
        state.frame.src = url + '#view=FitH';
        if (state.pdfUrl) {
            const old = state.pdfUrl;
            setTimeout(() => URL.revokeObjectURL(old), 2000);
        }
        state.pdfUrl = url;
        state.pdfBlob = blob;
    }

    function save(blob, fileName) {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }

    // Resizable panes. Widths are kept as fr weights (the panes' pixel widths), so the split keeps its
    // proportions when the window changes size, and they are remembered per browser.
    const PANES_KEY = 'sapling-resume-panes';
    const MIN_PANE = 240;
    const HANDLE = 12;

    const paneEls = main => [...main.querySelectorAll(':scope > .rs-pane')];

    function applyWidths(main, widths) {
        const [a, b, c] = widths.map(w => Math.max(1, Math.round(w)));
        main.style.setProperty('--rs-cols', `minmax(0, ${a}fr) ${HANDLE}px minmax(0, ${b}fr) ${HANDLE}px minmax(0, ${c}fr)`);
    }

    function saveWidths(widths) {
        try { localStorage.setItem(PANES_KEY, JSON.stringify(widths.map(Math.round))); } catch { /* private mode */ }
    }

    /** Moves the border between pane i and pane i+1 by delta pixels, from the given starting widths. */
    function resized(start, i, delta) {
        const widths = [...start];
        const pair = start[i] + start[i + 1];
        const left = Math.min(Math.max(start[i] + delta, MIN_PANE), pair - MIN_PANE);
        widths[i] = left;
        widths[i + 1] = pair - left;
        return widths;
    }

    function initResizers(main) {
        if (!main || main.dataset.resizers) return;
        main.dataset.resizers = 'on';

        try {
            const saved = JSON.parse(localStorage.getItem(PANES_KEY) ?? 'null');
            if (Array.isArray(saved) && saved.length === 3 && saved.every(n => n > 0)) applyWidths(main, saved);
        } catch { /* ignore bad data */ }

        const current = () => paneEls(main).map(p => p.getBoundingClientRect().width);

        main.querySelectorAll('.rs-resizer').forEach(handle => {
            const i = Number(handle.dataset.resizer);

            handle.addEventListener('pointerdown', e => {
                if (e.button !== 0) return;
                e.preventDefault();
                const start = current();
                const x0 = e.clientX;
                let latest = start;
                try { handle.setPointerCapture(e.pointerId); } catch { /* pointer already gone */ }
                main.classList.add('rs-dragging');

                const onMove = ev => {
                    latest = resized(start, i, ev.clientX - x0);
                    applyWidths(main, latest);
                };
                const onUp = () => {
                    handle.removeEventListener('pointermove', onMove);
                    handle.removeEventListener('pointerup', onUp);
                    handle.removeEventListener('pointercancel', onUp);
                    main.classList.remove('rs-dragging');
                    saveWidths(latest);
                };
                handle.addEventListener('pointermove', onMove);
                handle.addEventListener('pointerup', onUp);
                handle.addEventListener('pointercancel', onUp);
            });

            // Arrow keys nudge the border (Shift for bigger steps); double-click resets the layout.
            handle.addEventListener('keydown', e => {
                if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return;
                e.preventDefault();
                const step = (e.shiftKey ? 80 : 20) * (e.key === 'ArrowLeft' ? -1 : 1);
                const widths = resized(current(), i, step);
                applyWidths(main, widths);
                saveWidths(widths);
            });

            handle.addEventListener('dblclick', () => {
                main.style.removeProperty('--rs-cols');
                try { localStorage.removeItem(PANES_KEY); } catch { /* private mode */ }
            });
        });
    }

    const api = {
        initResizers,

        /** Returns "code" when CodeMirror loaded, "plain" when it fell back to a textarea. */
        async mount(editorHost, previewHost, text, dotnetRef) {
            api.destroy();
            state = { ref: dotnetRef, previewHost, dirty: false, silent: false, idle: 0, frame: null, pdfUrl: null, pdfBlob: null, editor: null, host: editorHost };
            editorHost.addEventListener('keydown', onKey, true);
            window.addEventListener('beforeunload', onBeforeUnload);

            let kind = 'code';
            let editor;
            try {
                editor = await mountCodeMirror(editorHost, text);
            } catch {
                kind = 'plain';
                editorHost.replaceChildren();
                editor = mountTextarea(editorHost, text);
            }

            if (!state) {
                editor.destroy();
                return kind;
            }

            state.editor = editor;
            return kind;
        },

        getText() {
            return state?.editor ? state.editor.get() : null;
        },

        /** Replaces the text without reporting it as an unsaved change: the server already has it. */
        setText(text) {
            if (!state?.editor) return;
            state.silent = true;
            try { state.editor.set(text); } finally { state.silent = false; }
        },

        /** Blocks typing while the AI works on the text, so nothing typed in the meantime is overwritten. */
        setLocked(locked) {
            if (!state?.host) return;
            state.host.inert = !!locked;
        },

        markClean() {
            if (!state) return;
            state.dirty = false;
            clearTimeout(state.idle);
        },

        showPdf(bytes) {
            setPdf(bytes);
        },

        hasPdf() {
            return !!state?.pdfBlob;
        },

        download(kind, fileName) {
            if (!state) return false;
            if (kind === 'pdf') {
                if (!state.pdfBlob) return false;
                save(state.pdfBlob, fileName + '.pdf');
                return true;
            }
            const text = api.getText() ?? '';
            save(new Blob([text], { type: 'application/x-tex;charset=utf-8' }), fileName + '.tex');
            return true;
        },

        destroy() {
            if (!state) return;
            clearTimeout(state.idle);
            state.host?.removeEventListener('keydown', onKey, true);
            window.removeEventListener('beforeunload', onBeforeUnload);
            try { state.editor?.destroy(); } catch { /* already gone */ }
            if (state.pdfUrl) URL.revokeObjectURL(state.pdfUrl);
            state.frame?.remove();
            state = null;
        },
    };

    window.sapling = window.sapling || {};
    window.sapling.resume = api;
})();
