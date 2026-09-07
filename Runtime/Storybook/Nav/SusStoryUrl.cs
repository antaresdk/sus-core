using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Sharq.Core.Storybook.Nav
{
    /// <summary>
    /// The only place in the engine that knows what platform it is on (plan §4.6).
    ///
    /// In a WebGL player it talks to the real address bar through the <c>.jslib</c> plugin that
    /// ships INSIDE this assembly (<c>SusStorybookUrl.jslib</c>): <c>history.pushState</c> to
    /// write, <c>popstate</c> + <c>hashchange</c> to read back. The plugin lives here and not in
    /// a WebGL template because templates live outside packages — a buyer does not have ours.
    ///
    /// Everywhere else (Editor, standalone) the same interface writes an internal field, which is
    /// what zone A displays next to the "share" button. That is deliberate: the whole navigation
    /// layer is then exercisable in EditMode, and only the bridge itself needs a browser
    /// (plan §9 risk 11 — the bridge has never been run in a player yet).
    ///
    /// Loop guard: a route that arrived FROM the address bar is delivered through
    /// <see cref="ExternalChanged"/> already marked <see cref="SusStoryRoute.FromUrl"/>, and
    /// <see cref="Push"/> refuses to write such a route back.
    /// </summary>
    public sealed class SusStoryUrl : IDisposable
    {
        static SusStoryUrl _bound;

        string _address = string.Empty;
        bool _disposed;

        /// <summary>Raised when the address changed OUTSIDE the shell: back button, edited URL.</summary>
        public event Action<SusStoryRoute> ExternalChanged;

        public SusStoryUrl()
        {
            _address = ReadAddress();
            Bind();
        }

        /// <summary>Last address written or read, in canonical hash form.</summary>
        public string Address => _address;

        /// <summary>True when this instance is talking to a real browser address bar.</summary>
        public static bool HasBrowser
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// The route the page was opened with, or null. Kept under this name from the pre-engine
        /// shell (plan D11) — <see cref="ParseStoryIdFromAbsoluteUrl"/> is the string-only form
        /// that 85 driver call sites in <c>sus-dev</c> already use.
        /// </summary>
        public static SusStoryRoute ParseRouteFromAbsoluteUrl()
        {
            var url = SafeAbsoluteUrl();
            return SusStoryRoute.TryParse(url, out var route) ? route : null;
        }

        /// <summary>
        /// Story id the page was opened with, or null. Name and behaviour preserved from the
        /// pre-engine shell (plan D11): it reads <c>Application.absoluteURL</c> and understands
        /// both the legacy <c>#story=&lt;id&gt;</c> form and the canonical <c>#/&lt;id&gt;</c> one.
        /// </summary>
        public static string ParseStoryIdFromAbsoluteUrl() => ParseRouteFromAbsoluteUrl()?.StoryId;

        /// <summary>
        /// Writes the route to the address bar without reloading. A route marked
        /// <see cref="SusStoryRoute.FromUrl"/> is NOT written — that is the loop guard, and the
        /// method returns false so a caller can tell "refused" from "nothing to do".
        /// </summary>
        public bool Push(SusStoryRoute route) => Write(route, replace: false);

        /// <summary>Same as <see cref="Push"/> but overwrites the current entry.</summary>
        public bool Replace(SusStoryRoute route) => Write(route, replace: true);

        bool Write(SusStoryRoute route, bool replace)
        {
            if (_disposed || route == null || route.FromUrl) return false;

            var hash = route.ToHash();
            if (string.Equals(hash, _address, StringComparison.Ordinal)) return false;
            _address = hash;

#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                if (replace) SusStorybookUrlReplace(hash);
                else SusStorybookUrlPush(hash);
            }
            catch (Exception e)
            {
                SusLog.Warn("[storybook] address bar write failed: " + e.Message);
            }
#endif
            return true;
        }

        /// <summary>
        /// Copies the current address to the clipboard. Returns false when no clipboard was
        /// reachable, so the caller can say so instead of showing a false "copied".
        /// </summary>
        public bool CopyToClipboard()
        {
            if (string.IsNullOrEmpty(_address)) return false;
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                return SusStorybookUrlCopy(_address) != 0;
            }
            catch (Exception e)
            {
                SusLog.Warn("[storybook] clipboard write failed: " + e.Message);
                return false;
            }
#else
            try
            {
                GUIUtility.systemCopyBuffer = _address;
                return true;
            }
            catch (Exception e)
            {
                SusLog.Warn("[storybook] clipboard write failed: " + e.Message);
                return false;
            }
#endif
        }

        /// <summary>
        /// Feeds an address in as if the browser had reported it. The bridge calls this; a test
        /// calls it too, which is how <c>popstate</c> behaviour is checked without a browser.
        /// </summary>
        public void HandleExternal(string address)
        {
            if (_disposed || string.IsNullOrWhiteSpace(address)) return;
            if (!SusStoryRoute.TryParse(address, out var route)) return;
            _address = route.ToHash();
            ExternalChanged?.Invoke(route.AsFromUrl(true));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (ReferenceEquals(_bound, this)) _bound = null;
            ExternalChanged = null;
        }

        static string SafeAbsoluteUrl()
        {
            try
            {
                return Application.absoluteURL;
            }
            catch (Exception)
            {
                return null;    // absoluteURL is not available on every platform / in every test host
            }
        }

        string ReadAddress()
        {
            var route = ParseRouteFromAbsoluteUrl();
            return route != null ? route.ToHash() : string.Empty;
        }

        void Bind()
        {
            _bound = this;
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                SusStorybookUrlBind(OnBrowserUrlChanged);
            }
            catch (Exception e)
            {
                SusLog.Warn("[storybook] address bar bridge unavailable: " + e.Message);
            }
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        delegate void UrlChangedCallback(IntPtr utf8);

        [DllImport("__Internal")] static extern void SusStorybookUrlBind(UrlChangedCallback callback);
        [DllImport("__Internal")] static extern void SusStorybookUrlPush(string hash);
        [DllImport("__Internal")] static extern void SusStorybookUrlReplace(string hash);
        [DllImport("__Internal")] static extern int SusStorybookUrlCopy(string text);

        [AOT.MonoPInvokeCallback(typeof(UrlChangedCallback))]
        static void OnBrowserUrlChanged(IntPtr utf8)
        {
            try
            {
                _bound?.HandleExternal(ReadUtf8(utf8));
            }
            catch (Exception e)
            {
                SusLog.Warn("[storybook] popstate handler threw: " + e.Message);
            }
        }

        /// <summary>
        /// Decodes a NUL-terminated UTF-8 buffer by hand. <c>Marshal.PtrToStringUTF8</c> is not on
        /// every API compatibility level Unity offers, and <c>PtrToStringAnsi</c> would mangle any
        /// non-ASCII byte a hand-typed address can contain.
        /// </summary>
        static string ReadUtf8(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return null;
            int len = 0;
            while (Marshal.ReadByte(ptr, len) != 0) len++;
            if (len == 0) return string.Empty;
            var bytes = new byte[len];
            Marshal.Copy(ptr, bytes, 0, len);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
#endif
    }
}
