// Voice mock interview, browser side.
// - The interviewer speaks through a natural server voice when one is configured, otherwise the best browser voice.
// - The student answers through the Web Speech recognition API; delivery (response delay, pauses, pace, filler
//   words) is measured here. Only the text of each answer and those numbers go to the server.
// - The camera is a local self-view only: the stream is shown in a <video> element and never sent anywhere.
(() => {
    const root = window.sapling = window.sapling || {};

    const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    const synth = window.speechSynthesis;
    const FILLERS = /\b(um+|uh+|erm+|hmm+|basically|actually|you know|so yeah|i mean)\b/gi;
    const NATURAL = 'natural';

    let dotnet = null;
    let opts = { silenceMs: 2600, pauseMs: 1500, noSpeechMs: 9000, lang: 'en-IN', natural: false };

    // ---------- Small helpers ----------

    const call = (method, ...args) => {
        if (!dotnet) return;
        dotnet.invokeMethodAsync(method, ...args).catch(() => { /* circuit gone */ });
    };

    const store = {
        get: (k) => { try { return localStorage.getItem(k); } catch { return null; } },
        set: (k, v) => { try { localStorage.setItem(k, v); } catch { /* private mode */ } },
    };

    const avatar = () => document.getElementById('interviewer-avatar');
    const setMouth = (n) => { const el = avatar(); if (el) el.setAttribute('data-mouth', String(n)); };

    let audioCtx = null;
    const ensureAudioContext = () => {
        try {
            if (!audioCtx) {
                audioCtx = new (window.AudioContext || window.webkitAudioContext)();
                const sink = store.get('sapling-interview-speaker') || '';
                if (sink && sink !== 'default' && audioCtx.setSinkId) audioCtx.setSinkId(sink).catch(() => { /* device gone */ });
            }
            if (audioCtx.state === 'suspended') audioCtx.resume();
        } catch { audioCtx = null; }
        return audioCtx;
    };

    // ---------- Voices ----------

    // Browser voices vary hugely. Edge's "Online (Natural)" voices are neural and sound human; Chrome's Google
    // voices are decent; the old local Windows voices (Microsoft Heera, Zira, David) sound robotic.
    const scoreVoice = (v) => {
        const name = v.name || '';
        const lang = (v.lang || '').replace('_', '-');
        if (!/^en/i.test(lang)) return -1000;
        let s = 0;
        if (/natural|neural/i.test(name)) s += 100;
        else if (/online/i.test(name)) s += 80;
        if (/google/i.test(name)) s += 55;
        if (!v.localService) s += 10;
        if (/microsoft/i.test(name) && v.localService && !/natural|online/i.test(name)) s -= 40;
        if (/^en-IN/i.test(lang)) s += 25;
        else if (/^en-GB/i.test(lang)) s += 15;
        else if (/^en-US/i.test(lang)) s += 12;
        else s += 8;
        // The server's voice is female, so prefer female browser voices to match it.
        if (/neerja|heera|aashi|ananya|kavya|aria|jenny|sonia|libby|natasha|ava|emma|michelle|zira|hazel|susan|female|samantha|karen|moira|tessa|veena/i.test(name)) s += 20;
        if (/ravi|prabhat|kunal|david|mark|guy|ryan|george|james|william|daniel|male\b/i.test(name)) s -= 20;
        return s;
    };

    const browserVoices = () => (synth ? synth.getVoices() : [])
        .filter(v => scoreVoice(v) > -1000)
        .sort((a, b) => scoreVoice(b) - scoreVoice(a));

    const loadVoices = () => new Promise(resolve => {
        if (!synth) return resolve([]);
        const now = browserVoices();
        if (now.length) return resolve(now);
        const done = () => { synth.removeEventListener('voiceschanged', done); resolve(browserVoices()); };
        synth.addEventListener('voiceschanged', done);
        setTimeout(done, 1500);
    });

    // The voice is chosen for the student, never by them: the server's natural voice when one is set up
    // (the same on every device), otherwise the best voice this browser has.
    const choice = () => (opts.natural ? NATURAL : '');

    // Voices that produced no sound in this session. Online voices fail when the browser's speech service is
    // unreachable, and some installed ones are broken; either way they are skipped from then on.
    const failedVoices = new Set();
    const usableVoices = () => browserVoices().filter(v => !failedVoices.has(v.name));

    const browserVoice = () => {
        const list = usableVoices();
        const name = choice();
        return list.find(v => v.name === name) || list[0] || null;
    };

    // What to try after a voice fails: a voice built into the device first (it needs no network), then any
    // other voice, and finally null, which lets the browser use its own default.
    const nextVoice = () => {
        const list = usableVoices();
        return list.find(v => v.localService) || list[0] || null;
    };

    // ---------- Speaking ----------

    let speakToken = 0;
    let mouthTimer = null;
    let keepAlive = null;
    let mouthFrame = 0;
    let currentAudio = null;
    let speakEndedAt = 0;

    const stopMouth = () => {
        clearInterval(mouthTimer); mouthTimer = null;
        clearInterval(keepAlive); keepAlive = null;
        cancelAnimationFrame(mouthFrame); mouthFrame = 0;
        setMouth(0);
    };

    const chunk = (text) => {
        const parts = (text.match(/[^.!?]+[.!?]*/g) || [text]).map(s => s.trim()).filter(Boolean);
        const out = [];
        for (const p of parts) {
            if (out.length && out[out.length - 1].length < 40) out[out.length - 1] += ' ' + p;
            else out.push(p);
        }
        return out;
    };

    // Natural voice: plays server audio, and opens the mouth in time with how loud the audio actually is.
    const playServer = (url, token, finish) => new Promise((resolve, reject) => {
        fetch(url, { credentials: 'same-origin' })
            .then(r => { if (!r.ok) throw new Error('voice ' + r.status); return r.blob(); })
            .then(blob => {
                if (token !== speakToken) return resolve();
                const src = URL.createObjectURL(blob);
                const audio = new Audio(src);
                currentAudio = audio;
                const cleanup = () => { URL.revokeObjectURL(src); if (currentAudio === audio) currentAudio = null; };
                audio.onended = () => { cleanup(); finish(); resolve(); };
                audio.onerror = () => { cleanup(); reject(new Error('audio')); };

                const ctx = ensureAudioContext();
                if (ctx) {
                    try {
                        const node = ctx.createMediaElementSource(audio);
                        const analyser = ctx.createAnalyser();
                        analyser.fftSize = 512;
                        node.connect(analyser);
                        analyser.connect(ctx.destination);
                        const data = new Uint8Array(analyser.fftSize);
                        const loop = () => {
                            if (token !== speakToken) return;
                            analyser.getByteTimeDomainData(data);
                            let sum = 0;
                            for (let i = 0; i < data.length; i++) { const d = (data[i] - 128) / 128; sum += d * d; }
                            const rms = Math.sqrt(sum / data.length);
                            setMouth(rms < 0.02 ? 0 : rms < 0.06 ? 1 : rms < 0.12 ? 2 : 3);
                            mouthFrame = requestAnimationFrame(loop);
                        };
                        mouthFrame = requestAnimationFrame(loop);
                    } catch {
                        mouthTimer = setInterval(() => setMouth(audio.paused ? 0 : 1 + Math.floor(Math.random() * 3)), 120);
                    }
                } else {
                    mouthTimer = setInterval(() => setMouth(audio.paused ? 0 : 1 + Math.floor(Math.random() * 3)), 120);
                }

                audio.play().then(() => call('OnSpeakStart')).catch(e => { cleanup(); reject(e); });
            })
            .catch(reject);
    });

    // Chrome drops an utterance's events once nothing refers to it, so the one being spoken is kept here.
    let liveUtterance = null;

    // How long a voice gets to start making sound before it is treated as broken. Online voices need a
    // moment to connect; a voice that is still silent after this is not going to speak.
    const VOICE_START_MS = 3500;

    // rescue, when given, plays the server's natural voice; it is tried first if the chosen voice fails.
    const playBrowser = (text, token, finish, rescue) => {
        if (!synth) { setTimeout(finish, 400); return; }
        const pieces = chunk(text);
        let index = 0;
        let started = false;
        let voice = browserVoice();
        let usingDefault = !voice;
        let attempt = 0;
        let startTimer = null;

        // Browser voices rarely report word timing, so the mouth runs on a timer while audio plays.
        mouthTimer = setInterval(() => {
            if (!synth.speaking || synth.paused) { setMouth(0); return; }
            setMouth(1 + Math.floor(Math.random() * 3));
        }, 120);
        // Chrome silently stalls long speech unless nudged.
        keepAlive = setInterval(() => { if (synth.speaking) synth.resume(); }, 8000);

        // Last resort: if nothing at all could speak, move on after a reading-time estimate. The question is
        // also on screen, so the interview carries on.
        const words = text.split(/\s+/).length;
        setTimeout(finish, (words / 2.2) * 1000 + 12000);

        // The current voice made no sound. Remember that, then try the next thing that might work.
        const fallBack = () => {
            if (token !== speakToken) return;
            clearTimeout(startTimer);
            synth.cancel();
            const failed = voice;
            if (failed) failedVoices.add(failed.name);
            attempt++;

            const useBrowser = () => {
                if (token !== speakToken) return;
                if (usingDefault) { finish(); return; }
                voice = nextVoice();
                usingDefault = !voice;
                // A cancel straight before a speak makes Chrome drop the new line, so leave a beat.
                setTimeout(speakPiece, 120);
            };

            if (rescue && attempt === 1 && !started) {
                stopMouth();
                rescue().catch(() => {
                    mouthTimer = setInterval(() => {
                        if (!synth.speaking || synth.paused) { setMouth(0); return; }
                        setMouth(1 + Math.floor(Math.random() * 3));
                    }, 120);
                    useBrowser();
                });
                return;
            }

            useBrowser();
        };

        const speakPiece = () => {
            if (token !== speakToken) return;
            if (index >= pieces.length) { finish(); return; }
            const piece = pieces[index];
            const u = new SpeechSynthesisUtterance(piece);
            liveUtterance = u;
            if (voice) { u.voice = voice; u.lang = voice.lang; } else { u.lang = opts.lang; }
            u.rate = /natural|online|neural/i.test(voice ? voice.name : '') ? 1.0 : 0.97;
            u.pitch = 1.0;
            let spoke = false;
            u.onstart = () => {
                spoke = true;
                clearTimeout(startTimer);
                if (!started) { started = true; call('OnSpeakStart'); }
            };
            u.onend = () => {
                if (u !== liveUtterance) return;
                clearTimeout(startTimer);
                // Some broken voices "finish" at once without ever starting.
                if (!spoke) { fallBack(); return; }
                index++;
                speakPiece();
            };
            u.onerror = (e) => {
                if (u !== liveUtterance) return;
                // Our own cancel() reports as an error; that is not the voice failing.
                if (e && (e.error === 'interrupted' || e.error === 'canceled')) return;
                fallBack();
            };
            clearTimeout(startTimer);
            startTimer = setTimeout(() => { if (u === liveUtterance && !spoke) fallBack(); }, VOICE_START_MS);
            synth.speak(u);
        };

        // Clear anything left over from the previous line, then start after a beat (see fallBack).
        if (synth.speaking || synth.pending) {
            synth.cancel();
            setTimeout(speakPiece, 120);
        } else {
            speakPiece();
        }
    };

    // Speaks the line, then calls OnSpeakEnd. Returns immediately so Blazor never waits on audio.
    // audioUrl is the natural-voice audio for this line, used when that voice is chosen.
    const speak = (text, audioUrl) => {
        stopListening(true);
        stopSpeaking(false);
        const token = ++speakToken;
        const finish = () => {
            if (token !== speakToken) return;
            speakToken++;
            stopMouth();
            speakEndedAt = performance.now();
            call('OnSpeakEnd');
        };

        const canNatural = !!audioUrl && opts.natural;
        const useNatural = canNatural && (choice() === NATURAL || !choice());
        if (useNatural) {
            playServer(audioUrl, token, finish).catch(() => {
                // Natural voice unavailable right now: fall back to the browser voice for this line.
                if (token !== speakToken) return;
                stopMouth();
                playBrowser(text, token, finish, null);
            });
            return;
        }

        // A browser voice was chosen; if it turns out to be broken, the server's voice is the first rescue.
        playBrowser(text || '', token, finish, canNatural ? () => playServer(audioUrl, token, finish) : null);
    };

    const preview = (audioUrl) => {
        speak("Hi, I'm Sapling AI. This is how I'll sound in your interview.", audioUrl);
    };

    const stopSpeaking = (markEnd = true) => {
        speakToken++;
        if (synth) synth.cancel();
        if (currentAudio) { try { currentAudio.pause(); } catch { /* ignore */ } currentAudio = null; }
        stopMouth();
        if (markEnd) speakEndedAt = performance.now();
    };

    // ---------- Devices: microphone, speaker, camera ----------
    // Chosen devices are remembered per browser. Speech recognition listens to the chosen microphone's track
    // (Chrome and Edge 137+); older browsers ignore the track and use the system default microphone.

    // Renamed from 'sapling-interview-mic', which saved the default microphone's real id and so forced every
    // student onto the track path.
    const MIC_KEY = 'sapling-interview-microphone';
    const SPEAKER_KEY = 'sapling-interview-speaker';
    const CAMERA_KEY = 'sapling-interview-camera';

    let micStream = null;
    let micFrame = 0;
    let muted = false;

    const micTrack = () => (micStream ? micStream.getAudioTracks()[0] : null);

    // Level meter: the self-view outline while answering, and any .mic-meter bar (the lobby's mic check).
    const startMeter = (stream) => {
        cancelAnimationFrame(micFrame);
        const ctx = ensureAudioContext();
        if (!ctx || !stream) return;
        try {
            const source = ctx.createMediaStreamSource(stream);
            const analyser = ctx.createAnalyser();
            analyser.fftSize = 512;
            source.connect(analyser);
            const data = new Uint8Array(analyser.fftSize);
            const loop = () => {
                analyser.getByteTimeDomainData(data);
                let sum = 0;
                for (let i = 0; i < data.length; i++) { const d = (data[i] - 128) / 128; sum += d * d; }
                const level = muted ? 0 : Math.min(1, Math.sqrt(sum / data.length) * 6);
                if (listening && !paused && level > 0.25) loudFrames++;
                const tile = document.getElementById('self-tile');
                if (tile) tile.style.setProperty('--lvl', listening && !paused ? level.toFixed(2) : '0');
                document.querySelectorAll('.mic-meter').forEach(el => el.style.setProperty('--lvl', level.toFixed(2)));
                micFrame = requestAnimationFrame(loop);
            };
            micFrame = requestAnimationFrame(loop);
        } catch { /* the meter is cosmetic */ }
    };

    // Opens the chosen (or default) microphone. Asking here, on the lobby, is what lets the browser show
    // device names in the pickers.
    const openMic = async (deviceId) => {
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) return 'unsupported';
        const wanted = deviceId === undefined ? (store.get(MIC_KEY) || '') : deviceId;
        const constraints = (id) => ({
            audio: { deviceId: id ? { exact: id } : undefined, echoCancellation: true, noiseSuppression: true, autoGainControl: true },
        });
        let stream = null;
        try {
            stream = await navigator.mediaDevices.getUserMedia(constraints(wanted));
        } catch (e) {
            if (e && e.name === 'NotAllowedError') return 'not-allowed';
            // The saved device may have been unplugged: fall back to the default one.
            try { stream = await navigator.mediaDevices.getUserMedia(constraints('')); } catch (e2) {
                return e2 && e2.name === 'NotAllowedError' ? 'not-allowed' : 'unavailable';
            }
        }
        if (micStream) micStream.getTracks().forEach(t => t.stop());
        micStream = stream;
        const track = micTrack();
        if (track) track.enabled = !muted;
        // Remember the student's own choice; '' means "system default", which recognition handles itself.
        store.set(MIC_KEY, wanted || '');
        startMeter(micStream);
        return 'ok';
    };

    const setMic = async (deviceId) => {
        trackBroken = false; // a new choice gets a fresh try
        const wasListening = listening && !paused;
        if (wasListening) pauseRecognition();
        const result = await openMic(deviceId || '');
        if (wasListening) resumeRecognition();
        return result;
    };

    const canPickSpeaker = () => !!(window.AudioContext && 'setSinkId' in AudioContext.prototype);

    const applySpeaker = () => {
        const id = store.get(SPEAKER_KEY) || '';
        if (audioCtx && canPickSpeaker()) {
            audioCtx.setSinkId(id === 'default' ? '' : id).catch(() => { /* device gone: stay on default */ });
        }
    };

    const setSpeaker = (deviceId) => {
        store.set(SPEAKER_KEY, deviceId || '');
        ensureAudioContext();
        applySpeaker();
    };

    // A short two-note chime through the chosen speaker.
    const testSpeaker = () => {
        const ctx = ensureAudioContext();
        if (!ctx) return;
        const now = ctx.currentTime;
        [[660, 0], [880, 0.18]].forEach(([freq, at]) => {
            const osc = ctx.createOscillator();
            const gain = ctx.createGain();
            osc.frequency.value = freq;
            gain.gain.setValueAtTime(0.0001, now + at);
            gain.gain.exponentialRampToValueAtTime(0.25, now + at + 0.02);
            gain.gain.exponentialRampToValueAtTime(0.0001, now + at + 0.35);
            osc.connect(gain).connect(ctx.destination);
            osc.start(now + at);
            osc.stop(now + at + 0.4);
        });
    };

    const devices = async () => {
        const result = { mics: [], speakers: [], cameras: [], mic: '', speaker: '', camera: '', canPickSpeaker: canPickSpeaker() };
        if (!navigator.mediaDevices || !navigator.mediaDevices.enumerateDevices) return result;
        const all = await navigator.mediaDevices.enumerateDevices();
        const named = (kind, fallback) => all
            .filter(d => d.kind === kind && d.deviceId !== 'communications')
            .map((d, i) => ({ id: d.deviceId, label: d.label || `${fallback} ${i + 1}` }));
        result.mics = named('audioinput', 'Microphone');
        result.speakers = named('audiooutput', 'Speaker');
        result.cameras = named('videoinput', 'Camera');
        result.mic = store.get(MIC_KEY) || 'default';
        result.speaker = store.get(SPEAKER_KEY) || (result.speakers[0] ? result.speakers[0].id : '');
        const cam = camStream ? camStream.getVideoTracks()[0] : null;
        result.camera = (cam && cam.getSettings && cam.getSettings().deviceId) || store.get(CAMERA_KEY) || (result.cameras[0] ? result.cameras[0].id : '');
        // A remembered device that is no longer plugged in would leave the picker blank.
        const known = (list, id) => (list.some(d => d.id === id) ? id : (list[0] ? list[0].id : ''));
        result.mic = known(result.mics, result.mic);
        result.speaker = known(result.speakers, result.speaker);
        result.camera = known(result.cameras, result.camera);
        return result;
    };

    // ---------- Listening ----------

    let recog = null;
    let listening = false;
    let paused = false;
    let committed = '';
    let sessionFinal = '';
    let interim = '';
    let listenStartedAt = 0;
    let firstSpeechAt = 0;
    let lastSpeechAt = 0;
    let longPauses = 0;
    let longestPauseMs = 0;
    let inPause = false;
    let noSpeechWarned = false;
    let tick = null;
    let lastPush = 0;
    let pushTimer = null;

    // Microphone choice for recognition. Passing a track to start() is new (Chromium 137+) and not every
    // speech service honours it, so it is only used for a microphone the student picked, and abandoned if the
    // meter hears them talking but no words come back.
    let usingTrack = false;
    let trackBroken = false;
    let loudFrames = 0;
    let restarts = [];

    const log = (...args) => { try { console.debug('[sapling.interview]', ...args); } catch { /* ignore */ } };

    const wantsTrack = () => {
        const chosen = store.get(MIC_KEY) || '';
        return !trackBroken && chosen !== '' && chosen !== 'default' && !!micTrack() && micTrack().readyState === 'live';
    };

    const currentText = () => [committed, sessionFinal, interim].filter(Boolean).join(' ').replace(/\s+/g, ' ').trim();

    const pushTranscript = () => {
        const now = performance.now();
        const send = () => { lastPush = performance.now(); pushTimer = null; call('OnTranscript', currentText()); };
        if (now - lastPush > 150) send();
        else if (!pushTimer) pushTimer = setTimeout(send, 150);
    };

    const startRecognition = () => {
        recog = new Recognition();
        recog.lang = opts.lang;
        recog.continuous = true;
        recog.interimResults = true;
        recog.maxAlternatives = 1;

        recog.onresult = (e) => {
            let fin = '';
            let tmp = '';
            for (let i = 0; i < e.results.length; i++) {
                const t = e.results[i][0].transcript;
                if (e.results[i].isFinal) fin += t + ' ';
                else tmp += t;
            }
            sessionFinal = fin.trim();
            interim = tmp.trim();
            if (!sessionFinal && !interim) return;

            const now = performance.now();
            if (!firstSpeechAt) firstSpeechAt = now;
            if (inPause) {
                longestPauseMs = Math.max(longestPauseMs, now - lastSpeechAt);
                inPause = false;
            }
            lastSpeechAt = now;
            pushTranscript();
        };

        recog.onaudiostart = () => log('audio start', usingTrack ? 'chosen microphone' : 'default microphone');
        recog.onspeechstart = () => log('speech start');

        recog.onerror = (e) => {
            log('error', e.error, e.message || '');
            if (e.error === 'no-speech' || e.error === 'aborted') return; // onend restarts
            if (e.error === 'language-not-supported' && opts.lang !== 'en-US') {
                opts.lang = 'en-US'; // onend restarts in a language every engine has
                return;
            }
            if (e.error === 'audio-capture' && usingTrack) {
                trackBroken = true; // onend restarts on the default microphone
                call('OnError', 'mic-fallback', currentText());
                return;
            }
            listening = false;
            call('OnError', e.error, currentText());
        };

        // Chrome ends recognition after about a minute or on hiccups; keep the answer going.
        recog.onend = () => {
            log('end');
            committed = [committed, sessionFinal, interim].filter(Boolean).join(' ');
            sessionFinal = '';
            interim = '';
            if (!listening || paused) return;

            // A session that ends again and again without hearing anything means the engine is not working;
            // report it instead of looping silently.
            const now = performance.now();
            restarts = restarts.filter(t => now - t < 15000);
            restarts.push(now);
            if (restarts.length > 6 && !firstSpeechAt) {
                listening = false;
                call('OnError', 'stalled', currentText());
                return;
            }
            try { startRecognition(); } catch { /* ignore */ }
        };

        usingTrack = wantsTrack();
        if (usingTrack) {
            try { recog.start(micTrack()); log('start', 'chosen microphone'); return; } catch (err) {
                log('start with track failed', err);
                usingTrack = false;
                trackBroken = true;
            }
        }
        recog.start();
        log('start', 'default microphone', opts.lang);
    };

    // The meter hears the student speaking but recognition returns nothing: the engine is ignoring the chosen
    // microphone. Carry on with the default one.
    const checkTrackWorks = (now) => {
        if (!usingTrack || firstSpeechAt || now - listenStartedAt < 4000 || loudFrames < 60) return;
        log('no words from the chosen microphone; using the default microphone');
        trackBroken = true;
        loudFrames = 0;
        if (recog) { try { recog.abort(); } catch { /* onend restarts without the track */ } }
        call('OnError', 'mic-fallback', currentText());
    };

    const pauseRecognition = () => {
        paused = true;
        if (recog) {
            // onend commits the words heard so far and does not restart while paused.
            try { recog.stop(); } catch { /* ignore */ }
        }
    };

    const resumeRecognition = () => {
        paused = false;
        if (!listening) return;
        // Time spent muted is not a hesitation.
        if (firstSpeechAt) lastSpeechAt = performance.now();
        inPause = false;
        noSpeechWarned = false;
        loudFrames = 0;
        restarts = [];
        listenStartedAt = performance.now();
        try { startRecognition(); } catch { /* ignore */ }
    };

    const setMuted = (value) => {
        muted = !!value;
        const track = micTrack();
        if (track) track.enabled = !muted;
        if (muted) pauseRecognition();
        else if (paused) resumeRecognition();
    };

    const onTick = () => {
        if (!listening || paused) return;
        const now = performance.now();
        checkTrackWorks(now);

        if (!firstSpeechAt) {
            if (!noSpeechWarned && now - listenStartedAt > opts.noSpeechMs) {
                noSpeechWarned = true;
                call('OnError', 'no-speech', '');
            }
            return;
        }

        const gap = now - lastSpeechAt;
        if (gap > opts.pauseMs && !inPause) {
            inPause = true;
            longPauses++;
        }
        if (gap > opts.silenceMs && currentText()) {
            // The trailing silence that ends the answer is not a hesitation.
            if (inPause) { longPauses = Math.max(0, longPauses - 1); inPause = false; }
            const result = stopListening(false);
            call('OnAnswer', result.text, result.metrics);
        }
    };

    const listen = () => {
        stopListening(true);
        if (!Recognition) return false;
        committed = ''; sessionFinal = ''; interim = '';
        firstSpeechAt = 0; lastSpeechAt = 0;
        longPauses = 0; longestPauseMs = 0; inPause = false; noSpeechWarned = false;
        loudFrames = 0; restarts = [];
        listenStartedAt = performance.now();
        if (!speakEndedAt) speakEndedAt = listenStartedAt;
        listening = true;
        paused = muted;
        if (!paused) {
            try {
                startRecognition();
            } catch {
                listening = false;
                return false;
            }
        }
        tick = setInterval(onTick, 200);
        return true;
    };

    const metrics = (text) => {
        const words = text ? text.split(/\s+/).filter(Boolean).length : 0;
        const fillers = (text.match(FILLERS) || []).length;
        return {
            responseDelayMs: firstSpeechAt ? Math.max(0, Math.round(firstSpeechAt - speakEndedAt)) : 0,
            speakingMs: firstSpeechAt ? Math.max(0, Math.round(lastSpeechAt - firstSpeechAt)) : 0,
            longPauses: longPauses,
            longestPauseMs: Math.round(longestPauseMs),
            wordCount: words,
            fillerCount: fillers,
            typed: false,
        };
    };

    // Ends the current answer and returns what was heard. Pass discard=true to throw it away.
    const stopListening = (discard) => {
        const wasListening = listening;
        listening = false;
        paused = false;
        clearInterval(tick); tick = null;
        clearTimeout(pushTimer); pushTimer = null;
        if (recog) {
            recog.onend = null;
            try { recog.abort(); } catch { /* ignore */ }
            recog = null;
        }
        const text = currentText();
        const result = { text: wasListening && !discard ? text : '', metrics: metrics(text) };
        committed = ''; sessionFinal = ''; interim = '';
        return result;
    };

    // ---------- Camera (local self-view only) ----------

    let camStream = null;

    const camera = {
        async start(video, deviceId) {
            const wanted = deviceId === undefined || deviceId === null ? (store.get(CAMERA_KEY) || '') : deviceId;
            const constraints = (id) => ({
                video: { deviceId: id ? { exact: id } : undefined, width: { ideal: 1280 }, height: { ideal: 720 }, facingMode: id ? undefined : 'user' },
                audio: false,
            });
            let stream = null;
            try {
                stream = await navigator.mediaDevices.getUserMedia(constraints(wanted));
            } catch (e) {
                if (e && e.name === 'NotAllowedError') return 'not-allowed';
                try { stream = await navigator.mediaDevices.getUserMedia(constraints('')); } catch (e2) {
                    return e2 && e2.name === 'NotAllowedError' ? 'not-allowed' : 'unavailable';
                }
            }
            if (camStream) camStream.getTracks().forEach(t => t.stop());
            camStream = stream;
            const track = camStream.getVideoTracks()[0];
            const settings = track && track.getSettings ? track.getSettings() : {};
            store.set(CAMERA_KEY, settings.deviceId || wanted || '');
            camera.attach(video);
            return 'ok';
        },
        attach(video) {
            if (!video || !camStream) return;
            if (video.srcObject !== camStream) video.srcObject = camStream;
            video.muted = true;
            video.play().catch(() => { /* autoplay of a muted video is always allowed */ });
        },
        stop() {
            if (camStream) camStream.getTracks().forEach(t => t.stop());
            camStream = null;
        },
    };

    // ---------- Timer ----------

    let timerHandle = null;
    const timer = {
        start(elementId, startSeconds) {
            clearInterval(timerHandle);
            const t0 = performance.now() - (startSeconds || 0) * 1000;
            const render = () => {
                const el = document.getElementById(elementId);
                if (!el) return;
                const s = Math.max(0, Math.floor((performance.now() - t0) / 1000));
                const h = Math.floor(s / 3600);
                const m = Math.floor((s % 3600) / 60);
                const sec = s % 60;
                const pad = (n) => String(n).padStart(2, '0');
                el.textContent = (h ? h + ':' + pad(m) : pad(m)) + ':' + pad(sec);
            };
            render();
            timerHandle = setInterval(render, 1000);
        },
        stop() { clearInterval(timerHandle); timerHandle = null; },
    };

    // ---------- Lifecycle ----------

    const capabilities = () => ({
        tts: !!synth,
        stt: !!Recognition,
        secureContext: window.isSecureContext === true,
        camera: !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia),
        narrow: window.matchMedia('(max-width: 899px)').matches,
    });

    const bind = async (ref, options) => {
        dotnet = ref;
        opts = Object.assign({}, opts, options || {});
        await loadVoices();
        return capabilities();
    };

    // Runs inside the Join click: browsers only play audio after a user gesture.
    const unlock = async () => {
        const ctx = ensureAudioContext();
        applySpeaker();
        if (synth) {
            try {
                const warm = new SpeechSynthesisUtterance(' ');
                warm.volume = 0;
                synth.speak(warm);
            } catch { /* ignore */ }
        }
        if (ctx && ctx.state === 'suspended') { try { await ctx.resume(); } catch { /* ignore */ } }
        if (!micStream) return await openMic();
        return 'ok';
    };

    const scrollToEnd = (el) => {
        if (el && el.scrollHeight - el.scrollTop - el.clientHeight > 4) el.scrollTop = el.scrollHeight;
    };

    const destroy = () => {
        stopListening(true);
        stopSpeaking();
        camera.stop();
        timer.stop();
        cancelAnimationFrame(micFrame);
        if (micStream) micStream.getTracks().forEach(t => t.stop());
        micStream = null;
        muted = false;
        dotnet = null;
        speakEndedAt = 0;
    };

    window.addEventListener('beforeunload', () => { try { destroy(); } catch { /* ignore */ } });

    root.interview = {
        capabilities, bind, unlock, preview,
        openMic, setMic, setMuted, setSpeaker, testSpeaker, devices,
        speak, stopSpeaking, listen, stopListening,
        camera, timer, scrollToEnd, destroy,
    };
})();
