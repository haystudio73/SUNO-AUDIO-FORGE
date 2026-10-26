// Runs only in the app's dedicated Suno WebView, after an explicit user click.
// Session credentials stay in this page. Only lyric fields cross the native bridge.
(async () => {
    const song = __SONG__, request = __REQUEST__;
    const send = value => window.chrome.webview.postMessage({ request, song, ...value });
    if (location.origin !== 'https://suno.com') return;
    try {
        let token = await window.Clerk?.session?.getToken();
        if (!token) {
            const cookie = document.cookie.split(';').map(v => v.trim()).find(v => v.startsWith('__session='));
            if (cookie) token = decodeURIComponent(cookie.slice('__session='.length));
        }
        if (!token) { send({ error: 'login' }); return; }
        const response = await fetch(`https://studio-api.prod.suno.com/api/gen/${song}/aligned_lyrics/v2/`, {
            headers: { Authorization: `Bearer ${token}` },
            redirect: 'error', signal: AbortSignal.timeout(45000)
        });
        token = null;
        if (!response.ok) { send({ error: response.status === 401 || response.status === 403 ? 'login' : 'http', status: response.status }); return; }
        const reader = response.body.getReader();
        const chunks = []; let size = 0;
        for (;;) {
            const { value, done } = await reader.read();
            if (done) break;
            size += value.length;
            if (size > 1048576) { await reader.cancel(); send({ error: 'size' }); return; }
            chunks.push(value);
        }
        const bytes = new Uint8Array(size); let offset = 0;
        for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
        const data = JSON.parse(new TextDecoder().decode(bytes));
        const rows = data.aligned_lyrics ?? data.data?.aligned_lyrics;
        if (!Array.isArray(rows)) { send({ error: 'empty' }); return; }
        // Explicit projection excludes account data and other API metadata.
        const aligned_lyrics = rows.slice(0, 10000).filter(r => r && typeof r === 'object').map(r => ({
            text: typeof r.text === 'string' ? r.text : '', start_s: r.start_s, end_s: r.end_s,
            words: Array.isArray(r.words) ? r.words.filter(w => w && typeof w === 'object').map(w => ({
                text: typeof w.text === 'string' ? w.text : typeof w.word === 'string' ? w.word : '', start_s: w.start_s, end_s: w.end_s
            })) : []
        }));
        send({ data: { aligned_lyrics } });
    } catch { send({ error: 'network' }); }
})();
