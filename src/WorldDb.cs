// HPG Mission Companion - World Database Update: the entry of the user's own hangar and hospital waypoints and of the changes to the offline databases of HPG
// (airports, aprons, hospital helipads, helipads), without editing JSON by hand. The records go to the user's own files (local-waypoints.json, local-db.json), the
// ones that SharedData merges with the published data. Every text is checked before it is saved: only plain ASCII is allowed (the mission and the tablet of HPG
// do not show other characters well); a character that is not allowed is named, with the plain letter that can replace it when there is one.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace HPGMissionCompanion
{
    // the logic of the window: no controls here, so that it can be checked from the command line
    internal static class WorldDb
    {
        // ---- the unicode check ---------------------------------------------------------------------------
        internal sealed class BadChar
        {
            public string Field;
            public string Text;         // the character (a surrogate pair is one character)
            public int CodePoint;
            public int Position;        // 1-based position in the field
            public string Suggest;      // the plain ASCII that can replace it, "" when there is none
            public string Describe()
            {
                string name = CodePoint < 32 ? "control character" : "'" + Text + "'";
                return Field + ": " + name + " (U+" + CodePoint.ToString("X4", CultureInfo.InvariantCulture) + ") at position " + Position + (Suggest.Length > 0 ? " - can be written \"" + Suggest + "\"" : " - no plain letter for it, remove or rewrite it");
            }
        }

        static readonly Dictionary<char, string> Special = new Dictionary<char, string>
        {
            { '\u00DF', "ss" }, { '\u00E6', "ae" }, { '\u00C6', "AE" }, { '\u00F8', "o" }, { '\u00D8', "O" }, { '\u0111', "d" }, { '\u0110', "D" },
            { '\u0142', "l" }, { '\u0141', "L" }, { '\u0153', "oe" }, { '\u0152', "OE" }, { '\u00F0', "d" }, { '\u00FE', "th" }, { '\u0131', "i" },
            { '\u2018', "'" }, { '\u2019', "'" }, { '\u201C', "\"" }, { '\u201D', "\"" }, { '\u2013', "-" }, { '\u2014', "-" }, { '\u2212', "-" },
            { '\u00A0', " " }, { '\u2026', "..." }, { '\u00B4', "'" }, { '`', "'" }
        };

        // the plain ASCII for one character, "" when there is none
        public static string AsciiFor(string ch)
        {
            if (ch.Length == 1 && Special.ContainsKey(ch[0])) return Special[ch[0]];
            string d = ch.Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder();
            foreach (char c in d)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if (c < 32 || c > 126) return "";
                sb.Append(c);
            }
            return sb.ToString();
        }

        // every character outside the printable ASCII of one text
        public static List<BadChar> Check(string field, string value)
        {
            List<BadChar> bad = new List<BadChar>();
            if (value == null) return bad;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c >= 32 && c <= 126) continue;
                string s = c.ToString();
                if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) { s = value.Substring(i, 2); }
                BadChar b = new BadChar();
                b.Field = field; b.Text = s; b.Position = i + 1;
                b.CodePoint = s.Length == 2 ? char.ConvertToUtf32(s, 0) : c;
                b.Suggest = AsciiFor(s);
                bad.Add(b);
                if (s.Length == 2) i++;
            }
            return bad;
        }

        // the text with every character that has a plain ASCII replaced; the others stay (the check names them)
        public static string Replace(string value)
        {
            if (value == null) return value;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c >= 32 && c <= 126) { sb.Append(c); continue; }
                string s = c.ToString();
                if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) { s = value.Substring(i, 2); i++; }
                string a = AsciiFor(s);
                sb.Append(a.Length > 0 ? a : s);
            }
            return sb.ToString();
        }

        // ---- numbers and coordinates ---------------------------------------------------------------------
        // A pair of coordinates pasted whole into one box, in the formats people copy (user request 2026-10-18):
        //   decimal            46.9096, 7.5049   46.9096,7.5049   46.9096 7.5049   46,9096;7,5049   46.9096/7.5049   (46.9096, 7.5049)   lat=46.9096 lon=7.5049
        //   with hemisphere    46.9096N 7.5049E   N46.9096 E7.5049   7.5049E 46.9096N   46.9096 North 7.5049 East   (S and W are negative)
        //   degrees, minutes   N46 54.576 E007 30.294   46 54.576 N 7 30.294 E   46:54.576N 7:30.294E
        //   degrees, minutes, seconds   46d54'34.6"N 7d30'17.6"E with the degree, prime and double prime signs, or only spaces: 46 54 34.6 N 7 30 17.6 E
        //   aviation, compact  465434N0073017E   4654N00730E   N465434E0073017   4654.576N00730.294E (NMEA)
        //   links              Google Maps (@lat,lon  !3dlat!4dlon  ?q=lat,lon), OpenStreetMap (#map=z/lat/lon, mlat= and mlon=), geo:lat,lon
        // The latitude comes first unless a hemisphere letter says otherwise; a first number beyond 90 with a second within 90 is a longitude first.
        sealed class Cpt { public double Val; public char Hemi; }

        static readonly Regex[] LinkRx = new Regex[]
        {
            new Regex("!3d(-?\\d+(?:\\.\\d+)?)!4d(-?\\d+(?:\\.\\d+)?)", RegexOptions.CultureInvariant),
            new Regex("@(-?\\d+(?:\\.\\d+)?),(-?\\d+(?:\\.\\d+)?)", RegexOptions.CultureInvariant),
            new Regex("[?&#](?:q|ll|query|center|destination|daddr|saddr)=(-?\\d+(?:\\.\\d+)?)(?:,|%2C)\\+?(-?\\d+(?:\\.\\d+)?)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
            new Regex("mlat=(-?\\d+(?:\\.\\d+)?)&(?:amp;)?mlon=(-?\\d+(?:\\.\\d+)?)", RegexOptions.CultureInvariant),
            new Regex("#map=\\d+/(-?\\d+(?:\\.\\d+)?)/(-?\\d+(?:\\.\\d+)?)", RegexOptions.CultureInvariant),
            new Regex("^geo:(-?\\d+(?:\\.\\d+)?),(-?\\d+(?:\\.\\d+)?)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)
        };
        static readonly Regex CompactSuffix = new Regex("^(\\d{2})(?:(\\d{2})(\\d{2}(?:\\.\\d+)?)?)?\\s*([NS])\\s*(\\d{3})(?:(\\d{2})(\\d{2}(?:\\.\\d+)?)?)?\\s*([EW])$", RegexOptions.CultureInvariant);
        static readonly Regex CompactPrefix = new Regex("^([NS])\\s*(\\d{2})(?:(\\d{2})(\\d{2}(?:\\.\\d+)?)?)?\\s*([EW])\\s*(\\d{3})(?:(\\d{2})(\\d{2}(?:\\.\\d+)?)?)?$", RegexOptions.CultureInvariant);
        static readonly Regex Nmea = new Regex("^(\\d{2})(\\d{2}\\.\\d+)\\s*([NS])\\s*(\\d{3})(\\d{2}\\.\\d+)\\s*([EW])$", RegexOptions.CultureInvariant);
        static readonly Regex Tok = new Regex("[-+]?\\d+(?:[.,]\\d+)?|[NSEW]", RegexOptions.CultureInvariant);

        // the text without the signs that only mark the units: degree, prime, double prime, brackets, the words latitude and longitude; null when something else is in it
        static string Normalize(string text, out bool marks)
        {
            marks = false;
            StringBuilder sb = new StringBuilder();
            foreach (char c in text)
            {
                if (c == '\u00B0' || c == '\u00BA' || c == '\u02DA' || c == '\u2032' || c == '\u2019' || c == '\u2018' || c == '\u00B4' || c == '`' || c == '\'' || c == '\u2033' || c == '\u201D' || c == '\u201C' || c == '"') { marks = true; sb.Append(' '); }
                else if (c == '\u2212' || c == '\u2013' || c == '\u2014') sb.Append('-');
                else if (c == '(' || c == ')' || c == '[' || c == ']' || c == '{' || c == '}' || c == ':' || c == '=') sb.Append(' ');
                else sb.Append(c);
            }
            string s = sb.ToString();
            s = Regex.Replace(s, "\\b(?:latitude|longitude|lat|lng|lon|long)\\b", " ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            s = Regex.Replace(s, "\\b(?:north|nord)\\b", "N", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            s = Regex.Replace(s, "\\b(?:south|sud)\\b", "S", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            s = Regex.Replace(s, "\\b(?:east|est)\\b", "E", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            s = Regex.Replace(s, "\\b(?:west|ovest)\\b", "W", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            s = Regex.Replace(s, "(?<=\\d)[dD](?=\\s|\\d|$)", " ", RegexOptions.CultureInvariant);   // 46d54
            s = s.Trim().ToUpperInvariant();
            if (s.Length == 0) return null;
            foreach (char c in s) if (!(char.IsDigit(c) || c == '.' || c == ',' || c == ';' || c == '/' || c == '|' || c == '-' || c == '+' || c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == 'N' || c == 'S' || c == 'E' || c == 'W')) return null;
            return s;
        }

        static bool Number(string t, out double v)
        {
            v = 0;
            if (t == null) return false;
            t = t.Trim();
            if (t.IndexOf('.') < 0 && t.IndexOf(',') >= 0 && t.IndexOf(',') == t.LastIndexOf(',')) t = t.Replace(',', '.');
            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !double.IsNaN(v) && !double.IsInfinity(v);
        }

        // degrees [minutes [seconds]] with the hemisphere: only the last part may have decimals, minutes and seconds under 60
        static bool Group(List<string> nums, char hemi, out Cpt c)
        {
            c = null;
            if (nums.Count < 1 || nums.Count > 3) return false;
            double[] p = new double[3];
            bool neg = false;
            for (int i = 0; i < nums.Count; i++)
            {
                string t = nums[i];
                if (i == 0 && t.StartsWith("-", StringComparison.Ordinal)) neg = true;
                if (i > 0 && (t.StartsWith("-", StringComparison.Ordinal) || t.StartsWith("+", StringComparison.Ordinal))) return false;
                if (!Number(t, out p[i])) return false;
                p[i] = Math.Abs(p[i]);
                if (i < nums.Count - 1 && (t.IndexOf('.') >= 0 || t.IndexOf(',') >= 0)) return false;
                if (i > 0 && p[i] >= 60) return false;
            }
            double v = p[0] + p[1] / 60.0 + p[2] / 3600.0;
            if (hemi == 'S' || hemi == 'W' || neg) v = -v;
            c = new Cpt(); c.Val = v; c.Hemi = hemi;
            return true;
        }

        static Cpt Fixed(string deg, string min, string sec, char hemi)
        {
            double d = 0, m = 0, s = 0;
            if (!Number(deg, out d)) return null;
            if (!string.IsNullOrEmpty(min) && !Number(min, out m)) return null;
            if (!string.IsNullOrEmpty(sec) && !Number(sec, out s)) return null;
            if (m >= 60 || s >= 60) return null;
            double v = d + m / 60.0 + s / 3600.0;
            Cpt c = new Cpt(); c.Val = (hemi == 'S' || hemi == 'W') ? -v : v; c.Hemi = hemi;
            return c;
        }

        // which one is the latitude and which the longitude, and the range
        static bool Assign(Cpt p, Cpt q, out double lat, out double lon)
        {
            lat = 0; lon = 0;
            if (p == null || q == null) return false;
            bool pLat = p.Hemi == 'N' || p.Hemi == 'S', pLon = p.Hemi == 'E' || p.Hemi == 'W', qLat = q.Hemi == 'N' || q.Hemi == 'S', qLon = q.Hemi == 'E' || q.Hemi == 'W';
            bool swap = false;
            if ((pLat && qLat) || (pLon && qLon)) return false;
            if (pLat || qLon) swap = false;
            else if (pLon || qLat) swap = true;
            else swap = Math.Abs(p.Val) > 90 && Math.Abs(q.Val) <= 90;
            lat = swap ? q.Val : p.Val; lon = swap ? p.Val : q.Val;
            return lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180;
        }

        static bool FromLink(string s, out Cpt a, out Cpt b)
        {
            a = null; b = null;
            if (s.IndexOf("://", StringComparison.Ordinal) < 0 && !s.StartsWith("geo:", StringComparison.OrdinalIgnoreCase)) return false;
            foreach (Regex r in LinkRx)
            {
                Match m = r.Match(s);
                if (!m.Success) continue;
                double x, y;
                if (!Number(m.Groups[1].Value, out x) || !Number(m.Groups[2].Value, out y)) continue;
                a = new Cpt(); a.Val = x; b = new Cpt(); b.Val = y;
                return true;
            }
            return false;
        }

        public static bool ParseCoordinates(string text, out double lat, out double lon)
        {
            lat = 0; lon = 0;
            if (text == null) return false;
            string raw = text.Trim();
            if (raw.Length < 3 || raw.Length > 600) return false;
            Cpt p, q;
            if (FromLink(raw, out p, out q)) return Assign(p, q, out lat, out lon);
            bool marks;
            string s = Normalize(raw, out marks);
            if (s == null) return false;
            Match m = CompactSuffix.Match(s);
            if (m.Success) return Assign(Fixed(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value[0]), Fixed(m.Groups[5].Value, m.Groups[6].Value, m.Groups[7].Value, m.Groups[8].Value[0]), out lat, out lon);
            m = CompactPrefix.Match(s);
            if (m.Success) return Assign(Fixed(m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value, m.Groups[1].Value[0]), Fixed(m.Groups[6].Value, m.Groups[7].Value, m.Groups[8].Value, m.Groups[5].Value[0]), out lat, out lon);
            m = Nmea.Match(s);
            if (m.Success) return Assign(Fixed(m.Groups[1].Value, m.Groups[2].Value, null, m.Groups[3].Value[0]), Fixed(m.Groups[4].Value, m.Groups[5].Value, null, m.Groups[6].Value[0]), out lat, out lon);

            // numbers and hemisphere letters in order
            List<string> items = new List<string>();
            List<int> start = new List<int>(), end = new List<int>();
            foreach (Match t in Tok.Matches(s)) { items.Add(t.Value); start.Add(t.Index); end.Add(t.Index + t.Length); }
            if (items.Count == 0) return false;
            List<int> letters = new List<int>();
            for (int i = 0; i < items.Count; i++) if (items[i].Length == 1 && char.IsLetter(items[i][0])) letters.Add(i);
            List<string> g1 = new List<string>(), g2 = new List<string>();
            char h1 = '\0', h2 = '\0';
            if (letters.Count == 0)
            {
                int n = items.Count;
                if (n == 2) { g1.Add(items[0]); g2.Add(items[1]); }
                else if (n == 4) { g1.Add(items[0]); g1.Add(items[1]); g2.Add(items[2]); g2.Add(items[3]); }
                else if (n == 6) { for (int i = 0; i < 3; i++) { g1.Add(items[i]); g2.Add(items[i + 3]); } }
                else return false;
                if (n == 2)
                {
                    // two plain numbers: enough digits to be a place, and "46,7" is one number with a decimal comma, not a pair
                    string sep = s.Substring(end[0], start[1] - end[0]);
                    bool dec = g1[0].IndexOfAny(new char[] { '.', ',' }) >= 0 || g2[0].IndexOfAny(new char[] { '.', ',' }) >= 0;
                    int digits = 0; foreach (char c in g1[0] + g2[0]) if (char.IsDigit(c)) digits++;
                    if (digits < 4) return false;
                    if (!dec && sep.Trim() == "," && sep.IndexOf(' ') < 0) return false;
                }
            }
            else if (letters.Count == 2)
            {
                int l1 = letters[0], l2 = letters[1];
                if (l1 == 0)
                {
                    // N46 54.5 E7 30.2 : the letter opens its group
                    h1 = items[0][0]; h2 = items[l2][0];
                    for (int i = 1; i < l2; i++) g1.Add(items[i]);
                    for (int i = l2 + 1; i < items.Count; i++) g2.Add(items[i]);
                }
                else if (l2 == items.Count - 1)
                {
                    // 46 54.5 N 7 30.2 E : the letter closes its group
                    h1 = items[l1][0]; h2 = items[l2][0];
                    for (int i = 0; i < l1; i++) g1.Add(items[i]);
                    for (int i = l1 + 1; i < l2; i++) g2.Add(items[i]);
                }
                else return false;
            }
            else return false;
            Cpt a, b;
            if (!Group(g1, h1, out a) || !Group(g2, h2, out b)) return false;
            return Assign(a, b, out lat, out lon);
        }

        // one coordinate with signs and a hemisphere: 46.9096, 46 54.576 N, N46 54 34.6, 46d54'34.6"N (without a mark or a letter two numbers are not read as degrees and minutes)
        public static bool ParseSingle(string text, out double v)
        {
            v = 0;
            if (text == null) return false;
            bool marks;
            string s = Normalize(text.Trim(), out marks);
            if (s == null) return false;
            List<string> nums = new List<string>();
            char hemi = '\0';
            List<string> items = new List<string>();
            foreach (Match t in Tok.Matches(s)) items.Add(t.Value);
            if (items.Count == 0) return false;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Length == 1 && char.IsLetter(items[i][0]))
                {
                    if (hemi != '\0' || (i != 0 && i != items.Count - 1)) return false;
                    hemi = items[i][0];
                }
                else nums.Add(items[i]);
            }
            if (nums.Count > 1 && !marks && hemi == '\0') return false;
            Cpt c;
            if (!Group(nums, hemi, out c)) return false;
            v = c.Val;
            return true;
        }

        public static string FormatCoord(double v)
        {
            string r = v.ToString("0.#######", CultureInfo.InvariantCulture);
            return r == "-0" ? "0" : r;
        }

        // a pair pasted in one box: the two numbers as plain decimals
        public static bool SplitPair(string text, out string lat, out string lon)
        {
            lat = null; lon = null;
            double a, b;
            if (!ParseCoordinates(text, out a, out b)) return false;
            lat = FormatCoord(a); lon = FormatCoord(b);
            return true;
        }

        // a coordinate typed by hand: the point or one comma as the decimal sign, or degrees with minutes and seconds and the hemisphere
        public static bool ParseNumber(string text, out double v)
        {
            v = 0;
            if (text == null) return false;
            string s = text.Trim();
            if (s.Length == 0) return false;
            if (Number(s, out v)) return true;
            return ParseSingle(s, out v);
        }

        // 1 for a coordinate, 0 empty, -1 not a number or out of range
        static int Coord(string text, bool lat, out double v)
        {
            v = 0;
            if (text == null || text.Trim().Length == 0) return 0;
            if (!ParseNumber(text, out v)) return -1;
            double lim = lat ? 90 : 180;
            return v < -lim || v > lim ? -1 : 1;
        }

        // ---- the records -----------------------------------------------------------------------------------
        // Builds the record of a hangar or a hospital. points: up to three pairs {lat, lon} typed by the user ("" = not used). Returns null and the reason when it cannot be.
        public static Dictionary<string, object> BuildWaypoint(bool hangar, string id, string by, string lat, string lon, string range, bool boarding, bool deboarding,
            string[] pointLat, string[] pointLon, bool marshal, string marshalLat, string marshalLon, out string error)
        {
            error = null;
            id = (id ?? "").Trim();
            if (id.Length == 0) { error = "The name is missing."; return null; }
            double a, b;
            int c1 = Coord(lat, true, out a), c2 = Coord(lon, false, out b);
            if (c1 != 1 || c2 != 1) { error = (hangar ? "The latitude and longitude of the hangar door" : "The latitude and longitude of the hospital door") + " are needed (latitude -90 to 90, longitude -180 to 180)."; return null; }
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["id"] = id;
            if (!string.IsNullOrWhiteSpace(by)) r["by"] = by.Trim();
            r[hangar ? "WPspawnlat" : "WPdoorlat"] = a;
            r[hangar ? "WPspawnlon" : "WPdoorlon"] = b;
            if (hangar)
            {
                double rg;
                if (!ParseNumber(range, out rg) || rg < 1 || rg > 5000) { error = "The radius must be a number of metres from 1 to 5000 (520 is usual)."; return null; }
                r["max_rng_m"] = (int)Math.Round(rg);
            }
            int total = 0;
            bool gap = false;
            double[] pl = new double[3], pn = new double[3];
            for (int k = 0; k < 3; k++)
            {
                string la = k < pointLat.Length ? pointLat[k] : "", lo = k < pointLon.Length ? pointLon[k] : "";
                int x1 = Coord(la, true, out pl[k]), x2 = Coord(lo, false, out pn[k]);
                if (x1 == -1 || x2 == -1) { error = "Point " + (k + 1) + ": latitude and longitude must be numbers in range."; return null; }
                if (x1 == 0 && x2 == 0) { gap = true; continue; }
                if (x1 != 1 || x2 != 1) { error = "Point " + (k + 1) + " needs both latitude and longitude (or none)."; return null; }
                if (gap) { error = "Fill the points in order: point " + (k + 1) + " is filled but an earlier one is empty."; return null; }
                total++;
            }
            r["WPtot"] = total;
            for (int k = 0; k < 3; k++)
            {
                if (k < total) { r["WP" + (k + 1) + "lat"] = pl[k]; r["WP" + (k + 1) + "lon"] = pn[k]; }
                else { r["WP" + (k + 1) + "lat"] = null; r["WP" + (k + 1) + "lon"] = null; }
            }
            if (hangar) { r["technician_boarding"] = boarding ? "yes" : "no"; r["technician_deboarding"] = deboarding ? "yes" : "no"; }
            r["marshal_present"] = marshal ? "yes" : "no";
            if (marshal)
            {
                double ma, mb;
                if (Coord(marshalLat, true, out ma) != 1 || Coord(marshalLon, false, out mb) != 1) { error = "The marshal needs a latitude and a longitude."; return null; }
                r["WPMarshalLAT"] = ma; r["WPMarshalLON"] = mb;
            }
            else { r["WPMarshalLAT"] = null; r["WPMarshalLON"] = null; }
            string why = SharedData.CheckWaypoint(r, hangar);
            if (why != null) { error = why; return null; }
            return r;
        }

        // a record of an offline database (see SharedData: airport* [lat, lon, name, icao, iata]; apron, H_civil, runway [lat, lon, name]; H_hospital [lat, lon, pad_name, name])
        public static List<object> BuildDbRecord(string type, string lat, string lon, string name, string icao, string iata, string pad, out string error)
        {
            error = null;
            double a, b;
            if (Coord(lat, true, out a) != 1 || Coord(lon, false, out b) != 1) { error = "The latitude and longitude are needed (latitude -90 to 90, longitude -180 to 180)."; return null; }
            List<object> rec = new List<object>();
            rec.Add(a); rec.Add(b);
            name = (name ?? "").Trim();
            if (type == "H_hospital")
            {
                pad = (pad ?? "").Trim();
                if (name.Length == 0 && pad.Length == 0) { error = "A hospital helipad needs the name of the hospital or the name of the pad."; return null; }
                rec.Add(pad); rec.Add(name);
                return rec;
            }
            if (name.Length == 0 && type.StartsWith("airport", StringComparison.Ordinal)) { error = "The name of the airport is missing."; return null; }
            rec.Add(name);
            if (type.StartsWith("airport", StringComparison.Ordinal))
            {
                icao = (icao ?? "").Trim().ToUpperInvariant();
                iata = (iata ?? "").Trim().ToUpperInvariant();
                if (!Regex.IsMatch(icao, "^[A-Z0-9]{0,4}$")) { error = "The ICAO code is up to 4 capital letters or digits."; return null; }
                if (!Regex.IsMatch(iata, "^[A-Z0-9]{0,3}$")) { error = "The IATA code is up to 3 capital letters or digits."; return null; }
                rec.Add(icao); rec.Add(iata);
            }
            return rec;
        }

        public static Dictionary<string, object> BuildRemoval(string lat, string lon, string radius, string name, out string error)
        {
            error = null;
            double a, b, rad = 30;
            if (Coord(lat, true, out a) != 1 || Coord(lon, false, out b) != 1) { error = "The latitude and longitude of the point are needed."; return null; }
            if (!string.IsNullOrWhiteSpace(radius) && (!ParseNumber(radius, out rad) || rad < 1 || rad > 500)) { error = "The radius is a number of metres from 1 to 500 (30 when empty)."; return null; }
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["lat"] = a; r["lon"] = b; r["radius_m"] = (int)Math.Round(rad);
            if (!string.IsNullOrWhiteSpace(name)) r["name"] = name.Trim();
            return r;
        }

        // ---- the user's files --------------------------------------------------------------------------
        static List<object> Listify(object o)
        {
            IList l = o as IList;
            List<object> r = new List<object>();
            if (l != null) foreach (object x in l) r.Add(Deep(x));
            return r;
        }

        static object Deep(object o)
        {
            Dictionary<string, object> d = o as Dictionary<string, object>;
            if (d != null) { Dictionary<string, object> n = new Dictionary<string, object>(); foreach (KeyValuePair<string, object> kv in d) n[kv.Key] = Deep(kv.Value); return n; }
            if (o is string) return o;
            IList l = o as IList;
            if (l != null) return Listify(l);
            return o;
        }

        static Dictionary<string, object> ReadLocal(string path)
        {
            if (!System.IO.File.Exists(path)) return new Dictionary<string, object>();
            Dictionary<string, object> d = Js.Obj(Js.Parse(System.IO.File.ReadAllText(path, Encoding.UTF8)));
            if (d == null) throw new FormatException(System.IO.Path.GetFileName(path) + " is not a JSON object. Fix or delete it first.");
            return (Dictionary<string, object>)Deep(d);
        }

        static void WriteLocal(string path, Dictionary<string, object> d)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.WriteAllText(path, Js.Pretty(d) + "\n", new UTF8Encoding(false));
        }

        static List<object> Bucket(Dictionary<string, object> d, string key)
        {
            object o;
            List<object> l = d.TryGetValue(key, out o) ? o as List<object> : null;
            if (l == null) { l = new List<object>(); d[key] = l; }
            return l;
        }

        static Dictionary<string, object> DbBucket(Dictionary<string, object> d, string type)
        {
            object o;
            Dictionary<string, object> t = d.TryGetValue(type, out o) ? o as Dictionary<string, object> : null;
            if (t == null) { t = new Dictionary<string, object>(); d[type] = t; }
            if (!(t.ContainsKey("add") && t["add"] is List<object>)) t["add"] = new List<object>();
            if (!(t.ContainsKey("remove") && t["remove"] is List<object>)) t["remove"] = new List<object>();
            return t;
        }

        // waypoints: key hangar_wps or hospitals_wps; a record with the same name is replaced
        public static void SaveWaypoint(string key, Dictionary<string, object> rec)
        {
            SharedData.EnsureLocalFiles();
            Dictionary<string, object> d = ReadLocal(SharedData.LocalWaypointsPath);
            List<object> list = Bucket(d, key);
            string id = ((string)rec["id"]).Trim().ToLowerInvariant();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Dictionary<string, object> x = list[i] as Dictionary<string, object>;
                if (x != null && (Js.Str(x, "id") ?? "").Trim().ToLowerInvariant() == id) list.RemoveAt(i);
            }
            list.Add(rec);
            if (!d.ContainsKey("hangar_wps")) d["hangar_wps"] = new List<object>();
            if (!d.ContainsKey("hospitals_wps")) d["hospitals_wps"] = new List<object>();
            WriteLocal(SharedData.LocalWaypointsPath, d);
        }

        public static List<string> ListWaypoints(string key)
        {
            List<string> names = new List<string>();
            try
            {
                foreach (object o in Bucket(ReadLocal(SharedData.LocalWaypointsPath), key))
                {
                    Dictionary<string, object> x = o as Dictionary<string, object>;
                    if (x != null) names.Add(Js.Str(x, "id") ?? "?");
                }
            }
            catch (Exception) { }
            return names;
        }

        public static void RemoveWaypoint(string key, string id)
        {
            Dictionary<string, object> d = ReadLocal(SharedData.LocalWaypointsPath);
            List<object> list = Bucket(d, key);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Dictionary<string, object> x = list[i] as Dictionary<string, object>;
                if (x != null && Js.Str(x, "id") == id) list.RemoveAt(i);
            }
            WriteLocal(SharedData.LocalWaypointsPath, d);
        }

        // databases: "add" of a type; an identical record is not added twice
        public static bool SaveDbRecord(string type, List<object> rec)
        {
            SharedData.EnsureLocalFiles();
            Dictionary<string, object> d = ReadLocal(SharedData.LocalDbPath);
            List<object> add = (List<object>)DbBucket(d, type)["add"];
            string text = Js.Compact(rec);
            foreach (object o in add) if (Js.Compact(o) == text) return false;
            add.Add(rec);
            WriteLocal(SharedData.LocalDbPath, d);
            return true;
        }

        public static bool SaveDbRemoval(string type, Dictionary<string, object> rule)
        {
            SharedData.EnsureLocalFiles();
            Dictionary<string, object> d = ReadLocal(SharedData.LocalDbPath);
            List<object> rem = (List<object>)DbBucket(d, type)["remove"];
            string text = Js.Compact(rule);
            foreach (object o in rem) if (Js.Compact(o) == text) return false;
            rem.Add(rule);
            WriteLocal(SharedData.LocalDbPath, d);
            return true;
        }

        // the lines of the lists of one type ("add" or "remove")
        public static List<string> ListDb(string type, bool removals)
        {
            List<string> lines = new List<string>();
            try
            {
                Dictionary<string, object> d = ReadLocal(SharedData.LocalDbPath);
                object o;
                Dictionary<string, object> t = d.TryGetValue(type, out o) ? o as Dictionary<string, object> : null;
                if (t == null) return lines;
                List<object> list = t.ContainsKey(removals ? "remove" : "add") ? t[removals ? "remove" : "add"] as List<object> : null;
                if (list == null) return lines;
                foreach (object x in list) lines.Add(Js.Compact(x));
            }
            catch (Exception) { }
            return lines;
        }

        public static void RemoveDbLine(string type, bool removals, int index)
        {
            Dictionary<string, object> d = ReadLocal(SharedData.LocalDbPath);
            List<object> list = (List<object>)DbBucket(d, type)[removals ? "remove" : "add"];
            if (index >= 0 && index < list.Count) list.RemoveAt(index);
            WriteLocal(SharedData.LocalDbPath, d);
        }
    }
}
