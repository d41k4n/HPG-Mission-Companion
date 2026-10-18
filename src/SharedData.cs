// HPG Mission Companion - shared data (user request 2026-10-18): the hangar and hospital waypoints of the R&E mission and the changes to the offline databases of the H145
// (airports, aprons, hospital helipads, helipads) are kept on GitHub (shared-data/ of the mission repository), so everybody gets what anybody adds.
//   - waypoints: the program downloads shared-data/waypoints.json, adds the points the user keeps in local-waypoints.json and writes table_R&E_shared_waypoints.json in the
//     work folder of the mission. The mission reads that table (its own built-in points stay as a fallback) and reloads it when L:HMC_DATA_REV changes.
//   - offline databases: html_ui\HPGH145-System\db\<type>.json are lists of [lat, lon, ...] the tablet of HPG uses. The shared changes (add / remove) are applied to a copy
//     kept from the original file, only with the simulator closed and only when the user turned the option on; the original is always put back by "Restore the originals".
// Nothing here runs on the simulator thread. Every download is checked against the sha256 of the manifest; a failed download leaves what is already there.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace HPGMissionCompanion
{
    internal sealed class SharedResult
    {
        public bool Ok = true;
        public int Revision;            // revision of the published files (manifest)
        public int DataRev;             // the counter the mission watches (L:HMC_DATA_REV)
        public int Hangars, Hospitals;  // records in the table
        public int LocalHangars, LocalHospitals;
        public int Written;             // work folders where the table was written or changed
        public bool FromCache;          // the published files could not be read: the copy kept from the last time was used
        public List<string> Lines = new List<string>();
        public void Say(string s) { Lines.Add(s); }
    }

    // a tiny JSON layer: the framework serializer reads the values (numbers as int, long, decimal or double) and this writes them back digit for digit
    internal static class Js
    {
        public static object Parse(string text)
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            js.RecursionLimit = 200;
            return js.DeserializeObject(text.TrimStart((char)0xFEFF));
        }

        public static Dictionary<string, object> Obj(object o) { return o as Dictionary<string, object>; }
        public static IList List(object o) { return o as IList; }

        public static string Str(IDictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? v as string : null;
        }

        public static bool Num(object v, out double d)
        {
            d = 0;
            if (v == null) return false;
            if (v is int) { d = (int)v; return true; }
            if (v is long) { d = (long)v; return true; }
            if (v is decimal) { d = (double)(decimal)v; return true; }
            if (v is double) { d = (double)v; return !double.IsNaN(d) && !double.IsInfinity(d); }
            return false;
        }

        public static string Number(object v)
        {
            if (v is int) return ((int)v).ToString(CultureInfo.InvariantCulture);
            if (v is long) return ((long)v).ToString(CultureInfo.InvariantCulture);
            if (v is decimal) return ((decimal)v).ToString(CultureInfo.InvariantCulture);
            double d = (double)v;
            if (double.IsNaN(d) || double.IsInfinity(d)) throw new InvalidOperationException("a number that is not finite");
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        public static string Quote(string s)
        {
            StringBuilder sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            sb.Append('"');
            return sb.ToString();
        }

        // one line
        public static void Compact(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }
            string s = v as string;
            if (s != null) { sb.Append(Quote(s)); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            IDictionary<string, object> d = v as IDictionary<string, object>;
            if (d != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, object> kv in d)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(Quote(kv.Key)).Append(':');
                    Compact(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }
            IList l = v as IList;
            if (l != null)
            {
                sb.Append('[');
                for (int i = 0; i < l.Count; i++) { if (i > 0) sb.Append(','); Compact(sb, l[i]); }
                sb.Append(']');
                return;
            }
            sb.Append(Number(v));
        }

        public static string Compact(object v) { StringBuilder sb = new StringBuilder(); Compact(sb, v); return sb.ToString(); }

        // the user's own files: objects one key a line, a list of records one record a line, a list of plain values on one line
        public static string Pretty(object v) { StringBuilder sb = new StringBuilder(); Pretty(sb, v, 0); return sb.ToString(); }

        static void Pretty(StringBuilder sb, object v, int indent)
        {
            string pad = new string(' ', indent);
            IDictionary<string, object> d = v as IDictionary<string, object>;
            if (d != null)
            {
                if (d.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                int n = 0;
                foreach (KeyValuePair<string, object> kv in d)
                {
                    sb.Append(pad).Append("  ").Append(Quote(kv.Key)).Append(": ");
                    Pretty(sb, kv.Value, indent + 2);
                    sb.Append(++n < d.Count ? ",\n" : "\n");
                }
                sb.Append(pad).Append('}');
                return;
            }
            IList l = v as IList;
            if (l != null && !(v is string))
            {
                if (l.Count == 0) { sb.Append("[]"); return; }
                bool flat = true;
                foreach (object x in l) if (x is IDictionary<string, object> || (x is IList && !(x is string))) flat = false;
                if (flat) { Compact(sb, v); return; }
                sb.Append("[\n");
                for (int i = 0; i < l.Count; i++)
                {
                    sb.Append(pad).Append("  ");
                    Compact(sb, l[i]);
                    sb.Append(i < l.Count - 1 ? ",\n" : "\n");
                }
                sb.Append(pad).Append(']');
                return;
            }
            Compact(sb, v);
        }

        // the style of the HPG database files: two spaces, every value of a record on its own line
        public static string Records(List<IList> records)
        {
            StringBuilder sb = new StringBuilder(records.Count * 60 + 16);
            sb.Append("[\n");
            for (int i = 0; i < records.Count; i++)
            {
                IList r = records[i];
                sb.Append("  [\n");
                for (int k = 0; k < r.Count; k++)
                {
                    sb.Append("    ");
                    Compact(sb, r[k]);
                    sb.Append(k < r.Count - 1 ? ",\n" : "\n");
                }
                sb.Append(i < records.Count - 1 ? "  ],\n" : "  ]\n");
            }
            sb.Append("]");
            return sb.ToString();
        }
    }

    internal static class SharedData
    {
        public const string RepoBase = "https://raw.githubusercontent.com/davierosoft/HEMS-random-everywhere-mission/main/shared-data/";
        public const string IssueUrl = "https://github.com/davierosoft/HEMS-random-everywhere-mission/issues/new";
        public const string TableName = "table_R&E_shared_waypoints.json";
        public const string VarRev = "L:HMC_DATA_REV";          // the counter the mission watches: it reads the table again when this number changes
        public static readonly string[] DbTypes = new string[] { "airport", "airport-nocode", "airport-mil", "apron", "H_civil", "H_hospital", "runway" };

        // the folder of the program data of this feature; tests point it elsewhere
        public static string StateOverride;
        public static string StateDir { get { return StateOverride != null ? StateOverride : Path.Combine(Ctx.BaseDir, "shared-data"); } }
        static string CacheDir { get { return Path.Combine(StateDir, "cache"); } }
        public static string LocalWaypointsPath { get { return Path.Combine(StateDir, "local-waypoints.json"); } }
        public static string LocalDbPath { get { return Path.Combine(StateDir, "local-db.json"); } }
        static string StatePath { get { return Path.Combine(StateDir, "state.json"); } }
        static string DbStatePath { get { return Path.Combine(StateDir, "db-state.json"); } }

        // ---- small helpers ---------------------------------------------------------------------------
        public static string Norm(string text) { return text.TrimStart((char)0xFEFF).Replace("\r\n", "\n"); }

        public static string Sha(string text)
        {
            using (SHA256 h = SHA256.Create()) return Hex(h.ComputeHash(Encoding.UTF8.GetBytes(Norm(text))));
        }

        static string Hex(byte[] b) { return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant(); }

        static string FileSha(string file)
        {
            using (SHA256 h = SHA256.Create())
            using (FileStream f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                return Hex(h.ComputeHash(f));
        }

        static byte[] ReadAllBytes(string file)
        {
            using (FileStream f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (MemoryStream m = new MemoryStream())
            {
                f.CopyTo(m);
                return m.ToArray();
            }
        }

        // a stream write: the file keeps the attributes of its own place (the files of the Community folder are encrypted by the Store, a plain copy of such a file can fail)
        static void WriteAllBytes(string file, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            using (FileStream f = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None)) f.Write(bytes, 0, bytes.Length);
        }

        static void WriteText(string file, string text) { WriteAllBytes(file, new UTF8Encoding(false).GetBytes(text)); }

        static string ReadText(string file) { return Encoding.UTF8.GetString(ReadAllBytes(file)).TrimStart((char)0xFEFF); }

        // the source is the repository (https) or a folder (tests, an offline copy)
        static string ReadSource(string source, string rel)
        {
            if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return Net.GetString((source.EndsWith("/") ? source : source + "/") + rel);
            return ReadText(Path.Combine(source, rel.Replace('/', '\\')));
        }

        static Dictionary<string, object> LoadState(string path)
        {
            try { if (File.Exists(path)) { Dictionary<string, object> d = Js.Obj(Js.Parse(ReadText(path))); if (d != null) return d; } }
            catch (Exception) { }
            return new Dictionary<string, object>();
        }

        static void SaveState(string path, Dictionary<string, object> d) { WriteText(path, Js.Compact(d)); }

        static int IntOf(Dictionary<string, object> d, string key)
        {
            double v;
            object o;
            return d != null && d.TryGetValue(key, out o) && Js.Num(o, out v) ? (int)v : 0;
        }

        // ---- validation of the waypoint records (the same rules as tools/shared-data.js of the mission repository) -------------
        static bool Coord(object lat, object lon)
        {
            double a, b;
            return Js.Num(lat, out a) && Js.Num(lon, out b) && a >= -90 && a <= 90 && b >= -180 && b <= 180;
        }

        // null when the record is usable, else why not
        public static string CheckWaypoint(Dictionary<string, object> r, bool hangar)
        {
            string id = Js.Str(r, "id");
            if (string.IsNullOrEmpty(id) || id.Trim().Length == 0) return "id is missing";
            double total;
            object tv;
            if (!r.TryGetValue("WPtot", out tv) || !Js.Num(tv, out total) || total < 0 || total > 3 || total != Math.Floor(total)) return "WPtot must be 0, 1, 2 or 3";
            for (int k = 1; k <= 3; k++)
            {
                object lat, lon;
                r.TryGetValue("WP" + k + "lat", out lat);
                r.TryGetValue("WP" + k + "lon", out lon);
                if (k <= total && !Coord(lat, lon)) return "WP" + k + " needs latitude and longitude (WPtot is " + total + ")";
            }
            string mar = Js.Str(r, "marshal_present");
            if (mar != "yes" && mar != "no") return "marshal_present must be yes or no";
            object mlat, mlon;
            r.TryGetValue("WPMarshalLAT", out mlat);
            r.TryGetValue("WPMarshalLON", out mlon);
            if (mar == "yes" && !Coord(mlat, mlon)) return "marshal_present is yes: WPMarshalLAT and WPMarshalLON are needed";
            object a, b;
            if (hangar)
            {
                object rng;
                double rg;
                r.TryGetValue("WPspawnlat", out a);
                r.TryGetValue("WPspawnlon", out b);
                if (!Coord(a, b)) return "WPspawnlat and WPspawnlon are needed";
                if (!r.TryGetValue("max_rng_m", out rng) || !Js.Num(rng, out rg) || rg < 1 || rg > 5000) return "max_rng_m must be between 1 and 5000";
                string tb = Js.Str(r, "technician_boarding"), td = Js.Str(r, "technician_deboarding");
                if ((tb != "yes" && tb != "no") || (td != "yes" && td != "no")) return "technician_boarding and technician_deboarding must be yes or no";
            }
            else
            {
                r.TryGetValue("WPdoorlat", out a);
                r.TryGetValue("WPdoorlon", out b);
                if (!Coord(a, b)) return "WPdoorlat and WPdoorlon are needed";
            }
            return null;
        }

        // the valid records of one list; the others are told in the result
        static List<Dictionary<string, object>> ValidRecords(object list, bool hangar, string where, SharedResult res)
        {
            List<Dictionary<string, object>> ok = new List<Dictionary<string, object>>();
            IList l = Js.List(list);
            if (l == null) return ok;
            for (int i = 0; i < l.Count; i++)
            {
                Dictionary<string, object> rec = Js.Obj(l[i]);
                string why = rec == null ? "not a record" : CheckWaypoint(rec, hangar);
                if (why != null) { res.Say("[WARN] " + where + " #" + (i + 1) + (rec != null && Js.Str(rec, "id") != null ? " \"" + Js.Str(rec, "id") + "\"" : "") + " skipped: " + why); continue; }
                ok.Add(rec);
            }
            return ok;
        }

        // ---- the published files -------------------------------------------------------------------------
        // reads manifest.json and the files it lists (each checked against its sha256) into memory; returns the texts by relative path
        static Dictionary<string, string> Download(string source, out int revision)
        {
            string mtext = ReadSource(source, "manifest.json");
            Dictionary<string, object> m = Js.Obj(Js.Parse(mtext));
            if (m == null || IntOf(m, "schema") != 1) throw new InvalidDataException("manifest.json: unknown schema");
            revision = IntOf(m, "revision");
            Dictionary<string, object> files = Js.Obj(m.ContainsKey("files") ? m["files"] : null);
            if (revision < 1 || files == null) throw new InvalidDataException("manifest.json: no revision or files");
            Dictionary<string, string> texts = new Dictionary<string, string>();
            texts["manifest.json"] = mtext;
            foreach (KeyValuePair<string, object> kv in files)
            {
                if (kv.Key.IndexOf("..", StringComparison.Ordinal) >= 0 || Path.IsPathRooted(kv.Key)) throw new InvalidDataException("manifest.json: bad file name " + kv.Key);
                string text = ReadSource(source, kv.Key);
                string want = Js.Str(Js.Obj(kv.Value), "sha256");
                if (want == null || Sha(text) != want.ToLowerInvariant()) throw new InvalidDataException(kv.Key + " is damaged or cut (the sha256 of the manifest differs)");
                Js.Parse(text);     // must be JSON
                texts[kv.Key] = text;
            }
            return texts;
        }

        static void KeepCache(Dictionary<string, string> texts)
        {
            foreach (KeyValuePair<string, string> kv in texts) WriteText(Path.Combine(CacheDir, kv.Key.Replace('/', '\\')), kv.Value);
        }

        static Dictionary<string, string> ReadCache(out int revision)
        {
            revision = 0;
            string mpath = Path.Combine(CacheDir, "manifest.json");
            if (!File.Exists(mpath)) return null;
            Dictionary<string, string> texts = new Dictionary<string, string>();
            string mtext = ReadText(mpath);
            Dictionary<string, object> m = Js.Obj(Js.Parse(mtext));
            revision = IntOf(m, "revision");
            texts["manifest.json"] = mtext;
            Dictionary<string, object> files = m == null ? null : Js.Obj(m.ContainsKey("files") ? m["files"] : null);
            if (files == null) return null;
            foreach (KeyValuePair<string, object> kv in files)
            {
                string p = Path.Combine(CacheDir, kv.Key.Replace('/', '\\'));
                if (!File.Exists(p)) return null;
                texts[kv.Key] = ReadText(p);
            }
            return texts;
        }

        // ---- local files ------------------------------------------------------------------------------------
        public static void EnsureLocalFiles()
        {
            Directory.CreateDirectory(StateDir);
            if (!File.Exists(LocalWaypointsPath)) WriteText(LocalWaypointsPath, "{\n  \"hangar_wps\": [],\n  \"hospitals_wps\": []\n}\n");
            if (!File.Exists(LocalDbPath)) WriteText(LocalDbPath, "{\n  \"airport\": {\"add\": [], \"remove\": []},\n  \"apron\": {\"add\": [], \"remove\": []},\n  \"H_hospital\": {\"add\": [], \"remove\": []},\n  \"H_civil\": {\"add\": [], \"remove\": []}\n}\n");
        }

        static Dictionary<string, object> LocalJson(string path, SharedResult res)
        {
            if (!File.Exists(path)) return new Dictionary<string, object>();
            try
            {
                Dictionary<string, object> d = Js.Obj(Js.Parse(ReadText(path)));
                if (d != null) return d;
                res.Say("[WARN] " + Path.GetFileName(path) + " is not a JSON object: ignored");
            }
            catch (Exception ex) { res.Say("[WARN] " + Path.GetFileName(path) + " is not valid JSON, ignored (" + ex.Message + ")"); }
            return new Dictionary<string, object>();
        }

        // ---- the waypoint table -----------------------------------------------------------------------------
        // source: the repository folder or URL; works: the work folders to write. The counter L:HMC_DATA_REV goes up only when a table file really changed.
        public static SharedResult Sync(string source, IList<string> works)
        {
            SharedResult res = new SharedResult();
            try
            {
                Directory.CreateDirectory(StateDir);
                Dictionary<string, string> texts = null;
                int revision = 0;
                try
                {
                    texts = Download(source, out revision);
                    KeepCache(texts);
                    res.Say("[INFO] Shared data: published revision " + revision + " read");
                }
                catch (Exception ex)
                {
                    texts = ReadCache(out revision);
                    if (texts == null) { res.Ok = false; res.Say("[WARN] Shared data: cannot read the published files (" + ex.Message + ") and there is no earlier copy"); return res; }
                    res.FromCache = true;
                    res.Say("[WARN] Shared data: cannot read the published files (" + ex.Message + "): the copy of revision " + revision + " kept from the last time is used");
                }
                res.Revision = revision;
                Dictionary<string, object> remote = Js.Obj(Js.Parse(texts["waypoints.json"]));
                Dictionary<string, object> local = LocalJson(LocalWaypointsPath, res);
                List<Dictionary<string, object>> rh = ValidRecords(remote.ContainsKey("hangar_wps") ? remote["hangar_wps"] : null, true, "shared hangar", res);
                List<Dictionary<string, object>> ro = ValidRecords(remote.ContainsKey("hospitals_wps") ? remote["hospitals_wps"] : null, false, "shared hospital", res);
                List<Dictionary<string, object>> lh = ValidRecords(local.ContainsKey("hangar_wps") ? local["hangar_wps"] : null, true, "your hangar", res);
                List<Dictionary<string, object>> lo = ValidRecords(local.ContainsKey("hospitals_wps") ? local["hospitals_wps"] : null, false, "your hospital", res);
                res.LocalHangars = lh.Count; res.LocalHospitals = lo.Count;
                List<Dictionary<string, object>> hangars = Merge(lh, rh);
                List<Dictionary<string, object>> hospitals = Merge(lo, ro);
                res.Hangars = hangars.Count; res.Hospitals = hospitals.Count;
                string table = TableText(revision, hangars, hospitals);
                string tsha = Sha(table);

                Dictionary<string, object> state = LoadState(StatePath);
                int dataRev = IntOf(state, "dataRev");
                bool changed = Js.Str(state, "tableSha") != tsha;
                foreach (string work in works)
                {
                    try
                    {
                        string file = Path.Combine(work, TableName);
                        if (File.Exists(file) && ReadText(file) == table) continue;
                        WriteText(file, table);
                        res.Written++;
                        changed = true;
                        res.Say("[INFO] Shared data: " + TableName + " written in " + work);
                    }
                    catch (Exception ex) { res.Say("[WARN] Shared data: cannot write the table in " + work + " (" + ex.Message + ")"); }
                }
                if (changed || dataRev < 1) dataRev = Math.Max(1, dataRev + 1);
                state["dataRev"] = dataRev;
                state["tableSha"] = tsha;
                state["revision"] = revision;
                state["checkedUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                SaveState(StatePath, state);
                res.DataRev = dataRev;
                res.Say("[INFO] Shared data: " + hangars.Count + " hangars (" + lh.Count + " yours), " + hospitals.Count + " hospitals (" + lo.Count + " yours); the mission counter is " + dataRev + (changed ? " (changed)" : " (nothing new)"));
                if (works.Count == 0) res.Say("[WARN] Shared data: no work folder of the mission was found (Tools tab: Browse): the table was not written");
            }
            catch (Exception ex)
            {
                res.Ok = false;
                res.Say("[ERROR] Shared data: " + ex.Message);
            }
            return res;
        }

        // your points first (they win when an id is in both), then the published ones
        static List<Dictionary<string, object>> Merge(List<Dictionary<string, object>> local, List<Dictionary<string, object>> remote)
        {
            List<Dictionary<string, object>> all = new List<Dictionary<string, object>>(local);
            HashSet<string> mine = new HashSet<string>();
            foreach (Dictionary<string, object> r in local) mine.Add(Js.Str(r, "id").Trim().ToLowerInvariant());
            foreach (Dictionary<string, object> r in remote) if (!mine.Contains(Js.Str(r, "id").Trim().ToLowerInvariant())) all.Add(r);
            return all;
        }

        // the file HPG reads: a flat JSON object (a table of the mission), one record per line
        static string TableText(int revision, List<Dictionary<string, object>> hangars, List<Dictionary<string, object>> hospitals)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\n\"version\":").Append(revision).Append(",\n");
            AppendList(sb, "hangar_wps", hangars);
            sb.Append(",\n");
            AppendList(sb, "hospitals_wps", hospitals);
            sb.Append("\n}\n");
            return sb.ToString();
        }

        static void AppendList(StringBuilder sb, string key, List<Dictionary<string, object>> list)
        {
            sb.Append(Js.Quote(key)).Append(":[");
            for (int i = 0; i < list.Count; i++)
            {
                sb.Append(i == 0 ? "\n" : ",\n");
                Js.Compact(sb, list[i]);
            }
            sb.Append(list.Count > 0 ? "\n]" : "]");
        }

        // the counter to publish to the simulator at start (before the link opens: a variable must be known when the link connects)
        public static int StoredDataRev()
        {
            try { return IntOf(LoadState(StatePath), "dataRev"); } catch (Exception) { return 0; }
        }

        public static string StatusLine()
        {
            Dictionary<string, object> s = LoadState(StatePath);
            if (s.Count == 0) return "Not synchronised yet.";
            return "Published revision " + IntOf(s, "revision") + ", checked " + (Js.Str(s, "checkedUtc") ?? "?") + " UTC, mission counter " + IntOf(s, "dataRev") + ".";
        }

        // the work folders that get the table: the HPG packages with a work folder (the mission runs in the H145 add-on package), or the folder chosen in the Tools tab
        public static List<string> TargetWorkFolders(string custom)
        {
            List<string> list = new List<string>();
            foreach (WorkFolderInfo w in Tools.FindWorkFolders(custom))
            {
                bool h145 = w.Path.IndexOf("hpg-airbus-h145-ap", StringComparison.OrdinalIgnoreCase) >= 0;
                bool chosen = !string.IsNullOrEmpty(custom) && string.Equals(w.Path, custom, StringComparison.OrdinalIgnoreCase);
                if ((h145 || chosen) && !list.Contains(w.Path)) list.Add(w.Path);
            }
            return list;
        }

        // ---- offline databases of HPG ---------------------------------------------------------------------
        // the db folders of the HPG packages: <Community>\hpg-*\html_ui\<something>\db with the database files
        public static List<string> FindDbFolders()
        {
            List<string> list = new List<string>();
            foreach (string root in Tools.CommunityRoots())
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (string pkg in Directory.GetDirectories(root, "hpg-*"))
                    {
                        string ui = Path.Combine(pkg, "html_ui");
                        if (!Directory.Exists(ui)) continue;
                        foreach (string sys in Directory.GetDirectories(ui))
                        {
                            string db = Path.Combine(sys, "db");
                            if (File.Exists(Path.Combine(db, "H_hospital.json")) || File.Exists(Path.Combine(db, "airport.json"))) list.Add(db);
                        }
                    }
                }
                catch (Exception) { }
            }
            return list;
        }

        sealed class Rule { public double Lat, Lon, Radius; public string Name; }

        sealed class Delta
        {
            public List<IList> Add = new List<IList>();
            public List<Rule> Remove = new List<Rule>();
            public string Sig = "";
            public bool Empty { get { return Add.Count == 0 && Remove.Count == 0; } }
        }

        static int FieldCount(string type) { return type == "H_hospital" ? 4 : (type.StartsWith("airport", StringComparison.Ordinal) ? 5 : 3); }

        // the shared delta of a type (from the checked copy of the repository files) and the user's own one
        static Delta LoadDelta(string type, Dictionary<string, string> published, Dictionary<string, object> local, SharedResult res)
        {
            Delta d = new Delta();
            StringBuilder sig = new StringBuilder();
            string text;
            if (published != null && published.TryGetValue("db/" + type + ".json", out text))
            {
                AddDelta(d, type, Js.Obj(Js.Parse(text)), "shared " + type, res);
                sig.Append(Sha(text));
            }
            object mine;
            if (local != null && local.TryGetValue(type, out mine))
            {
                Dictionary<string, object> m = Js.Obj(mine);
                AddDelta(d, type, m, "your " + type, res);
                sig.Append("|").Append(Sha(Js.Compact(mine)));
            }
            d.Sig = Sha(sig.ToString());
            return d;
        }

        static void AddDelta(Delta d, string type, Dictionary<string, object> src, string where, SharedResult res)
        {
            if (src == null) return;
            IList add = Js.List(src.ContainsKey("add") ? src["add"] : null);
            int want = FieldCount(type);
            if (add != null)
            {
                for (int i = 0; i < add.Count; i++)
                {
                    IList r = Js.List(add[i]);
                    double lat = 0, lon = 0;
                    bool ok = r != null && r.Count == want && Js.Num(r[0], out lat) && Js.Num(r[1], out lon) && lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180;
                    for (int k = 2; ok && k < want; k++) if (!(r[k] is string)) ok = false;
                    if (!ok) { res.Say("[WARN] " + where + " add #" + (i + 1) + " skipped: a record has " + want + " values (latitude, longitude, then texts)"); continue; }
                    d.Add.Add(r);
                }
            }
            IList rem = Js.List(src.ContainsKey("remove") ? src["remove"] : null);
            if (rem != null)
            {
                for (int i = 0; i < rem.Count; i++)
                {
                    Dictionary<string, object> o = Js.Obj(rem[i]);
                    double lat = 0, lon = 0, rad = 30;
                    object rv;
                    bool ok = o != null && o.ContainsKey("lat") && o.ContainsKey("lon") && Js.Num(o["lat"], out lat) && Js.Num(o["lon"], out lon);
                    if (ok && o.TryGetValue("radius_m", out rv) && rv != null && !Js.Num(rv, out rad)) ok = false;
                    if (ok && (rad < 1 || rad > 500)) ok = false;
                    if (!ok) { res.Say("[WARN] " + where + " remove #" + (i + 1) + " skipped: lat and lon are needed, radius_m from 1 to 500"); continue; }
                    Rule rule = new Rule();
                    rule.Lat = lat; rule.Lon = lon; rule.Radius = rad; rule.Name = Js.Str(o, "name");
                    d.Remove.Add(rule);
                }
            }
        }

        static double Meters(double lat1, double lon1, double lat2, double lon2)
        {
            double dLat = (lat2 - lat1) * Math.PI / 180, dLon = (lon2 - lon1) * Math.PI / 180;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 6371000.0 * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        static bool NameIs(IList rec, string name)
        {
            for (int k = 2; k < rec.Count; k++) { string s = rec[k] as string; if (s != null && string.Equals(s, name, StringComparison.OrdinalIgnoreCase)) return true; }
            return false;
        }

        static string NamesOf(IList rec)
        {
            StringBuilder sb = new StringBuilder();
            for (int k = 2; k < rec.Count; k++) sb.Append(rec[k] as string ?? "").Append('|');
            return sb.ToString().ToLowerInvariant();
        }

        // the records of a database file after the delta: the removals first (every record within radius_m of the point, and with the name when one is given), then the additions
        // (not when a record with the same names is within 30 m already)
        static List<IList> ApplyDelta(List<IList> records, Delta delta, out int added, out int removed)
        {
            added = 0; removed = 0;
            List<IList> kept = new List<IList>(records.Count);
            foreach (IList rec in records)
            {
                double lat, lon;
                bool drop = false;
                if (rec.Count >= 2 && Js.Num(rec[0], out lat) && Js.Num(rec[1], out lon))
                {
                    foreach (Rule r in delta.Remove)
                    {
                        if (Math.Abs(lat - r.Lat) > r.Radius / 100000.0 + 0.0005) continue;
                        if (Meters(lat, lon, r.Lat, r.Lon) > r.Radius) continue;
                        if (r.Name != null && !NameIs(rec, r.Name)) continue;
                        drop = true; break;
                    }
                }
                if (drop) removed++; else kept.Add(rec);
            }
            foreach (IList add in delta.Add)
            {
                double alat, alon, lat, lon;
                Js.Num(add[0], out alat); Js.Num(add[1], out alon);
                string names = NamesOf(add);
                bool dup = false;
                foreach (IList rec in kept)
                {
                    if (rec.Count < 2 || !Js.Num(rec[0], out lat) || !Js.Num(rec[1], out lon)) continue;
                    if (Math.Abs(lat - alat) > 0.0005) continue;
                    if (NamesOf(rec) == names && Meters(lat, lon, alat, alon) <= 30) { dup = true; break; }
                }
                if (dup) continue;
                kept.Add(add);
                added++;
            }
            return kept;
        }

        static List<IList> ParseRecords(byte[] bytes)
        {
            object o = Js.Parse(Encoding.UTF8.GetString(bytes));
            IList l = Js.List(o);
            if (l == null) throw new InvalidDataException("the database file is not a list");
            List<IList> list = new List<IList>(l.Count);
            foreach (object r in l) { IList rec = Js.List(r); if (rec == null) throw new InvalidDataException("a record is not a list"); list.Add(rec); }
            return list;
        }

        static string BackupPath(string folder, string type)
        {
            using (MD5 m = MD5.Create())
                return Path.Combine(Path.Combine(Path.Combine(StateDir, "db-backup"), Hex(m.ComputeHash(Encoding.UTF8.GetBytes(folder.ToLowerInvariant()))).Substring(0, 12)), type + ".json");
        }

        // Puts the shared and the user's changes into the databases of the folders. Only with the simulator closed (the caller checks); dryRun only reports what would change.
        // The original of every file is kept first; a file that is not the original and not our result any more (HPG updated it) becomes the new original.
        public static SharedResult ApplyDb(IList<string> folders, bool dryRun)
        {
            SharedResult res = new SharedResult();
            try
            {
                Directory.CreateDirectory(StateDir);
                int revision;
                Dictionary<string, string> published = null;
                try { published = ReadCache(out revision); } catch (Exception) { published = null; }
                Dictionary<string, object> local = LocalJson(LocalDbPath, res);
                Dictionary<string, object> state = LoadState(DbStatePath);
                Dictionary<string, Delta> deltas = new Dictionary<string, Delta>();
                foreach (string dtype in DbTypes) deltas[dtype] = LoadDelta(dtype, published, local, res);
                int files = 0, touched = 0;
                foreach (string folder in folders)
                {
                    foreach (string type in DbTypes)
                    {
                        string file = Path.Combine(folder, type + ".json");
                        if (!File.Exists(file)) continue;
                        files++;
                        string key = (folder + "|" + type).ToLowerInvariant();
                        Dictionary<string, object> e = null;
                        object eo;
                        if (state.TryGetValue(key, out eo)) e = Js.Obj(eo);
                        Delta delta = deltas[type];
                        string cur = FileSha(file);
                        string backup = BackupPath(folder, type);
                        string patched = e == null ? null : Js.Str(e, "patchedSha");
                        string original = e == null ? null : Js.Str(e, "originalSha");
                        bool backupOk = e != null && File.Exists(backup) && original != null && FileSha(backup) == original;
                        if (delta.Empty)
                        {
                            // nothing to apply: a file we changed before goes back to the original
                            if (e != null && cur == patched && backupOk)
                            {
                                res.Say("[INFO] " + type + " (" + folder + "): no changes left, the original comes back");
                                if (!dryRun) { WriteAllBytes(file, ReadAllBytes(backup)); state.Remove(key); touched++; }
                            }
                            else if (e != null && !dryRun) state.Remove(key);
                            continue;
                        }
                        if (e != null && cur == patched && Js.Str(e, "deltaSig") == delta.Sig) { res.Say("[INFO] " + type + " (" + folder + "): up to date"); continue; }
                        byte[] baseBytes;
                        if (e != null && cur == patched && backupOk) baseBytes = ReadAllBytes(backup);
                        else
                        {
                            if (e != null && cur != original) res.Say("[INFO] " + type + " (" + folder + "): the file is not the one we changed: it is taken as the new original");
                            baseBytes = ReadAllBytes(file);
                            original = cur;
                            if (!dryRun) WriteAllBytes(backup, baseBytes);
                        }
                        bool bom = baseBytes.Length > 2 && baseBytes[0] == 0xEF && baseBytes[1] == 0xBB && baseBytes[2] == 0xBF;
                        List<IList> records = ParseRecords(baseBytes);
                        int added, removed;
                        List<IList> result = ApplyDelta(records, delta, out added, out removed);
                        res.Say("[INFO] " + type + " (" + folder + "): " + records.Count + " records, " + removed + " removed, " + added + " added" + (dryRun ? " (check only)" : ""));
                        if (dryRun) continue;
                        byte[] body = new UTF8Encoding(false).GetBytes(Js.Records(result));
                        byte[] outBytes = body;
                        if (bom)
                        {
                            outBytes = new byte[body.Length + 3];
                            outBytes[0] = 0xEF; outBytes[1] = 0xBB; outBytes[2] = 0xBF;
                            Array.Copy(body, 0, outBytes, 3, body.Length);
                        }
                        // the result must read back with the number of records we expect, else the file is not touched
                        if (ParseRecords(outBytes).Count != records.Count - removed + added) throw new InvalidDataException(type + ": the new file does not read back correctly, not written");
                        try { WriteAllBytes(file, outBytes); }
                        catch (Exception)
                        {
                            try { WriteAllBytes(file, baseBytes); } catch (Exception) { }
                            throw;
                        }
                        Dictionary<string, object> ne = new Dictionary<string, object>();
                        ne["folder"] = folder;
                        ne["type"] = type;
                        ne["originalSha"] = original;
                        ne["patchedSha"] = FileSha(file);
                        ne["deltaSig"] = delta.Sig;
                        ne["utc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                        state[key] = ne;
                        touched++;
                    }
                }
                if (!dryRun) SaveState(DbStatePath, state);
                res.Say("[INFO] Offline databases: " + files + " file(s) looked at, " + touched + " changed");
            }
            catch (Exception ex)
            {
                res.Ok = false;
                res.Say("[ERROR] Offline databases: " + ex.Message);
            }
            return res;
        }

        // every file we changed goes back to its original (kept in the backup); a file HPG changed since is left alone
        public static SharedResult RestoreDb()
        {
            SharedResult res = new SharedResult();
            try
            {
                Dictionary<string, object> state = LoadState(DbStatePath);
                int back = 0;
                foreach (string key in new List<string>(state.Keys))
                {
                    Dictionary<string, object> e = Js.Obj(state[key]);
                    int bar = key.LastIndexOf('|');
                    // the key is lower case: the entry keeps the real folder and type
                    string folder = Js.Str(e, "folder") ?? key.Substring(0, bar), type = Js.Str(e, "type") ?? key.Substring(bar + 1);
                    string file = Directory.Exists(folder) ? Path.Combine(folder, type + ".json") : null;
                    string backup = file == null ? null : BackupPath(Path.GetDirectoryName(file), type);
                    if (file != null && File.Exists(file) && backup != null && File.Exists(backup) && FileSha(file) == Js.Str(e, "patchedSha") && FileSha(backup) == Js.Str(e, "originalSha"))
                    {
                        WriteAllBytes(file, ReadAllBytes(backup));
                        back++;
                        res.Say("[INFO] " + type + " (" + Path.GetDirectoryName(file) + "): the original is back");
                    }
                    else res.Say("[INFO] " + type + " (" + folder + "): not ours any more (or already the original): left as it is");
                    state.Remove(key);
                }
                SaveState(DbStatePath, state);
                res.Say("[INFO] Offline databases: " + back + " file(s) restored");
            }
            catch (Exception ex)
            {
                res.Ok = false;
                res.Say("[ERROR] Offline databases: " + ex.Message);
            }
            return res;
        }

        public static string DbStatusLine()
        {
            Dictionary<string, object> state = LoadState(DbStatePath);
            return state.Count == 0 ? "The offline databases of HPG are the original ones." : state.Count + " database file(s) carry the shared changes.";
        }

        // the text to paste into a GitHub issue to share one's own points (the maintainers add them to shared-data/)
        public static string ShareText()
        {
            EnsureLocalFiles();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Hangar and hospital waypoints and offline database changes to share (from the Mission Companion " + AppInfo.Version + ").");
            sb.AppendLine("Please say who made them and where they are (the ICAO code or the name of the place).");
            sb.AppendLine();
            sb.AppendLine("local-waypoints.json");
            sb.AppendLine("```json");
            sb.AppendLine(Norm(ReadText(LocalWaypointsPath)).Trim());
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("local-db.json");
            sb.AppendLine("```json");
            sb.AppendLine(Norm(ReadText(LocalDbPath)).Trim());
            sb.AppendLine("```");
            return sb.ToString();
        }
    }
}
