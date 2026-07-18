using System;
using System.Globalization;

namespace Sharq.Core
{
    /// <summary>
    /// Built-in binding converters for pipe syntax in templates (F5):
    /// <c>:text="Hp.Value | format('{0}/{1}', MaxHp.Value)"</c>
    /// <c>:text="Name.Value | upper"</c>
    /// Emitted by the Sharq generator as static calls.
    /// </summary>
    public static class SusBindingConverters
    {
        public static string Format(object value, string format)
        {
            if (format == null) return value?.ToString() ?? "";
            try { return string.Format(CultureInfo.InvariantCulture, format, value); }
            catch { return value?.ToString() ?? ""; }
        }

        public static string Format(string format, params object[] args)
        {
            if (format == null) return "";
            try { return string.Format(CultureInfo.InvariantCulture, format, args ?? Array.Empty<object>()); }
            catch { return format; }
        }

        public static string Upper(object value) =>
            value?.ToString()?.ToUpperInvariant() ?? "";

        public static string Lower(object value) =>
            value?.ToString()?.ToLowerInvariant() ?? "";

        public static string Round(object value, int digits = 0)
        {
            if (value == null) return "";
            try
            {
                var d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return Math.Round(d, digits).ToString(CultureInfo.InvariantCulture);
            }
            catch { return value.ToString(); }
        }

        public static string Truncate(object value, int maxLen)
        {
            var s = value?.ToString() ?? "";
            if (maxLen <= 0 || s.Length <= maxLen) return s;
            return s.Substring(0, maxLen) + "…";
        }
    }
}
