// Grabs the current video frame at native resolution and posts it to the host as PNG (base64).
// Called by FrameGrabber.cs as (this function)(requestId).
async function (reqId) {
  const post = (msg) => window.chrome.webview.postMessage(Object.assign({ type: 'ytlite-frame', id: reqId }, msg));
  try {
    const candidates = [...document.querySelectorAll('video')].filter(v => v.videoWidth > 0);
    const video =
      document.querySelector('#movie_player video.html5-main-video') ||
      document.querySelector('video.html5-main-video') ||
      candidates.sort((a, b) => b.videoWidth * b.videoHeight - a.videoWidth * a.videoHeight)[0];
    if (!video || !video.videoWidth) throw new Error('No video frame on this page yet (open a video first).');

    const canvas = document.createElement('canvas');
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    canvas.getContext('2d').drawImage(video, 0, 0, canvas.width, canvas.height);

    const blob = await new Promise((resolve, reject) =>
      canvas.toBlob(b => (b ? resolve(b) : reject(new Error('PNG encoding failed'))), 'image/png'));
    const dataUrl = await new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(reader.result);
      reader.onerror = () => reject(reader.error);
      reader.readAsDataURL(blob);
    });

    const url = new URL(location.href);
    const videoId = url.searchParams.get('v') ||
      ((url.pathname.match(/^\/(?:shorts|live|embed)\/([\w-]{6,})/) || [])[1]) || '';
    const title = document.title.replace(/^\(\d+\)\s*/, '').replace(/ - YouTube$/, '') || 'YouTube';

    post({
      ok: true,
      png: dataUrl.slice(dataUrl.indexOf(',') + 1),
      width: canvas.width,
      height: canvas.height,
      time: video.currentTime,
      videoId,
      title,
      pageUrl: location.href,
    });
  } catch (e) {
    post({ ok: false, error: String((e && e.message) || e) });
  }
}
