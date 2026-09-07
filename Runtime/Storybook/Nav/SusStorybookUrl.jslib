// Address-bar bridge for the SUS storybook engine (plan ARCH-20260907-STORYBOOK-ENGINE §4.6, D14).
//
// Lives INSIDE the engine assembly on purpose: a WebGL template is a project asset
// (sus-demo/Assets/WebGLTemplates/**), and a buyer opening the storybook in their own project
// does not have ours. Shipping the bridge with the code makes deep links work everywhere the
// engine works.
//
// Contract with SusStoryUrl.cs:
//   SusStorybookUrlPush(hash)      - history.pushState, no reload
//   SusStorybookUrlReplace(hash)   - history.replaceState, no reload
//   SusStorybookUrlBind(callback)  - popstate + hashchange -> callback(utf8 pointer to location.hash)
//   SusStorybookUrlCopy(text)      - clipboard, returns 1 on success and 0 when refused
mergeInto(LibraryManager.library, {

  SusStorybookUrlPush: function (hashPtr) {
    try {
      var hash = UTF8ToString(hashPtr);
      if (window.history && window.history.pushState) window.history.pushState(null, '', hash);
      else window.location.hash = hash;
    } catch (e) { console.warn('[sus-storybook] pushState failed: ' + e); }
  },

  SusStorybookUrlReplace: function (hashPtr) {
    try {
      var hash = UTF8ToString(hashPtr);
      if (window.history && window.history.replaceState) window.history.replaceState(null, '', hash);
      else window.location.hash = hash;
    } catch (e) { console.warn('[sus-storybook] replaceState failed: ' + e); }
  },

  SusStorybookUrlBind: function (callback) {
    try {
      if (window.__susStorybookUrlBound) return;
      window.__susStorybookUrlBound = true;

      var notify = function () {
        try {
          var hash = window.location.hash || '';
          var size = lengthBytesUTF8(hash) + 1;
          var ptr = _malloc(size);
          stringToUTF8(hash, ptr, size);
          {{{ makeDynCall('vi', 'callback') }}}(ptr);
          _free(ptr);
        } catch (e) { console.warn('[sus-storybook] url notify failed: ' + e); }
      };

      window.addEventListener('popstate', notify);
      window.addEventListener('hashchange', notify);
    } catch (e) { console.warn('[sus-storybook] url bind failed: ' + e); }
  },

  SusStorybookUrlCopy: function (textPtr) {
    try {
      var text = UTF8ToString(textPtr);
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text);
        return 1;
      }
      // Fallback for pages served without a secure context: the async Clipboard API is absent
      // there, and a link nobody can copy is the same as no share button at all.
      var ta = document.createElement('textarea');
      ta.value = text;
      ta.setAttribute('readonly', '');
      ta.style.position = 'fixed';
      ta.style.opacity = '0';
      document.body.appendChild(ta);
      ta.select();
      var ok = document.execCommand('copy');
      document.body.removeChild(ta);
      return ok ? 1 : 0;
    } catch (e) {
      console.warn('[sus-storybook] clipboard failed: ' + e);
      return 0;
    }
  }

});
