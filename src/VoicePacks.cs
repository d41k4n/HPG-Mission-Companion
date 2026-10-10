// HPG Mission Companion - voice packs: XML files in the voicepacks folder with the texts of the mission messages (several options each),
// and the <say:KEY> tag family that the missions use to speak them. See docs/architecture/VOICEPACKS.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace HPGMissionCompanion
{
    internal sealed class PackMessage
    {
        public string Key;
        public int Actor = 1;
        public string Radio = "auto";           // auto: the mission decides with its own <radio> tags; on / off: the pack decides when the mission does not
        public string Group = "";               // conversation: with the coherent variety the lines of a group use the same option number
        public int Seq;
        public string Lvar = "";                // the LVAR of the wav voice packs (information)
        public string Wav = "";                 // the wav of the same message (information, or the file played by a sound-only message)
        public double Sleep = -1;               // seconds the mission waits after the message (information)
        public string Trigger = "";             // when the message happens (information)
        public bool Sfx;                        // a sound without text: the wav is played from the audio folder
        public List<string> Options = new List<string>();
    }

    internal sealed class VoicePack
    {
        public string Name = "";
        public string Language = "EN";
        public string Title = "";
        public int Version;
        public string File = "";
        public Dictionary<string, PackMessage> Messages = new Dictionary<string, PackMessage>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<string> Problems = new List<string>();
        public bool Usable { get { return Name.Length > 0 && Version > 0; } }
        public override string ToString() { return Name + " v" + Version; }
    }

    internal static class VoicePacks
    {
        public static List<VoicePack> Packs = new List<VoicePack>();
        public static string Dir { get { return Path.Combine(Ctx.BaseDir, "voicepacks"); } }
        public static string ObsoleteDir { get { return Path.Combine(Dir, "obsolete"); } }

        public static void Refresh()
        {
            List<VoicePack> list = new List<VoicePack>();
            if (Directory.Exists(Dir))
            {
                string[] files = Directory.GetFiles(Dir, "*.xml");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (string f in files)
                {
                    VoicePack p = Load(f);
                    list.Add(p);
                    foreach (string problem in p.Problems) Ctx.Log("[WARN] voice pack " + Path.GetFileName(f) + ": " + problem);
                }
            }
            Packs = list;
            Say.Reload();
        }

        // Reads and checks a pack file; it never throws: the problems are in Problems and an unusable pack has Name "" or Version 0.
        public static VoicePack Load(string path)
        {
            VoicePack p = new VoicePack();
            p.File = path;
            XmlDocument doc = new XmlDocument();
            try { doc.Load(path); }
            catch (Exception ex) { p.Problems.Add("not a valid XML file: " + ex.Message); return p; }
            XmlElement root = doc.DocumentElement;
            if (root == null || root.Name != "voicepack") { p.Problems.Add("the root element must be <voicepack>"); return p; }
            p.Name = (root.GetAttribute("name") ?? "").Trim();
            if (p.Name.Length == 0)       // no name inside the file: the file name is the pack name (My pack.xml -> My_pack)
                p.Name = Regex.Replace(Path.GetFileNameWithoutExtension(path).Trim(), "[^A-Za-z0-9_-]+", "_").Trim('_');
            if (p.Name.Length > 24) p.Name = p.Name.Substring(0, 24);
            p.Language = (root.GetAttribute("language") ?? "EN").Trim().ToUpperInvariant();
            if (p.Language.Length == 0) p.Language = "EN";
            p.Title = (root.GetAttribute("title") ?? "").Trim();
            int v;
            if (!int.TryParse(root.GetAttribute("version"), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) || v < 1) { p.Problems.Add("version must be a whole number from 1"); v = 0; }
            p.Version = v;
            if (p.Name.Length == 0) p.Problems.Add("the pack has no name");
            else if (!Regex.IsMatch(p.Name, "^[A-Za-z0-9_-]{1,24}$")) { p.Problems.Add("the name may only have letters, digits, _ and - (up to 24)"); p.Name = ""; }
            if (LangInfo.ByCode(p.Language) == null) p.Problems.Add("language " + p.Language + " is not supported (EN, FR, IT, DE)");

            foreach (XmlNode n in root.SelectNodes("names/name"))
            {
                XmlElement e = n as XmlElement;
                if (e == null) continue;
                string role = e.GetAttribute("role").Trim();
                if (role.Length > 0) p.Names[role] = e.InnerText.Trim();
            }
            foreach (XmlNode n in root.SelectNodes("message"))
            {
                XmlElement e = n as XmlElement;
                if (e == null) continue;
                PackMessage m = new PackMessage();
                m.Key = e.GetAttribute("key").Trim();
                if (m.Key.Length == 0) { p.Problems.Add("a message has no key"); continue; }
                if (!Regex.IsMatch(m.Key, "^[A-Za-z0-9_.\\-]{1,40}$")) { p.Problems.Add("message " + m.Key + ": the key may only have letters, digits, . _ -"); continue; }
                if (p.Messages.ContainsKey(m.Key)) { p.Problems.Add("message " + m.Key + " appears twice, the first one is kept"); continue; }
                int actor;
                if (e.HasAttribute("actor"))
                {
                    if (int.TryParse(e.GetAttribute("actor"), NumberStyles.Integer, CultureInfo.InvariantCulture, out actor) && actor >= 1 && actor <= Actors.Count) m.Actor = actor;
                    else p.Problems.Add("message " + m.Key + ": actor must be 1-" + Actors.Count);
                }
                string radio = e.GetAttribute("radio").Trim().ToLowerInvariant();
                if (radio == "on" || radio == "off" || radio == "auto") m.Radio = radio;
                else if (radio.Length > 0) p.Problems.Add("message " + m.Key + ": radio must be auto, on or off");
                m.Group = e.GetAttribute("group").Trim();
                int.TryParse(e.GetAttribute("seq"), NumberStyles.Integer, CultureInfo.InvariantCulture, out m.Seq);
                m.Lvar = e.GetAttribute("lvar").Trim();
                m.Wav = e.GetAttribute("wav").Trim();
                m.Trigger = e.GetAttribute("trigger").Trim();
                double sl;
                if (double.TryParse(e.GetAttribute("sleep"), NumberStyles.Float, CultureInfo.InvariantCulture, out sl)) m.Sleep = sl;
                m.Sfx = e.GetAttribute("sfx") == "1";
                foreach (XmlNode o in e.SelectNodes("o"))
                {
                    string t = Regex.Replace(o.InnerText, "\\s+", " ").Trim();
                    if (t.Length > 0) m.Options.Add(t);
                }
                if (m.Options.Count == 0 && !(m.Sfx && m.Wav.Length > 0)) { p.Problems.Add("message " + m.Key + " has no <o> text"); continue; }
                p.Messages[m.Key] = m;
            }
            return p;
        }

        public static VoicePack Find(string name)
        {
            foreach (VoicePack p in Packs) if (p.Usable && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        // Copies a pack file into the voicepacks folder. An older pack of the same name goes to voicepacks\obsolete.
        // confirm is asked when the new version is not newer than the installed one; it returns true to replace.
        public static string Install(string path, Func<string, bool> confirm)
        {
            VoicePack np = Load(path);
            if (!np.Usable)
            {
                string why = np.Problems.Count > 0 ? np.Problems[0] : "unusable pack";
                throw new InvalidOperationException(Path.GetFileName(path) + " cannot be installed: " + why);
            }
            Directory.CreateDirectory(Dir);
            string target = Path.Combine(Dir, np.Name + ".xml");
            VoicePack old = File.Exists(target) ? Load(target) : null;
            if (old != null && old.Usable)
            {
                if (np.Version <= old.Version && !confirm(old.Name + " version " + old.Version + " is installed; this file is version " + np.Version + (np.Version == old.Version ? " (the same)." : " (older).") + "\n\nReplace it anyway?"))
                    return "Not installed: " + np.Name + " version " + old.Version + " was kept.";
                Directory.CreateDirectory(ObsoleteDir);
                string arch = Path.Combine(ObsoleteDir, old.Name + "-v" + old.Version + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".xml");
                File.Copy(target, arch, true);
            }
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) File.Copy(path, target, true);
            Refresh();
            int warnings = np.Problems.Count;
            return "Installed " + np.Name + " version " + np.Version + " (" + np.Messages.Count + " messages)" + (old != null && old.Usable ? "; version " + old.Version + " moved to the obsolete folder" : "") + (warnings > 0 ? "; " + warnings + " warning(s), see the log" : "") + ".";
        }
    }

    // ---- the tags that use the packs ------------------------------------------------------------------------
    //   <say:KEY>args      speak the message KEY of the pack; args = texts separated by // that fill {0} {1} ...  (everything after the tag)
    //   <say:KEY:3>args    the same with option 3 forced
    //   <plain>text        speak the text as it is: no tag inside it is interpreted (for free texts such as a dispatch)
    //   <pack:EN:2>        the pack the mission uses and the version it needs (status goes to L:HMC_VOICE_STATUS)
    //   <names:PLT=Mark,CPL=Anna>  names for {PLT} {CPL} {PAX1} {PAX2} {PAX3}; <names:off> goes back to the functions
    //   <variety:random> / <variety:coherent>  how the option of a message is chosen
    internal static class Say
    {
        const string Rest = "(?:\\s*[:=]\\s*([^<>\\[\\]]{0,400}))?";
        static readonly Regex ControlRx = new Regex("[<\\[]\\s*(say|pack|names|variety|plain)" + Rest + "\\s*[>\\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex ActorTagRx = new Regex("[<\\[]\\s*actor\\s*\\d", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RadioTagRx = new Regex("[<\\[]\\s*radio\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex HolderRx = new Regex("\\{([A-Za-z0-9_]+)\\}", RegexOptions.Compiled);

        static readonly string[] Roles = { "PLT", "CPL", "PAX1", "PAX2", "PAX3" };
        static readonly string[] RoleDefaults = { "Captain", "Copilot", "Chief", "Paramedic", "Doc" };

        static readonly object sync = new object();
        static Random rng = new Random();
        static string declared = "";
        static int required;
        static bool coherent;
        static readonly Dictionary<string, string> missionNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, int> lastOption = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        static string lastGroup = "";
        static DateTime lastGroupTime = DateTime.MinValue;
        static int lastGroupOption;
        static readonly HashSet<string> warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static string Declared { get { return declared; } }

        public static void Seed(int seed) { lock (sync) { rng = new Random(seed); } }

        public static void Reload()
        {
            lock (sync) { warned.Clear(); }
            if (declared.Length > 0) Declare(declared, required, false);
        }

        // ---- main entry: the text of a speak message in, the text for the tag parser out ----
        public static string Process(string text, List<string> notes, out string wavFile)
        {
            wavFile = null;
            if (string.IsNullOrEmpty(text)) return text;
            if (!ControlRx.IsMatch(text)) return text;
            lock (sync)
            {
                StringBuilder outp = new StringBuilder();
                int pos = 0;
                Match m = ControlRx.Match(text);
                while (m.Success)
                {
                    outp.Append(text, pos, m.Index - pos);
                    pos = m.Index + m.Length;
                    string kind = m.Groups[1].Value.ToLowerInvariant();
                    string arg = m.Groups[2].Success ? m.Groups[2].Value.Trim() : "";
                    if (kind == "pack") ParsePack(arg, notes);
                    else if (kind == "names") ParseNames(arg);
                    else if (kind == "variety") ParseVariety(arg, notes);
                    else if (kind == "plain")
                    {
                        outp.Append(Literal(text.Substring(pos)));
                        pos = text.Length;
                        break;
                    }
                    else   // say: the rest of the text is the argument list
                    {
                        outp.Append(Expand(arg, text.Substring(pos), outp.ToString(), notes, out wavFile));
                        pos = text.Length;
                        break;
                    }
                    m = m.NextMatch();
                }
                if (pos < text.Length) outp.Append(text, pos, text.Length - pos);
                return outp.ToString();
            }
        }

        // free text: no tag inside it works
        public static string Literal(string s)
        {
            if (s == null) return "";
            return s.Replace('<', ' ').Replace('>', ' ').Replace('[', '(').Replace(']', ')');
        }

        // ---- state tags ----
        static void ParsePack(string arg, List<string> notes)
        {
            string name = arg; int ver = 0;
            int c = arg.IndexOf(':');
            if (c >= 0)
            {
                name = arg.Substring(0, c).Trim();
                int.TryParse(arg.Substring(c + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ver);
            }
            Declare(name, ver, true);
        }

        static void Declare(string name, int need, bool announce)
        {
            declared = name; required = need;
            VoicePack p = VoicePacks.Find(name);
            int status; int version = 0;
            if (p == null)
            {
                status = 2;
                if (announce) Ctx.Log("[WARN] The mission needs the voice pack " + name + (need > 0 ? " version " + need : "") + ", which is not installed: add it in the Voice packs tab.");
            }
            else
            {
                version = p.Version;
                if (need > 0 && p.Version < need)
                {
                    status = 3;
                    if (announce) Ctx.Log("[WARN] The voice pack " + p.Name + " is version " + p.Version + " but the mission needs version " + need + ": the pack is obsolete, install a newer one.");
                }
                else
                {
                    status = 1;
                    if (announce) Ctx.Log("[INFO] Voice pack " + p.Name + " version " + p.Version + (need > 0 ? " (the mission needs " + need + ")" : "") + " in use.");
                }
            }
            SimLink.Publish(SimLink.VarStatus, status);
            SimLink.Publish(SimLink.VarPackVersion, version);
        }

        static void ParseNames(string arg)
        {
            missionNames.Clear();
            if (arg.Length == 0 || string.Equals(arg, "off", StringComparison.OrdinalIgnoreCase)) return;
            foreach (string part in arg.Split(new char[] { ',', ';' }))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string role = part.Substring(0, eq).Trim();
                string name = Literal(part.Substring(eq + 1).Trim());
                if (role.Length > 0 && name.Length > 0) missionNames[role] = name;
            }
        }

        static void ParseVariety(string arg, List<string> notes)
        {
            string a = arg.Trim().ToLowerInvariant();
            if (a == "coherent" || a == "coherence") { coherent = true; lastGroup = ""; }
            else if (a == "random" || a == "") coherent = false;
            else if (notes != null) notes.Add("variety " + arg + " does not exist (random, coherent)");
        }

        // ---- say ----
        static PackMessage FindMessage(string key, out VoicePack pack)
        {
            pack = null;
            VoicePack d = declared.Length > 0 ? VoicePacks.Find(declared) : null;
            PackMessage m;
            if (d != null && d.Messages.TryGetValue(key, out m)) { pack = d; return m; }
            foreach (VoicePack p in VoicePacks.Packs)
            {
                if (!p.Usable || p == d) continue;
                if (p.Messages.TryGetValue(key, out m)) { pack = p; return m; }
            }
            return null;
        }

        static string Expand(string spec, string args, string before, List<string> notes, out string wavFile)
        {
            wavFile = null;
            string key = spec; int forced = 0;
            int c = spec.IndexOf(':');
            if (c >= 0)
            {
                key = spec.Substring(0, c).Trim();
                int.TryParse(spec.Substring(c + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out forced);
            }
            key = key.Trim();
            VoicePack pack = null;
            PackMessage msg = null;
            if (key.Length > 0) msg = FindMessage(key, out pack);
            if (msg == null)
            {
                if (notes != null) notes.Add("voice pack message '" + key + "' not found" + (declared.Length > 0 ? " (pack " + declared + ")" : "") + ": nothing spoken");
                return "";
            }
            if (msg.Options.Count == 0)
            {
                if (msg.Wav.Length > 0) wavFile = msg.Wav;
                return "";
            }
            int index = forced >= 1 && forced <= msg.Options.Count ? forced - 1 : Pick(msg);
            string[] parts = args.Length > 0 ? args.Split(new string[] { "//" }, StringSplitOptions.None) : new string[0];
            string body = Fill(msg.Options[index], parts, pack, notes);
            StringBuilder tags = new StringBuilder();
            if (!ActorTagRx.IsMatch(before)) tags.Append("<actor").Append(msg.Actor).Append('>');
            if (!RadioTagRx.IsMatch(before))
            {
                if (msg.Radio == "on") tags.Append("<radio>");
                else if (msg.Radio == "off") tags.Append("<radio:off>");
            }
            Ctx.Log("[SAY] " + msg.Key + " -> option " + (index + 1) + " of " + msg.Options.Count + " (" + pack.Name + " v" + pack.Version + ")");
            return tags.ToString() + body;
        }

        static int Pick(PackMessage m)
        {
            int n = m.Options.Count;
            if (n == 1) return 0;
            int k;
            if (coherent && m.Group.Length > 0)
            {
                if (string.Equals(m.Group, lastGroup, StringComparison.OrdinalIgnoreCase) && (DateTime.UtcNow - lastGroupTime).TotalSeconds < 180)
                    k = Math.Min(lastGroupOption, n - 1);
                else
                    k = rng.Next(n);
                lastGroup = m.Group; lastGroupTime = DateTime.UtcNow; lastGroupOption = k;
            }
            else
            {
                int last;
                if (!lastOption.TryGetValue(m.Key, out last)) last = -1;
                k = rng.Next(n);
                if (k == last) k = (k + 1 + rng.Next(n - 1)) % n;     // never the same option twice in a row
            }
            lastOption[m.Key] = k;
            return k;
        }

        static string Fill(string option, string[] args, VoicePack pack, List<string> notes)
        {
            return HolderRx.Replace(option, delegate (Match m)
            {
                string name = m.Groups[1].Value;
                int i;
                if (int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out i))
                {
                    if (i >= 0 && i < args.Length) return Literal(args[i].Trim());
                    if (notes != null) notes.Add("the message needs argument {" + i + "} but the mission sent " + args.Length);
                    return "";
                }
                string v;
                if (missionNames.TryGetValue(name, out v)) return v;
                if (pack != null && pack.Names.TryGetValue(name, out v)) return v;
                for (int r = 0; r < Roles.Length; r++) if (string.Equals(Roles[r], name, StringComparison.OrdinalIgnoreCase)) return RoleDefaults[r];
                if (string.Equals(name, "FRA", StringComparison.OrdinalIgnoreCase)) return "Frank";
                if (string.Equals(name, "SARA", StringComparison.OrdinalIgnoreCase)) return "Sarah";
                if (notes != null) notes.Add("unknown placeholder {" + name + "}");
                return "";
            });
        }
    }
}
