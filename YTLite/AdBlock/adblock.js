// YTLite built-in ad blocking. Injected at document creation, so it runs before YouTube's own scripts.
// window.__ytliteRules = { prunePaths: [...], hideSelectors: [...] } is prepended by AdBlocker.cs.
(() => {
  'use strict';
  if (window.__ytliteAdblock || !/(^|\.)youtube\.com$/.test(location.hostname)) return;
  window.__ytliteAdblock = true;

  const rules = window.__ytliteRules || {};
  const paths = (rules.prunePaths || []).map(p => p.split('.'));
  const hide = rules.hideSelectors || [];

  // ---- 1. Strip ad data from player responses -------------------------------------------
  function pruneOne(obj) {
    for (const path of paths) {
      let cur = obj;
      for (let i = 0; i < path.length - 1 && cur; i++) cur = cur[path[i]];
      const last = path[path.length - 1];
      if (cur && typeof cur === 'object' && last in cur) delete cur[last];
    }
  }
  function prune(obj) {
    if (!obj || typeof obj !== 'object') return obj;
    try {
      const items = Array.isArray(obj) ? obj : [obj];
      for (const item of items) {
        if (!item || typeof item !== 'object') continue;
        pruneOne(item);
        if (item.playerResponse && typeof item.playerResponse === 'object') pruneOne(item.playerResponse);
      }
    } catch (_) { /* never break the page */ }
    return obj;
  }

  // JSON.parse covers fetch().text() and XHR responseText paths.
  const nativeParse = JSON.parse;
  const parse = function (text, reviver) { return prune(nativeParse.call(this, text, reviver)); };
  parse.toString = () => nativeParse.toString();
  JSON.parse = parse;

  // fetch().json() for /youtubei/v1/player and friends.
  const nativeJson = Response.prototype.json;
  Response.prototype.json = function () { return nativeJson.call(this).then(prune); };

  // Inline `var ytInitialPlayerResponse = {...}` on first page load.
  for (const name of ['ytInitialPlayerResponse']) {
    let value;
    try {
      Object.defineProperty(window, name, {
        configurable: true,
        enumerable: true,
        get() { return value; },
        set(v) { value = prune(v); },
      });
    } catch (_) { /* already defined */ }
  }

  if (window.top !== window) return; // the rest is for the main page only

  // ---- 2. Hide ad elements ------------------------------------------------------------------
  if (hide.length) {
    const css = hide.map(s => `${s} { display: none !important; }`).join('\n');
    const addStyle = () => {
      if (document.getElementById('ytlite-adblock')) return;
      const style = document.createElement('style');
      style.id = 'ytlite-adblock';
      style.textContent = css;
      (document.head || document.documentElement).appendChild(style);
    };
    if (document.documentElement) addStyle();
    else document.addEventListener('DOMContentLoaded', addStyle, { once: true });
  }

  // ---- 3. Safety net: fast-forward and skip any ad that still plays -------------------------
  // (Playback rate instead of seeking, so server-stitched ads can't make us skip the real video.)
  const SKIP = '.ytp-skip-ad-button, .ytp-ad-skip-button, .ytp-ad-skip-button-modern, .ytp-ad-skip-button-container button';
  let adActive = false, mutedBefore = false, rateBefore = 1;
  setInterval(() => {
    const player = document.querySelector('#movie_player') || document.querySelector('.html5-video-player');
    if (!player) return;
    const video = player.querySelector('video');
    const isAd = player.classList.contains('ad-showing') || player.classList.contains('ad-interrupting');
    if (isAd) {
      if (!adActive) {
        window.__ytliteAdsSeen = (window.__ytliteAdsSeen || 0) + 1; // ads that got past layers 1–2
        if (video) { mutedBefore = video.muted; rateBefore = video.playbackRate || 1; }
      }
      adActive = true;
      if (video) { video.muted = true; video.playbackRate = 16; }
      const btn = player.querySelector(SKIP);
      if (btn) btn.click();
    } else if (adActive) {
      adActive = false;
      if (video) { video.muted = mutedBefore; video.playbackRate = rateBefore; }
    }
  }, 250);
})();
