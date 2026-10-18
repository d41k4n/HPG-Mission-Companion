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

        // ---- numbers -------------------------------------------------------------------------------------
        static readonly Regex Pair = new Regex("^\\s*(-?\\d+(?:[.,]\\d+)?)\\s*[,;]\\s*(-?\\d+(?:[.,]\\d+)?)\\s*$", RegexOptions.CultureInvariant);

        // "46.9096, 7.5049" pasted in one box: the two numbers
        public static bool SplitPair(string text, out string lat, out string lon)
        {
            lat = null; lon = null;
            if (text == null) return false;
            Match m = Pair.Match(text);
            if (!m.Success) return false;
            lat = m.Groups[1].Value; lon = m.Groups[2].Value;
            return true;
        }

        // a coordinate typed by hand: the point or one comma as the decimal sign
        public static bool ParseNumber(string text, out double v)
        {
            v = 0;
            if (text == null) return false;
            string s = text.Trim();
            if (s.Length == 0) return false;
            if (s.IndexOf('.') < 0 && s.IndexOf(',') >= 0 && s.IndexOf(',') == s.LastIndexOf(',')) s = s.Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !double.IsNaN(v) && !double.IsInfinity(v);
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
