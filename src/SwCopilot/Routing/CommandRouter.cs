using System.Globalization;
using System.Text.RegularExpressions;

namespace SwCopilot.Routing
{
    /// <summary>
    /// Deterministic, hard-coded command routing (spec section 6). No LLM.
    /// Maps free text to one of the three supported command objects, or Unsupported.
    /// </summary>
    public static class CommandRouter
    {
        public static CommandBase Route(string input)
        {
            if (string.IsNullOrEmpty(input) || input.Trim().Length == 0)
                return new UnsupportedCommand { RawInput = input };

            string text = input.Trim();
            string lower = text.ToLowerInvariant();

            // 1) Create mounting plate.
            if (text.Contains("安装板") || lower.Contains("mounting plate") || lower.Contains("base plate"))
            {
                var cmd = new CreateMountingPlateCommand { RawInput = input };

                // Optional: parse an explicit WxHxT, e.g. "100x60x8" / "100*60*8" / "100×60×8".
                Match m = Regex.Match(lower,
                    @"(\d+(?:\.\d+)?)\s*[x×*]\s*(\d+(?:\.\d+)?)\s*[x×*]\s*(\d+(?:\.\d+)?)");
                if (m.Success)
                {
                    cmd.WidthMm = ParseD(m.Groups[1].Value, cmd.WidthMm);
                    cmd.HeightMm = ParseD(m.Groups[2].Value, cmd.HeightMm);
                    cmd.ThicknessMm = ParseD(m.Groups[3].Value, cmd.ThicknessMm);
                }

                // Optional: hole size from a metric callout like "M6" / "M8".
                // No \b anchor: .NET treats CJK as word chars, so "M6通孔" has no
                // boundary after the digit.
                Match mh = Regex.Match(text, @"[mM](\d+(?:\.\d+)?)");
                if (mh.Success)
                    cmd.HoleDiameterMm = ParseD(mh.Groups[1].Value, cmd.HoleDiameterMm);

                return cmd;
            }

            // 2) Update thickness. (Checked before hole so "厚度" wins even if a stray number exists.)
            if (text.Contains("厚度") || lower.Contains("thickness"))
            {
                double v;
                if (TryFirstNumber(text, out v))
                    return new UpdateThicknessCommand { ValueMm = v, RawInput = input };
                return new UnsupportedCommand { RawInput = input };
            }

            // 3) Update hole diameter.
            if (text.Contains("孔径") || text.Contains("孔的直径")
                || lower.Contains("hole diameter") || lower.Contains("hole dia"))
            {
                double v;
                if (TryFirstNumber(text, out v))
                    return new UpdateHoleDiameterCommand { ValueMm = v, RawInput = input };
                return new UnsupportedCommand { RawInput = input };
            }

            return new UnsupportedCommand { RawInput = input };
        }

        private static bool TryFirstNumber(string text, out double value)
        {
            value = 0;
            Match m = Regex.Match(text, @"\d+(?:\.\d+)?");
            if (!m.Success) return false;
            return double.TryParse(m.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        private static double ParseD(string s, double fallback)
        {
            double v;
            return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }
    }
}
