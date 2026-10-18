window.sapling = (() => {
    const THEME_KEY = 'sapling-theme';

    const darkQuery = window.matchMedia ? matchMedia('(prefers-color-scheme: dark)') : null;

    const theme = {
        // data-sapling-theme is the choice (light | dark | system); data-sapling-mode is what it resolves to.
        apply(mode) {
            const choice = mode || 'system';
            const dark = choice === 'dark' || (choice === 'system' && !!darkQuery?.matches);
            document.documentElement.setAttribute('data-sapling-theme', choice);
            document.documentElement.setAttribute('data-sapling-mode', dark ? 'dark' : 'light');
        },
        read() {
            try { return localStorage.getItem(THEME_KEY) || 'system'; } catch { return 'system'; }
        },
        write(mode) {
            try { localStorage.setItem(THEME_KEY, mode); } catch { /* private mode */ }
            theme.apply(mode);
        },
        // What is on screen right now: 'dark' or 'light'.
        resolved() {
            return document.documentElement.getAttribute('data-sapling-mode') === 'dark' ? 'dark' : 'light';
        }
    };

    // Moving between the sign-in pages swaps the page in place, which resets <html> to what the server sent
    // and drops the resolved theme, so the page flipped to light. Put it back the moment it goes missing.
    new MutationObserver(() => {
        const root = document.documentElement;
        const choice = theme.read();
        if (!root.hasAttribute('data-sapling-mode') || root.getAttribute('data-sapling-theme') !== choice) {
            theme.apply(choice);
        }
    }).observe(document.documentElement, { attributes: true, attributeFilter: ['data-sapling-mode', 'data-sapling-theme'] });

    // While "System" is chosen, follow the OS if it switches during the session.
    darkQuery?.addEventListener('change', () => {
        if (document.documentElement.getAttribute('data-sapling-theme') === 'system') theme.apply('system');
    });

    const store = {
        get(key) {
            try { return localStorage.getItem(key); } catch { return null; }
        },
        set(key, value) {
            try { localStorage.setItem(key, value); } catch { /* private mode */ }
        },
        remove(key) {
            try { localStorage.removeItem(key); } catch { /* private mode */ }
        }
    };

    const carouselInstances = new WeakMap();

    const getSlideOffset = (track, domIndex) => {
        if (!track) return 0;
        const slides = track.querySelectorAll('.carousel-slide');
        if (!slides || slides.length === 0 || !slides[domIndex]) return 0;
        return slides[domIndex].offsetLeft - slides[0].offsetLeft;
    };

    const getStep = (track) => {
        if (!track) return 0;
        const slides = track.querySelectorAll('.carousel-slide');
        if (slides && slides.length >= 2) {
            const step = slides[1].offsetLeft - slides[0].offsetLeft;
            if (step > 0) return step;
        }
        const first = track.firstElementChild;
        return first ? first.getBoundingClientRect().width + 12 : 0;
    };

    const silentJump = (track, domIndex, state) => {
        if (!track) return;
        if (state) state.isJumping = true;
        const targetLeft = getSlideOffset(track, domIndex);
        const prevBehavior = track.style.scrollBehavior;
        const prevSnap = track.style.scrollSnapType;
        track.style.scrollBehavior = 'auto';
        track.style.scrollSnapType = 'none';
        try {
            track.scrollTo({ left: targetLeft, behavior: 'instant' });
        } catch {
            track.scrollLeft = targetLeft;
        }
        track.scrollLeft = targetLeft;
        void track.offsetWidth;
        track.style.scrollBehavior = prevBehavior;
        track.style.scrollSnapType = prevSnap;
        if (state) {
            setTimeout(() => { state.isJumping = false; }, 60);
        }
    };

    const scrollToSlide = (track, domIndex) => {
        if (!track) return;
        const targetLeft = getSlideOffset(track, domIndex);
        track.scrollTo({ left: targetLeft, behavior: 'smooth' });
    };

    const carousel = {
        init(track, dotNetRef, count) {
            if (!track || !count || count <= 1) return;

            carousel.destroy(track);

            const state = {
                track,
                dotNetRef,
                count,
                currentDomIndex: 1,
                isJumping: false,
                scrollTimer: null,
                frame: 0,
                cleanups: [],
                resizeObserver: null,
            };
            carouselInstances.set(track, state);

            track.classList.add('carousel-ready');
            silentJump(track, 1, state);

            track.querySelectorAll('.carousel-slide[aria-hidden="true"] a, .carousel-slide[aria-hidden="true"] button')
                .forEach(el => el.setAttribute('tabindex', '-1'));

            const onScrollEnd = () => {
                if (state.isJumping) return;
                const step = getStep(track);
                if (step <= 0) return;

                const domIndex = Math.round(track.scrollLeft / step);

                if (domIndex <= 0) {
                    silentJump(track, count, state);
                    state.currentDomIndex = count;
                    dotNetRef.invokeMethodAsync('OnScrolled', count - 1);
                } else if (domIndex >= count + 1) {
                    silentJump(track, 1, state);
                    state.currentDomIndex = 1;
                    dotNetRef.invokeMethodAsync('OnScrolled', 0);
                } else {
                    state.currentDomIndex = domIndex;
                    const logicalIndex = domIndex - 1;
                    if (logicalIndex >= 0 && logicalIndex < count) {
                        dotNetRef.invokeMethodAsync('OnScrolled', logicalIndex);
                    }
                }
            };

            let isScrollEnding = false;
            const handleScrollEnd = () => {
                if (isScrollEnding || state.isJumping) return;
                isScrollEnding = true;
                clearTimeout(state.scrollTimer);
                requestAnimationFrame(() => {
                    onScrollEnd();
                    isScrollEnding = false;
                });
            };

            if ('onscrollend' in window) {
                track.addEventListener('scrollend', handleScrollEnd);
                state.cleanups.push(() => track.removeEventListener('scrollend', handleScrollEnd));
            }

            const scrollHandler = () => {
                if (state.isJumping) return;
                clearTimeout(state.scrollTimer);
                state.scrollTimer = setTimeout(handleScrollEnd, 120);

                cancelAnimationFrame(state.frame);
                state.frame = requestAnimationFrame(() => {
                    if (state.isJumping) return;
                    const step = getStep(track);
                    if (step <= 0) return;
                    const rawIndex = Math.round(track.scrollLeft / step);
                    const logicalIndex = ((rawIndex - 1) % count + count) % count;
                    if (logicalIndex >= 0 && logicalIndex < count) {
                        dotNetRef.invokeMethodAsync('OnScrolled', logicalIndex);
                    }
                });
            };

            track.addEventListener('scroll', scrollHandler, { passive: true });
            state.cleanups.push(() => track.removeEventListener('scroll', scrollHandler));

            const pauseEvents = ['pointerdown', 'touchstart', 'mouseenter'];
            const resumeEvents = ['pointerup', 'touchend', 'mouseleave'];

            const onPause = () => dotNetRef.invokeMethodAsync('OnInteraction', true);
            const onResume = () => dotNetRef.invokeMethodAsync('OnInteraction', false);

            pauseEvents.forEach(e => {
                track.addEventListener(e, onPause, { passive: true });
                state.cleanups.push(() => track.removeEventListener(e, onPause));
            });

            resumeEvents.forEach(e => {
                track.addEventListener(e, onResume, { passive: true });
                state.cleanups.push(() => track.removeEventListener(e, onResume));
            });

            if (typeof ResizeObserver !== 'undefined') {
                state.resizeObserver = new ResizeObserver(() => {
                    const step = getStep(track);
                    if (step > 0) {
                        silentJump(track, state.currentDomIndex, state);
                    }
                });
                state.resizeObserver.observe(track);
            }
        },

        next(track) {
            const state = carouselInstances.get(track);
            if (!state || state.count <= 1) return;

            const count = state.count;
            const step = getStep(track);

            if (state.currentDomIndex >= count + 1 || (step > 0 && track.scrollLeft >= getSlideOffset(track, count) + (step / 2))) {
                silentJump(track, 1, state);
                state.currentDomIndex = 1;
            }

            const targetDomIndex = state.currentDomIndex + 1;
            state.currentDomIndex = targetDomIndex;
            scrollToSlide(track, targetDomIndex);
        },

        prev(track) {
            const state = carouselInstances.get(track);
            if (!state || state.count <= 1) return;

            const count = state.count;
            const step = getStep(track);

            if (state.currentDomIndex <= 0 || (step > 0 && track.scrollLeft <= getSlideOffset(track, 1) - (step / 2))) {
                silentJump(track, count, state);
                state.currentDomIndex = count;
            }

            const targetDomIndex = state.currentDomIndex - 1;
            state.currentDomIndex = targetDomIndex;
            scrollToSlide(track, targetDomIndex);
        },

        goTo(track, logicalIndex) {
            const state = carouselInstances.get(track);
            if (!state || state.count <= 1) return;

            const count = state.count;
            const target = ((logicalIndex % count) + count) % count;

            if (state.currentDomIndex >= count + 1) {
                silentJump(track, 1, state);
            } else if (state.currentDomIndex <= 0) {
                silentJump(track, count, state);
            }

            const targetDomIndex = target + 1;
            state.currentDomIndex = targetDomIndex;
            scrollToSlide(track, targetDomIndex);
        },

        destroy(track) {
            if (!track) return;
            const state = carouselInstances.get(track);
            if (!state) return;

            if (state.resizeObserver) {
                state.resizeObserver.disconnect();
            }
            clearTimeout(state.scrollTimer);
            cancelAnimationFrame(state.frame);
            state.cleanups.forEach(fn => fn());
            carouselInstances.delete(track);
        },

        observe(track, dotNetRef) {
            const count = track ? track.querySelectorAll('.carousel-slide:not(.carousel-clone-prev):not(.carousel-clone-next)').length : 0;
            carousel.init(track, dotNetRef, count);
        }
    };

    const sidebar = {
        init(sidebarEl, handle, dotNetRef) {
            if (!sidebarEl || !handle) return;

            const COLLAPSED_W = parseInt(getComputedStyle(document.documentElement).getPropertyValue('--sidebar-w-collapsed')) || 68;
            const EXPANDED_W = parseInt(getComputedStyle(document.documentElement).getPropertyValue('--sidebar-w')) || 240;
            const SNAP_THRESHOLD = (COLLAPSED_W + EXPANDED_W) / 2;
            const MIN_W = COLLAPSED_W;
            const MAX_W = 400;

            let startX = 0;
            let startW = 0;
            let dragging = false;

            const onPointerDown = (e) => {
                e.preventDefault();
                dragging = true;
                startX = e.clientX;
                startW = sidebarEl.getBoundingClientRect().width;
                sidebarEl.classList.add('dragging');
                handle.classList.add('active');
                handle.setPointerCapture(e.pointerId);
                document.body.style.cursor = 'col-resize';
                document.body.style.userSelect = 'none';
            };

            const onPointerMove = (e) => {
                if (!dragging) return;
                const dx = e.clientX - startX;
                const newW = Math.max(MIN_W, Math.min(MAX_W, startW + dx));
                sidebarEl.style.width = newW + 'px';
            };

            const onPointerUp = (e) => {
                if (!dragging) return;
                dragging = false;
                sidebarEl.classList.remove('dragging');
                handle.classList.remove('active');
                document.body.style.cursor = '';
                document.body.style.userSelect = '';

                const finalW = sidebarEl.getBoundingClientRect().width;
                sidebarEl.style.width = '';

                if (finalW <= SNAP_THRESHOLD) {
                    dotNetRef.invokeMethodAsync('SetCollapsed', true);
                } else {
                    dotNetRef.invokeMethodAsync('SetCollapsed', false);
                }
            };

            handle.addEventListener('pointerdown', onPointerDown);
            handle.addEventListener('pointermove', onPointerMove);
            handle.addEventListener('pointerup', onPointerUp);
            handle.addEventListener('pointercancel', onPointerUp);
        }
    };

    const avatarEditor = (() => {
        let canvas = null;
        let ctx = null;
        let img = null;
        let scale = 1.0;
        let minScale = 1.0;
        let maxScale = 4.0;
        let posX = 0;
        let posY = 0;
        let rotationDeg = 0;
        let isDragging = false;
        let startPointerX = 0;
        let startPointerY = 0;
        let startPosX = 0;
        let startPosY = 0;
        const activeTouches = new Map();
        let initialPinchDist = 0;
        let initialPinchScale = 1.0;
        let dotNetRef = null;
        let showGrid = false;
        let cropRadius = 120;
        let canvasWidth = 320;
        let canvasHeight = 320;

        function getDistance(t1, t2) {
            const dx = t1.clientX - t2.clientX;
            const dy = t1.clientY - t2.clientY;
            return Math.sqrt(dx * dx + dy * dy);
        }

        function clampOffsets() {
            if (!img) return;
            const isQuarter = (Math.abs(rotationDeg) / 90) % 2 === 1;
            const imgW = (isQuarter ? img.height : img.width) * scale;
            const imgH = (isQuarter ? img.width : img.height) * scale;

            const maxOffsetX = Math.max(0, (imgW / 2) - cropRadius);
            const maxOffsetY = Math.max(0, (imgH / 2) - cropRadius);

            posX = Math.max(-maxOffsetX, Math.min(maxOffsetX, posX));
            posY = Math.max(-maxOffsetY, Math.min(maxOffsetY, posY));
        }

        function draw() {
            if (!canvas || !ctx || !img) return;

            const dpr = window.devicePixelRatio || 1;
            const cx = canvas.width / 2;
            const cy = canvas.height / 2;

            ctx.clearRect(0, 0, canvas.width, canvas.height);

            // Draw image transformed
            ctx.save();
            ctx.translate(cx + posX * dpr, cy + posY * dpr);
            ctx.rotate((rotationDeg * Math.PI) / 180);
            ctx.scale(scale * dpr, scale * dpr);
            ctx.drawImage(img, -img.width / 2, -img.height / 2);
            ctx.restore();

            // WhatsApp dark overlay outside circle
            ctx.save();
            ctx.fillStyle = 'rgba(0, 0, 0, 0.65)';
            ctx.beginPath();
            ctx.rect(0, 0, canvas.width, canvas.height);
            ctx.arc(cx, cy, cropRadius * dpr, 0, Math.PI * 2, true);
            ctx.fill();

            // Circular border
            ctx.beginPath();
            ctx.arc(cx, cy, cropRadius * dpr, 0, Math.PI * 2);
            ctx.strokeStyle = 'rgba(255, 255, 255, 0.85)';
            ctx.lineWidth = 2 * dpr;
            ctx.stroke();

            // Subtle 3x3 grid when dragging/interacting
            if (showGrid) {
                ctx.strokeStyle = 'rgba(255, 255, 255, 0.3)';
                ctx.lineWidth = 1 * dpr;
                const r = cropRadius * dpr;
                for (let i of [-1 / 3, 1 / 3]) {
                    const x = cx + i * r;
                    const chordHalf = Math.sqrt(Math.max(0, r * r - (i * r) * (i * r)));
                    ctx.beginPath();
                    ctx.moveTo(x, cy - chordHalf);
                    ctx.lineTo(x, cy + chordHalf);
                    ctx.stroke();

                    const y = cy + i * r;
                    ctx.beginPath();
                    ctx.moveTo(cx - chordHalf, y);
                    ctx.lineTo(cx + chordHalf, y);
                    ctx.stroke();
                }
            }
            ctx.restore();
        }

        function computeFitScale() {
            if (!img) return 1.0;
            const isQuarter = (Math.abs(rotationDeg) / 90) % 2 === 1;
            const w = isQuarter ? img.height : img.width;
            const h = isQuarter ? img.width : img.height;
            const diameter = cropRadius * 2;
            const scaleX = diameter / w;
            const scaleY = diameter / h;
            return Math.max(scaleX, scaleY);
        }

        function onPointerDown(e) {
            if (!canvas) return;
            canvas.setPointerCapture(e.pointerId);
            activeTouches.set(e.pointerId, { clientX: e.clientX, clientY: e.clientY });

            if (activeTouches.size === 1) {
                isDragging = true;
                showGrid = true;
                startPointerX = e.clientX;
                startPointerY = e.clientY;
                startPosX = posX;
                startPosY = posY;
            } else if (activeTouches.size === 2) {
                isDragging = false;
                const touches = Array.from(activeTouches.values());
                initialPinchDist = getDistance(touches[0], touches[1]);
                initialPinchScale = scale;
            }
            draw();
        }

        function onPointerMove(e) {
            if (!canvas) return;
            if (activeTouches.has(e.pointerId)) {
                activeTouches.set(e.pointerId, { clientX: e.clientX, clientY: e.clientY });
            }

            if (activeTouches.size === 2) {
                const touches = Array.from(activeTouches.values());
                const dist = getDistance(touches[0], touches[1]);
                if (initialPinchDist > 0) {
                    const factor = dist / initialPinchDist;
                    setZoom(initialPinchScale * factor, false);
                }
            } else if (isDragging) {
                const dx = e.clientX - startPointerX;
                const dy = e.clientY - startPointerY;
                posX = startPosX + dx;
                posY = startPosY + dy;
                clampOffsets();
                draw();
            }
        }

        function onPointerUp(e) {
            if (!canvas) return;
            try { canvas.releasePointerCapture(e.pointerId); } catch (_) {}
            activeTouches.delete(e.pointerId);

            if (activeTouches.size === 1) {
                const remaining = Array.from(activeTouches.values())[0];
                startPointerX = remaining.clientX;
                startPointerY = remaining.clientY;
                startPosX = posX;
                startPosY = posY;
                isDragging = true;
            } else if (activeTouches.size === 0) {
                isDragging = false;
                showGrid = false;
                clampOffsets();
                draw();
            }
        }

        function onWheel(e) {
            e.preventDefault();
            const delta = e.deltaY < 0 ? 0.1 : -0.1;
            setZoom(scale * (1 + delta));
        }

        function setZoom(newScale, notifyDotNet = true) {
            scale = Math.max(minScale, Math.min(maxScale, newScale));
            clampOffsets();
            draw();
            if (notifyDotNet && dotNetRef) {
                const norm = (scale - minScale) / Math.max(0.001, (maxScale - minScale));
                dotNetRef.invokeMethodAsync('OnZoomChanged', Math.round(norm * 100));
            }
        }

        function rotate() {
            rotationDeg = (rotationDeg + 90) % 360;
            minScale = computeFitScale();
            scale = Math.max(minScale, scale);
            clampOffsets();
            draw();
        }

        function reset() {
            rotationDeg = 0;
            minScale = computeFitScale();
            scale = minScale;
            posX = 0;
            posY = 0;
            clampOffsets();
            draw();
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('OnZoomChanged', 0);
            }
        }

        let objectUrlToRevoke = null;

        function cleanupObjectUrl() {
            if (objectUrlToRevoke) {
                try { URL.revokeObjectURL(objectUrlToRevoke); } catch (_) {}
                objectUrlToRevoke = null;
            }
        }

        function attachCanvasEvents(canvasEl) {
            canvas = typeof canvasEl === 'string' ? document.getElementById(canvasEl) : canvasEl;
            if (!canvas) return;
            ctx = canvas.getContext('2d');

            const rect = canvas.getBoundingClientRect();
            canvasWidth = rect.width || 300;
            canvasHeight = rect.height || 300;
            const dpr = window.devicePixelRatio || 1;
            canvas.width = canvasWidth * dpr;
            canvas.height = canvasHeight * dpr;
            cropRadius = Math.min(canvasWidth, canvasHeight) * 0.42;

            canvas.removeEventListener('pointerdown', onPointerDown);
            canvas.removeEventListener('pointermove', onPointerMove);
            canvas.removeEventListener('pointerup', onPointerUp);
            canvas.removeEventListener('pointercancel', onPointerUp);
            canvas.removeEventListener('wheel', onWheel);

            canvas.addEventListener('pointerdown', onPointerDown);
            canvas.addEventListener('pointermove', onPointerMove);
            canvas.addEventListener('pointerup', onPointerUp);
            canvas.addEventListener('pointercancel', onPointerUp);
            canvas.addEventListener('wheel', onWheel, { passive: false });

            if (img && img.complete) {
                minScale = computeFitScale();
                scale = Math.max(minScale, scale);
                draw();
            }
        }

        function loadFromUrl(url) {
            img = new Image();
            img.crossOrigin = 'anonymous';
            img.onload = () => {
                rotationDeg = 0;
                minScale = computeFitScale();
                scale = minScale;
                maxScale = minScale * 4.0;
                posX = 0;
                posY = 0;
                if (canvas) {
                    draw();
                }
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync('OnImageLoaded');
                }
            };
            img.src = url;
        }

        function handleFileInput(inputEl) {
            if (!inputEl || !inputEl.files || inputEl.files.length === 0) return;
            const file = inputEl.files[0];
            cleanupObjectUrl();
            objectUrlToRevoke = URL.createObjectURL(file);
            loadFromUrl(objectUrlToRevoke);
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('OnImageSelected');
            }
            try { inputEl.value = ''; } catch (_) {}
        }

        return {
            bind(dotNet) {
                dotNetRef = dotNet;
            },

            handleFileInput,

            initCanvas(canvasEl) {
                attachCanvasEvents(canvasEl);
            },

            init(canvasEl, imageUrl, dotNet) {
                dotNetRef = dotNet;
                if (imageUrl) {
                    loadFromUrl(imageUrl);
                }
                attachCanvasEvents(canvasEl);
            },

            setZoomSlider(percent) {
                const t = Math.max(0, Math.min(100, percent)) / 100.0;
                const target = minScale + t * (maxScale - minScale);
                setZoom(target, false);
            },

            zoomDelta(delta) {
                setZoom(scale + delta * (maxScale - minScale) * 0.1);
            },

            rotate,
            reset,

            getResult(size = 256) {
                if (!img) return null;
                const outCanvas = document.createElement('canvas');
                outCanvas.width = size;
                outCanvas.height = size;
                const outCtx = outCanvas.getContext('2d');

                const cropDiameter = cropRadius * 2;
                const factor = size / cropDiameter;

                outCtx.save();
                outCtx.beginPath();
                outCtx.arc(size / 2, size / 2, size / 2, 0, Math.PI * 2);
                outCtx.clip();

                outCtx.translate(size / 2 + posX * factor, size / 2 + posY * factor);
                outCtx.rotate((rotationDeg * Math.PI) / 180);
                outCtx.scale(scale * factor, scale * factor);
                outCtx.drawImage(img, -img.width / 2, -img.height / 2);
                outCtx.restore();

                // Ultra lightweight WebP with transparency (<20KB), fallback to PNG (<40KB)
                try {
                    const webp = outCanvas.toDataURL('image/webp', 0.85);
                    if (webp && webp.startsWith('data:image/webp')) {
                        return webp;
                    }
                } catch (_) {}

                return outCanvas.toDataURL('image/png');
            },

            destroy() {
                if (canvas) {
                    canvas.removeEventListener('pointerdown', onPointerDown);
                    canvas.removeEventListener('pointermove', onPointerMove);
                    canvas.removeEventListener('pointerup', onPointerUp);
                    canvas.removeEventListener('pointercancel', onPointerUp);
                    canvas.removeEventListener('wheel', onWheel);
                }
                cleanupObjectUrl();
                activeTouches.clear();
                canvas = null;
                ctx = null;
                img = null;
                dotNetRef = null;
            }
        };
    })();

    const scrollToId = (id) => document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });

    const print = () => window.print();

    // On a phone the on-screen keyboard shrinks the screen. While it is up, <html> gets "kb-open" so the tab
    // bar steps aside and only the box being typed in sits above the keyboard.
    (() => {
        if (!window.matchMedia || !matchMedia('(pointer: coarse)').matches) return;
        const root = document.documentElement;
        const height = () => (window.visualViewport ? window.visualViewport.height : window.innerHeight);
        let width = window.innerWidth;
        let full = height();
        const check = () => {
            if (window.innerWidth !== width) {
                // Rotated: measure the full height again.
                width = window.innerWidth;
                full = height();
            }
            const now = height();
            if (now > full) full = now;
            const el = document.activeElement;
            const typing = !!el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable);
            root.classList.toggle('kb-open', typing && full - now > 120);
        };
        window.addEventListener('resize', check);
        window.visualViewport?.addEventListener('resize', check);
        document.addEventListener('focusin', () => setTimeout(check, 60));
        document.addEventListener('focusout', () => setTimeout(check, 60));
    })();

    // Sapling AI chat box: keep the newest message in view and the cursor in the input.
    const chat = {
        toEnd: (el) => { if (el) el.scrollTop = el.scrollHeight; },
        focus: (el) => { if (el && window.matchMedia('(min-width: 768px)').matches) el.focus(); }
    };

    return { theme, store, carousel, sidebar, avatarEditor, scrollToId, print, chat };
})();
