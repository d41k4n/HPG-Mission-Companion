// HPG Mission Companion - profiles of the settings (the user's request of 2026-10-12): a default profile for the setup in one click,
// save and load of the settings to a file, and the setup of what a profile needs (Piper engine, Piper voices) from the same code
// for the window (Service tab), the command line (--oneclick) and the installer.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace HPGMissionCompanion
{
    // What a profile holds: the choices that make the voices and the levels (the same names as the settings file, so a copy of
    // HPGMissionCompanion.json is a valid profile). It never holds what belongs to this PC: folders, the mission file, the audio devices,
    // the port, the update address and time. A field that is not in the file takes the value below.
    internal sealed class ProfileData
    {
        public int Schema = 1;
        public string Name = "";
        public string Description = "";
        public string DefaultLanguage = "EN";
        public int MasterVolume = 100;
        public RadioSettings Radio = new RadioSettings();
        public AudioOutSettings AudioOut = new AudioOutSettings();       // only the levels are taken; the devices stay the ones of this PC
        public bool UseMissionVolumes = true;
        public int AttenCamInside = 9;
        public int AttenCamOutside = 12;
        public string BridgePack = "";
        public string Variety = "random";
        public string Names = "functions";
        public string CallSignMode = "airline";          // airline | custom
        public string CallSign = "";                      // the custom name (kept only in a personal file)
        public Dictionary<string, string> CrewNames = new Dictionary<string, string>();     // empty: the names of this PC are kept
        public bool StartWithSim = false;
        public bool ExitWithSim = true;
        public bool StartMinimized = true;
        public bool ReadyAnnounce = true;
        public bool AutoAudioInhibit = true;
        public bool AutoIndex = true;
        public bool AutoUpdateCheck = true;
        public bool UpdatePack = true;
        public string UpdateWhat = "both";
        public int OverlapMode = 1;                      // voices at the same time: 0 one at a time, 1 radio and crew take turns, 2 everything together
        public bool SharedAutoSync = true;               // the waypoint table of the mission; the change of the offline databases (SharedDbPatch) is never in a profile: it edits files outside the program
        public bool ShowAdvanced = false;
        public Dictionary<string, LangSetting> Languages = new Dictionary<string, LangSetting>();   // only the languages that the profile sets
    }

    // What a profile needs that this PC does not have yet.
    internal sealed class ProfileNeeds
    {
        public bool Engine;                                                  // the Piper engine is missing and a voice of the profile needs it
        public List<CatalogVoice> Voices = new List<CatalogVoice>();         // Piper voices to download
        public List<string> Unknown = new List<string>();                    // Piper voices that are not in any list: they cannot be downloaded
        public long Bytes;

        public bool Any { get { return Engine || Voices.Count > 0; } }

        public string Describe()
        {
            StringBuilder sb = new StringBuilder();
            if (Engine) sb.Append("the Piper engine (" + (PiperInstaller.EngineSize / 1048576) + " MB)");
            foreach (CatalogVoice c in Voices)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append("the voice " + c.Key + " (" + (c.OnnxSize / 1048576) + " MB)");
            }
            if (sb.Length == 0) return "nothing to download";
            return sb.ToString() + ": about " + ((Bytes + 1048575) / 1048576) + " MB in all";
        }
    }

    internal static class Profiles
    {
        public const string ResourceName = "profile.Recommended.json";

        public static string ProfilesDir { get { return Path.Combine(Ctx.BaseDir, "profiles"); } }
        public static string SettingsBackupDir { get { return Path.Combine(Path.Combine(Ctx.BaseDir, "backups"), "settings"); } }

        // ---- the profile as a file ---------------------------------------------------------------
        public static ProfileData Recommended()
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
            {
                if (s == null) throw new InvalidOperationException("the recommended profile is not part of this program");
                using (StreamReader r = new StreamReader(s, Encoding.UTF8)) return Parse(r.ReadToEnd());
            }
        }

        public static ProfileData Parse(string json)
        {
            if (json != null && json.Length > 0 && json[0] == '\uFEFF') json = json.Substring(1);
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            Dictionary<string, object> raw;
            try { raw = js.DeserializeObject(json) as Dictionary<string, object>; }
            catch (Exception ex) { throw new InvalidDataException("not a settings file (" + ex.Message + ")"); }
            if (raw == null || !(raw.ContainsKey("Schema") || raw.ContainsKey("Languages") || raw.ContainsKey("Radio")))
                throw new InvalidDataException("not a settings profile of the HPG Mission Companion");
            ProfileData p = js.Deserialize<ProfileData>(json);
            if (p.Schema > 1) throw new InvalidDataException("this profile was made by a newer version of the program");
            Normalize(p);
            return p;
        }

        static void Normalize(ProfileData p)
        {
            if (p.Name == null) p.Name = "";
            if (p.Description == null) p.Description = "";
            if (LangInfo.ByCode(p.DefaultLanguage) == null) p.DefaultLanguage = "EN";
            p.MasterVolume = Math.Max(0, Math.Min(100, p.MasterVolume));
            if (p.Radio == null) p.Radio = new RadioSettings();
            p.Radio.NoiseLevel = Math.Max(0, Math.Min(100, p.Radio.NoiseLevel));
            p.Radio.RadioNoiseLevel = Math.Max(0, Math.Min(100, p.Radio.RadioNoiseLevel));
            p.Radio.ExtRadioNoiseLevel = Math.Max(0, Math.Min(100, p.Radio.ExtRadioNoiseLevel));
            if (p.AudioOut == null) p.AudioOut = new AudioOutSettings();
            p.AudioOut.Normalize();
            if (p.CrewNames == null) p.CrewNames = new Dictionary<string, string>();
            if (p.Languages == null) p.Languages = new Dictionary<string, LangSetting>();
            if (p.Variety != "coherent") p.Variety = "random";
            if (p.Names != "random") p.Names = "functions";
            if (p.UpdateWhat != "mission" && p.UpdateWhat != "app") p.UpdateWhat = "both";
            if (p.BridgePack == null) p.BridgePack = "";
            if (p.OverlapMode < 0 || p.OverlapMode > 2) p.OverlapMode = 1;
            p.AttenCamInside = Math.Max(0, Math.Min(30, p.AttenCamInside));
            p.AttenCamOutside = Math.Max(0, Math.Min(30, p.AttenCamOutside));
            List<string> drop = new List<string>();
            foreach (KeyValuePair<string, LangSetting> kv in p.Languages)
                if (kv.Value == null || LangInfo.ByCode(kv.Key) == null) drop.Add(kv.Key);
            foreach (string k in drop) p.Languages.Remove(k);
            foreach (LangSetting ls in p.Languages.Values)
            {
                if (ls.Actors == null) ls.Actors = new List<ActorSetting>();
                while (ls.Actors.Count < Actors.Count) ls.Actors.Add(new ActorSetting());
                if (ls.Actors.Count > Actors.Count) ls.Actors.RemoveRange(Actors.Count, ls.Actors.Count - Actors.Count);
                foreach (ActorSetting a in ls.Actors)
                {
                    if (a == null) continue;
                    if (a.Speed < 0.5 || a.Speed > 3.0) a.Speed = 1.0;
                }
            }
        }

        public static string ToJson(ProfileData p)
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            return Indent(js.Serialize(p));
        }

        // the profile files are meant to be read and edited: one value on each line
        static string Indent(string json)
        {
            StringBuilder sb = new StringBuilder();
            int level = 0;
            bool inString = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) { sb.Append(json[++i]); continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inString = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { sb.Append(c).Append(json[++i]); break; }
                        level++; sb.Append(c).Append("\r\n").Append(' ', level * 2); break;
                    case '}':
                    case ']': level--; sb.Append("\r\n").Append(' ', level * 2).Append(c); break;
                    case ',': sb.Append(c).Append("\r\n").Append(' ', level * 2); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        // the current settings as a profile; personal = also the crew names typed by the user
        public static ProfileData FromSettings(Settings s, string name, string description, bool personal)
        {
            ProfileData p = new ProfileData();
            p.Name = name; p.Description = description;
            p.DefaultLanguage = s.DefaultLanguage; p.MasterVolume = s.MasterVolume;
            p.Radio = new RadioSettings();
            p.Radio.NoiseLevel = s.Radio.NoiseLevel; p.Radio.RadioNoiseLevel = s.Radio.RadioNoiseLevel; p.Radio.ExtRadioNoiseLevel = s.Radio.ExtRadioNoiseLevel; p.Radio.Beep = s.Radio.Beep; p.Radio.BeepLevel = s.Radio.BeepLevel; p.Radio.Tail = s.Radio.Tail; p.Radio.Quality = s.Radio.Quality;
            p.AudioOut = new AudioOutSettings();       // the devices are left empty: they belong to this PC
            p.AudioOut.HeadsetLevel = s.AudioOut.HeadsetLevel; p.AudioOut.ExternalLevel = s.AudioOut.ExternalLevel; p.AudioOut.NotifyLevel = s.AudioOut.NotifyLevel;
            p.UseMissionVolumes = s.UseMissionVolumes; p.AttenCamInside = s.AttenCamInside; p.AttenCamOutside = s.AttenCamOutside;
            p.BridgePack = s.BridgePack; p.Variety = s.Variety; p.Names = s.Names;
            p.CallSignMode = s.CallSignMode; p.CallSign = personal ? s.CallSign : "";
            p.CrewNames = new Dictionary<string, string>();
            if (personal) foreach (KeyValuePair<string, string> kv in s.CrewNames) p.CrewNames[kv.Key] = kv.Value;
            p.StartWithSim = s.StartWithSim; p.ExitWithSim = s.ExitWithSim; p.StartMinimized = s.StartMinimized; p.ReadyAnnounce = s.ReadyAnnounce;
            p.AutoAudioInhibit = s.AutoAudioInhibit; p.AutoIndex = s.AutoIndex; p.AutoUpdateCheck = s.AutoUpdateCheck; p.UpdatePack = s.UpdatePack; p.UpdateWhat = s.UpdateWhat;
            p.ShowAdvanced = s.ShowAdvanced;
            p.SharedAutoSync = s.SharedAutoSync;
            p.OverlapMode = s.OverlapMode;
            p.Languages = new Dictionary<string, LangSetting>();
            foreach (KeyValuePair<string, LangSetting> kv in s.Languages)
            {
                LangSetting ls = new LangSetting();
                foreach (ActorSetting a in kv.Value.Actors) ls.Actors.Add(a.Copy());
                p.Languages[kv.Key] = ls;
            }
            Normalize(p);
            return p;
        }

        // copies the profile into the settings (not the paths, the devices, the port and the update data of this PC); returns what it did
        public static List<string> Apply(ProfileData p, Settings s)
        {
            List<string> notes = new List<string>();
            s.DefaultLanguage = p.DefaultLanguage; s.MasterVolume = p.MasterVolume;
            s.Radio.NoiseLevel = p.Radio.NoiseLevel; s.Radio.RadioNoiseLevel = p.Radio.RadioNoiseLevel; s.Radio.ExtRadioNoiseLevel = p.Radio.ExtRadioNoiseLevel; s.Radio.Beep = p.Radio.Beep; s.Radio.BeepLevel = p.Radio.BeepLevel; s.Radio.Tail = p.Radio.Tail; s.Radio.Quality = p.Radio.Quality;
            s.AudioOut.HeadsetLevel = p.AudioOut.HeadsetLevel; s.AudioOut.ExternalLevel = p.AudioOut.ExternalLevel; s.AudioOut.NotifyLevel = p.AudioOut.NotifyLevel;
            s.UseMissionVolumes = p.UseMissionVolumes; s.AttenCamInside = p.AttenCamInside; s.AttenCamOutside = p.AttenCamOutside;
            s.BridgePack = p.BridgePack; s.Variety = p.Variety; s.Names = p.Names;
            s.CallSignMode = p.CallSignMode == "custom" ? "custom" : "airline";
            if (!string.IsNullOrEmpty(p.CallSign)) s.CallSign = p.CallSign;
            if (p.CrewNames.Count > 0)
            {
                s.CrewNames = new Dictionary<string, string>();
                foreach (KeyValuePair<string, string> kv in p.CrewNames) s.CrewNames[kv.Key] = kv.Value;
                notes.Add("crew names: " + p.CrewNames.Count);
            }
            s.StartWithSim = p.StartWithSim; s.ExitWithSim = p.ExitWithSim; s.StartMinimized = p.StartMinimized; s.ReadyAnnounce = p.ReadyAnnounce;
            s.AutoAudioInhibit = p.AutoAudioInhibit; s.AutoIndex = p.AutoIndex; s.AutoUpdateCheck = p.AutoUpdateCheck; s.UpdatePack = p.UpdatePack; s.UpdateWhat = p.UpdateWhat;
            s.ShowAdvanced = p.ShowAdvanced;
            s.SharedAutoSync = p.SharedAutoSync;
            s.OverlapMode = p.OverlapMode; s.OverlapLanes = p.OverlapMode != 0;
            foreach (KeyValuePair<string, LangSetting> kv in p.Languages)
            {
                LangSetting ls = new LangSetting();
                foreach (ActorSetting a in kv.Value.Actors) ls.Actors.Add(a.Copy());
                s.Languages[kv.Key] = ls;
                notes.Add("voices and speeds of the language " + kv.Key);
            }
            s.Normalize();
            return notes;
        }

        // ---- what the profile needs --------------------------------------------------------------
        // "piper:en_US-ryan-high" or "piper:en_GB-aru-medium:02" -> the key of the voice
        public static List<string> PiperKeys(ProfileData p)
        {
            List<string> keys = new List<string>();
            foreach (LangSetting ls in p.Languages.Values)
                foreach (ActorSetting a in ls.Actors)
                {
                    if (a == null) continue;
                    List<string> ids = new List<string>();
                    if (a.Voice != null) ids.Add(a.Voice);
                    if (a.Pool != null) ids.AddRange(a.Pool);
                    foreach (string id in ids)
                    {
                        if (id == null || !id.StartsWith("piper:", StringComparison.Ordinal)) continue;
                        string key = id.Substring(6);
                        int colon = key.IndexOf(':');
                        if (colon > 0) key = key.Substring(0, colon);
                        if (!keys.Contains(key)) keys.Add(key);
                    }
                }
            return keys;
        }

        static List<CatalogVoice> Catalog(Action<string> log)
        {
            string json = null;
            if (File.Exists(Ctx.CatalogPath)) json = File.ReadAllText(Ctx.CatalogPath, Encoding.UTF8);
            else
            {
                log("Getting the list of Piper voices");
                json = Net.GetString(PiperInstaller.CatalogUrl);
                Directory.CreateDirectory(Path.GetDirectoryName(Ctx.CatalogPath));
                File.WriteAllText(Ctx.CatalogPath, json, new UTF8Encoding(false));
            }
            return PiperInstaller.LoadCatalog(json);
        }

        public static ProfileNeeds Plan(ProfileData p, Action<string> log)
        {
            ProfileNeeds n = new ProfileNeeds();
            List<string> keys = PiperKeys(p);
            if (keys.Count == 0) return n;
            List<CatalogVoice> cat = Catalog(log);
            foreach (string key in keys)
            {
                CatalogVoice found = null;
                foreach (CatalogVoice c in cat) if (c.Key == key) found = c;
                if (found == null) foreach (CatalogVoice c in PiperInstaller.Community) if (c.Key == key) found = c;
                if (found == null) { n.Unknown.Add(key); continue; }
                if (PiperInstaller.IsInstalled(found)) continue;
                n.Voices.Add(found);
                n.Bytes += found.OnnxSize;
            }
            if (!Ctx.PiperInstalled && (n.Voices.Count > 0 || keys.Count > 0)) { n.Engine = true; n.Bytes += PiperInstaller.EngineSize; }
            return n;
        }

        // ---- the setup ---------------------------------------------------------------------------
        // report(step, done, total): the download that is running (total 0 = no size)
        public static bool Download(ProfileNeeds needs, Action<string> log, Action<string, long, long> report)
        {
            bool ok = true;
            int step = 0;
            int steps = (needs.Engine ? 1 : 0) + needs.Voices.Count;
            if (needs.Engine)
            {
                step++;
                string label = "Piper engine (" + step + " of " + steps + ")";
                log("Downloading the " + label);
                try { PiperInstaller.InstallEngine(delegate (long d, long t) { report(label, d, t); }); log("Piper engine installed"); }
                catch (Exception ex) { log("ERROR the Piper engine: " + ex.Message); ok = false; }
            }
            foreach (CatalogVoice c in needs.Voices)
            {
                step++;
                string label = "voice " + c.Key + " (" + step + " of " + steps + ")";
                log("Downloading the " + label);
                try { PiperInstaller.InstallVoice(c, delegate (long d, long t) { report(label, d, t); }); log("Voice " + c.Key + " installed"); }
                catch (Exception ex) { log("ERROR the voice " + c.Key + ": " + ex.Message); ok = false; }
            }
            return ok;
        }

        public static string BackupSettings()
        {
            if (!File.Exists(Ctx.SettingsPath)) return null;
            Directory.CreateDirectory(SettingsBackupDir);
            string dest = Path.Combine(SettingsBackupDir, "HPGMissionCompanion-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            File.Copy(Ctx.SettingsPath, dest, true);
            return dest;
        }

        // a voice that this PC does not have (a Windows voice that is not installed, a Piper voice that was not downloaded) goes back to the default of its language
        static void DropMissingVoices(Settings s, Action<string> log)
        {
            VoiceLibrary.Refresh();
            foreach (KeyValuePair<string, LangSetting> kv in s.Languages)
                for (int i = 0; i < kv.Value.Actors.Count; i++)
                {
                    ActorSetting a = kv.Value.Actors[i];
                    if (string.IsNullOrEmpty(a.Voice) || VoiceLibrary.ById(Ctx.Voices, a.Voice) != null) continue;
                    log("Voice not available on this PC, the default is used: " + kv.Key + " actor " + (i + 1) + " (" + a.Voice + ")");
                    a.Voice = null;
                }
        }

        // the entry of the simulators follows the setting (the same thing the Service tab checkbox does)
        static void SyncAutostart(Settings s, string simRoot, Action<string> log)
        {
            List<string> a = new List<string>();
            if (s.StartMinimized) a.Add("--minimized");
            if (s.ExitWithSim) a.Add("--exit-with-sim");
            string cmd = string.Join(" ", a.ToArray());
            foreach (SimTarget t in SimAutostart.Detect(simRoot))
            {
                try
                {
                    if (s.StartWithSim) SimAutostart.Enable(t, Application.ExecutablePath, cmd); else SimAutostart.Disable(t);
                    log("Start with " + t.Name + ": " + (s.StartWithSim ? "on" : "off"));
                }
                catch (Exception ex) { log("Start with " + t.Name + " not changed: " + ex.Message); }
            }
        }

        // after the downloads: backup of the settings, the profile applied, voices that are not there dropped, settings saved, start with the simulator
        public static void Finish(ProfileData p, string simRoot, Action<string> log)
        {
            string backup = BackupSettings();
            if (backup != null) log("The settings of before are kept in " + backup);
            foreach (string note in Apply(p, Ctx.Settings)) log("Applied: " + note);
            DropMissingVoices(Ctx.Settings, log);
            Ctx.Settings.Save(Ctx.SettingsPath);
            log("Settings saved");
            SyncAutostart(Ctx.Settings, simRoot, log);
        }

        // the command line: --oneclick <report> [--profile <file>] [--no-download]; the report grows line by line (the installer shows it)
        public static int RunCli(string reportPath, string profilePath, bool download, string simRoot)
        {
            StringBuilder all = new StringBuilder();
            Action<string> log = delegate (string line)
            {
                all.AppendLine(line);
                try { File.AppendAllText(reportPath, line + Environment.NewLine, new UTF8Encoding(false)); } catch (Exception) { }
            };
            try { File.WriteAllText(reportPath, "", new UTF8Encoding(false)); } catch (Exception) { }
            try
            {
                ProfileData p = profilePath != null ? Parse(File.ReadAllText(profilePath, Encoding.UTF8)) : Recommended();
                log("Profile: " + (p.Name.Length > 0 ? p.Name : "(no name)"));
                ProfileNeeds needs = null;
                try { needs = Plan(p, log); }
                catch (Exception ex) { log("ERROR the list of voices could not be read (" + ex.Message + "): the voices are not downloaded"); }
                bool ok = true;
                if (needs != null)
                {
                    log("Needs: " + needs.Describe());
                    foreach (string u in needs.Unknown) log("Not in any list, not downloaded: " + u);
                    if (download && needs.Any)
                    {
                        long lastMb = -1;
                        ok = Download(needs, log, delegate (string label, long d, long t)
                        {
                            long mb = d / (25 * 1048576);
                            if (mb != lastMb) { lastMb = mb; log("  " + label + ": " + (d / 1048576) + " of " + (t / 1048576) + " MB"); }
                        });
                    }
                    else if (needs.Any) log("Downloads skipped");
                }
                else ok = false;
                Finish(p, simRoot, log);
                log(ok ? "Setup done." : "Setup done, but something could not be downloaded: run Set up in one click again in the program when the connection works.");
                return ok ? 0 : 2;
            }
            catch (Exception ex)
            {
                log("ERROR " + ex.Message);
                return 1;
            }
        }
    }

    // The window of a setup that downloads: what is running, a bar and the lines of the log. It closes only when the work ends.
    internal sealed class ProfileProgressForm : Form
    {
        readonly Label step = new Label();
        readonly ProgressBar bar = new ProgressBar();
        readonly TextBox box = new TextBox();
        readonly Button close = new Button();
        bool finished;

        public ProfileProgressForm(string title)
        {
            Text = title;
            ClientSize = new Size(560, 330);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);
            step.SetBounds(14, 12, 532, 20);
            step.Text = "Preparing...";
            Controls.Add(step);
            bar.SetBounds(14, 36, 532, 20);
            Controls.Add(bar);
            box.Multiline = true;
            box.ReadOnly = true;
            box.ScrollBars = ScrollBars.Vertical;
            box.SetBounds(14, 68, 532, 202);
            Controls.Add(box);
            close.Text = "Close";
            close.Enabled = false;
            close.SetBounds(456, 284, 90, 28);
            close.Click += delegate { Close(); };
            Controls.Add(close);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!finished) e.Cancel = true;          // the downloads cannot be left half way from here
            base.OnFormClosing(e);
        }

        public void AddLine(string line)
        {
            if (IsDisposed) return;
            BeginInvoke((MethodInvoker)delegate { box.AppendText(line + Environment.NewLine); });
        }

        public void SetProgress(string label, long done, long total)
        {
            if (IsDisposed) return;
            BeginInvoke((MethodInvoker)delegate
            {
                step.Text = label + (total > 0 ? ": " + (done / 1048576) + " of " + (total / 1048576) + " MB" : "");
                bar.Value = total > 0 ? (int)Math.Min(100, done * 100 / total) : 0;
            });
        }

        public void Finished(string message)
        {
            if (IsDisposed) return;
            BeginInvoke((MethodInvoker)delegate
            {
                finished = true;
                step.Text = message;
                bar.Value = 100;
                close.Enabled = true;
            });
        }
    }
}
