// HPG Mission Companion
// A voice service for the HPG missions: text to speech with the Windows voices and with Piper, one voice per mission actor.
//
// Command line:
//   (none)                   show the window and start the service
//   --minimized              start hidden (notification area); used when the simulator starts the program
//   --exit-with-sim          leave when Microsoft Flight Simulator closes
//   --silent                 do everything but the sound (for checks)
//   --log-file <path>        also write the log to a file
//   --port <n>  --audio <dir>
//   --list-file <path>       write the voices to a file and exit
//   --selftest <wav> --text "<tagged text>"   synthesize the first part of the text to a wav file and exit
//   --autostart on|off       add or remove this program in the EXE.xml of the simulators found (and exit)
//   --expand <report> --text "<tagged text>" | --lines <file> [--repeat N] [--seed N]   (lines: @watch NAME VALUE, @settings k=v) what a mission text becomes (voice pack expansion and tags), no audio
//   --install-pack <xml>     install a voice pack (an older version of the same pack is archived in voicepacks\obsolete)
//   --packs-check <report>   list the voice packs of the voicepacks folder, their problems and what they lack against the base pack EN
//   --pack-template <xml> --lang IT [--name IT] [--report <file>]   write the base pack as the start of a translation
//   --pack-add-missing <pack> [--report <file>]   add the English texts of the keys that a pack lacks to its file (the old file goes to voicepacks\obsolete)
//   --simlink-check <report> find SimConnect.dll, try one connection, write the result and exit
//   --sim-root <dir>       (test) use this folder instead of the simulator folders for the start with the simulator
//   --test-autostart <on|off> (with --sim-root) add or remove the entry and exit
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// keep in step with AppInfo.Version in Core.cs
[assembly: AssemblyTitle("HPG Mission Companion")]
[assembly: AssemblyProduct("HPG Mission Companion")]
[assembly: AssemblyVersion("0.17.0.0")]
[assembly: AssemblyFileVersion("0.17.0.0")]

namespace HPGMissionCompanion
{
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            string logFile = Option(args, "--log-file");
            string listFile = Option(args, "--list-file");
            string selftest = Option(args, "--selftest");
            string simRoot = Option(args, "--sim-root");
            string autostartTest = Option(args, "--test-autostart");
            Ctx.Silent = Has(args, "--silent");
            Ctx.Settings = Settings.Load(Ctx.SettingsPath);
            string portArg = Option(args, "--port");
            int port;
            if (portArg != null && int.TryParse(portArg, out port)) Ctx.Settings.Port = port;
            string audioArg = Option(args, "--audio");
            Ctx.AudioDir = audioArg != null ? audioArg : (string.IsNullOrEmpty(Ctx.Settings.AudioFolder) ? Path.Combine(Ctx.BaseDir, "audio") : Ctx.Settings.AudioFolder);

            if (autostartTest != null && simRoot != null)
            {
                foreach (SimTarget t in SimAutostart.Detect(simRoot))
                {
                    if (autostartTest == "on") SimAutostart.Enable(t, Application.ExecutablePath, "--minimized --exit-with-sim");
                    else SimAutostart.Disable(t);
                }
                return 0;
            }

            if (Has(args, "--install-engine") || Option(args, "--install-voice") != null)
            {
                StringBuilder rep = new StringBuilder();
                string reportPath = Option(args, "--report") ?? Path.Combine(Ctx.BaseDir, "install-report.txt");
                int rc = 0;
                try
                {
                    long lastShown = -1;
                    Action<long, long> prog = delegate (long done, long total)
                    {
                        long mb = done / (4 * 1048576);
                        if (mb != lastShown) { lastShown = mb; rep.AppendLine("  " + (done / 1048576) + " of " + (total / 1048576) + " MB"); }
                    };
                    if (Has(args, "--install-engine"))
                    {
                        rep.AppendLine("installing the engine");
                        PiperInstaller.InstallEngine(prog);
                        rep.AppendLine("engine installed: " + Ctx.PiperExe + " exists=" + Ctx.PiperInstalled);
                    }
                    string vkey = Option(args, "--install-voice");
                    if (vkey != null)
                    {
                        string json = File.Exists(Ctx.CatalogPath) ? File.ReadAllText(Ctx.CatalogPath, Encoding.UTF8) : Net.GetString(PiperInstaller.CatalogUrl);
                        Directory.CreateDirectory(Path.GetDirectoryName(Ctx.CatalogPath));
                        File.WriteAllText(Ctx.CatalogPath, json, new UTF8Encoding(false));
                        CatalogVoice found = null;
                        foreach (CatalogVoice c in PiperInstaller.LoadCatalog(json)) if (c.Key == vkey) found = c;
                        if (found == null) throw new InvalidOperationException("voice " + vkey + " not in the catalog");
                        rep.AppendLine("installing the voice " + vkey);
                        PiperInstaller.InstallVoice(found, prog);
                        rep.AppendLine("voice installed: " + PiperInstaller.IsInstalled(found));
                    }
                }
                catch (Exception ex) { rep.AppendLine("ERROR: " + ex); rc = 1; }
                File.WriteAllText(reportPath, rep.ToString());
                return rc;
            }

            // --autostart on|off: adds or removes this program in the EXE.xml of the simulators found (same as the Service tab checkbox)
            string autostart = Option(args, "--autostart");
            if (autostart != null)
            {
                StringBuilder rep = new StringBuilder();
                string reportPath = Option(args, "--report") ?? Path.Combine(Ctx.BaseDir, "autostart-report.txt");
                List<string> a = new List<string>();
                if (Ctx.Settings.StartMinimized) a.Add("--minimized");
                if (Ctx.Settings.ExitWithSim) a.Add("--exit-with-sim");
                string cmdline = string.Join(" ", a.ToArray());
                foreach (SimTarget t in SimAutostart.Detect(simRoot))
                {
                    try
                    {
                        if (autostart == "on") SimAutostart.Enable(t, Application.ExecutablePath, cmdline); else SimAutostart.Disable(t);
                        rep.AppendLine(autostart + ": " + t.ExeXml + " (" + cmdline + ")");
                    }
                    catch (Exception ex) { rep.AppendLine("ERROR " + t.ExeXml + ": " + ex.Message); }
                }
                if (autostart == "on") { Ctx.Settings.StartWithSim = true; Ctx.Settings.Save(Ctx.SettingsPath); }
                File.WriteAllText(reportPath, rep.ToString());
                return 0;
            }
            // tools, for checks: --scan-work <dir> | --clean-work <dir> --groups debug,test [--no-backup] | --run-index <cmd>   (all with --report <path>)
            string scanWork = Option(args, "--scan-work");
            string cleanWork = Option(args, "--clean-work");
            string runIndex = Option(args, "--run-index");
            if (scanWork != null || cleanWork != null || runIndex != null)
            {
                StringBuilder rep = new StringBuilder();
                string reportPath = Option(args, "--report") ?? Path.Combine(Ctx.BaseDir, "tools-report.txt");
                int rc = 0;
                try
                {
                    if (scanWork != null || cleanWork != null)
                    {
                        string dir = scanWork ?? cleanWork;
                        List<CleanGroup> groups = Tools.Scan(dir);
                        foreach (CleanGroup g in groups) rep.AppendLine("group " + g.Key + " default=" + g.Default + " | " + g.ToString());
                        if (cleanWork != null)
                        {
                            string want = "," + (Option(args, "--groups") ?? "") + ",";
                            List<string> files = new List<string>();
                            foreach (CleanGroup g in groups) if (want.IndexOf("," + g.Key + ",", StringComparison.Ordinal) >= 0) files.AddRange(g.Files);
                            if (!Has(args, "--no-backup")) rep.AppendLine("backup: " + Tools.Backup(dir, files, Path.Combine(dir, "..\\backup-test")));
                            List<string> failed = new List<string>();
                            int n = Tools.Delete(files, failed);
                            rep.AppendLine("deleted " + n + " of " + files.Count + "; failed " + failed.Count);
                        }
                    }
                    if (runIndex != null)
                    {
                        int exit = Tools.RunIndex(runIndex, delegate (string line) { rep.AppendLine("> " + line); });
                        rep.AppendLine("exit code " + exit);
                        rep.AppendLine("index file: " + Tools.IndexFileOf(runIndex));
                    }
                }
                catch (Exception ex) { rep.AppendLine("ERROR: " + ex); rc = 1; }
                File.WriteAllText(reportPath, rep.ToString());
                return rc;
            }

            // --check-updates <report> [--community <dir>] [--index-file <file>] [--mission-url <url>] [--github-mission <api url>] [--github-app <api url>]: the reads of the Check for updates button, no download
            // --test-replace <installed> --with <file> --backup <dir> [--report <file>]: the replacement of the mission file (check it on a dummy copy)
            string checkUpdates = Option(args, "--check-updates");
            string testReplace = Option(args, "--test-replace");
            if (checkUpdates != null || testReplace != null)
            {
                StringBuilder rep = new StringBuilder();
                int rc = 0;
                try
                {
                    if (testReplace != null)
                    {
                        string backup = Updates.ReplaceInstalled(testReplace, Option(args, "--with"), Option(args, "--backup"));
                        rep.AppendLine("replaced " + testReplace + "; backup " + backup);
                        string t, i;
                        Updates.ParseHead(Updates.ReadHead(testReplace), out t, out i);
                        rep.AppendLine("now: " + t);
                        File.WriteAllText(Option(args, "--report") ?? testReplace + ".report.txt", rep.ToString());
                        return 0;
                    }
                    string community = Option(args, "--community");
                    List<string> roots = community != null ? new List<string>(new string[] { community }) : Tools.CommunityRoots();
                    List<MissionCopy> copies = Updates.FindInstalled(roots, null);
                    rep.AppendLine("installed copies: " + copies.Count);
                    foreach (MissionCopy c in copies) rep.AppendLine("  " + c + " | " + c.Size + " bytes | " + c.Path);
                    string indexFile = Option(args, "--index-file");
                    if (indexFile != null)
                    {
                        foreach (MissionRelease r in Updates.ReadIndexReleases(indexFile)) rep.AppendLine("index lists: " + r);
                    }
                    string url = Option(args, "--mission-url");
                    string ghMission = Option(args, "--github-mission");
                    if (ghMission != null)
                    {
                        try { RemoteMission m = Updates.ReadRemoteMissionGitHub(ghMission); rep.AppendLine("github mission: " + m.Release + " size " + m.Size + " sha256 " + m.Sha256 + " " + m.Url); }
                        catch (Exception ex) { rep.AppendLine("github mission: " + ex.Message); }
                    }
                    if (url != null)
                    {
                        try { RemoteMission m = Updates.ReadRemoteMission(url); rep.AppendLine("link mission: " + m.Release + " size " + m.Size + " (" + m.Source + ")"); }
                        catch (Exception ex) { rep.AppendLine("link mission: " + ex.Message); }
                    }
                    string ghApp = Option(args, "--github-app");
                    if (ghApp != null)
                    {
                        try { RemoteApp a = Updates.ReadRemoteApp(ghApp); rep.AppendLine("github app: " + a.Tag + " newer=" + Updates.AppIsNewer(a) + " asset " + a.AssetName + " size " + a.AssetSize + " sha256 " + a.Sha256); }
                        catch (Exception ex) { rep.AppendLine("github app: " + ex.Message); }
                    }
                }
                catch (Exception ex) { rep.AppendLine("ERROR: " + ex); rc = 1; }
                File.WriteAllText(checkUpdates ?? (testReplace + ".report.txt"), rep.ToString());
                return rc;
            }

            // --simlink-check <report>: finds SimConnect.dll and tries to connect once (without a simulator it must report "waiting")
            string simlinkCheck = Option(args, "--simlink-check");
            if (simlinkCheck != null)
            {
                StringBuilder rep = new StringBuilder();
                string dll = SimLink.FindDll();
                rep.AppendLine("SimConnect.dll: " + (dll ?? "not found"));
                if (dll != null)
                {
                    Ctx.Log = delegate (string s) { rep.AppendLine(s); };
                    SimLink.Start(delegate { return true; });
                    Thread.Sleep(3500);
                    SimLink.Stop();
                    rep.AppendLine("status: " + SimLink.Status);
                }
                File.WriteAllText(simlinkCheck, rep.ToString());
                return dll != null ? 0 : 1;
            }

            // --install-pack <xml> [--report <file>]: install a voice pack into the voicepacks folder (the old version of the same pack goes to voicepacks\obsolete)
            string installPack = Option(args, "--install-pack");
            if (installPack != null)
            {
                string reportPath = Option(args, "--report") ?? Path.Combine(Ctx.BaseDir, "install-pack-report.txt");
                string msg;
                int rc = 0;
                try { msg = VoicePacks.Install(installPack, delegate (string q) { return true; }); }
                catch (Exception ex) { msg = "ERROR: " + ex.Message; rc = 1; }
                File.WriteAllText(reportPath, msg + Environment.NewLine);
                return rc;
            }

            // --packs-check <report>: lists the voice packs of the voicepacks folder with their problems
            string packsCheck = Option(args, "--packs-check");
            if (packsCheck != null)
            {
                StringBuilder rep = new StringBuilder();
                VoicePacks.Refresh();
                rep.AppendLine("folder: " + VoicePacks.Dir);
                VoicePack baseRef = PackCheck.Reference();
                foreach (VoicePack p in VoicePacks.Packs)
                {
                    rep.AppendLine("pack " + (p.Name.Length > 0 ? p.Name : "(no name)") + " version " + p.Version + " language " + p.Language + " messages " + p.Messages.Count + (p.BaseVer > 0 ? " basever " + p.BaseVer : "") + " file " + Path.GetFileName(p.File));
                    foreach (string problem in p.Problems) rep.AppendLine("  problem: " + problem);
                    if (!p.Usable || baseRef == null) continue;
                    PackReport pr = PackCheck.Compare(p, baseRef);
                    rep.AppendLine("  against " + PackCheck.BaseName + " " + baseRef.Version + ": " + (p == baseRef ? "the base pack" : pr.Summary()) + " [level " + pr.Level + "]");
                    for (int i = 0; i < pr.Missing.Count && i < 8; i++) rep.AppendLine("  missing: " + pr.Missing[i]);
                    for (int i = 0; i < pr.Values.Count && i < 8; i++) rep.AppendLine("  values differ: " + pr.Values[i]);
                }
                File.WriteAllText(packsCheck, rep.ToString());
                return 0;
            }

            // --pack-template <out.xml> --lang IT [--name IT] [--report <file>]: a translation of the base pack as a new file
            // --pack-add-missing <pack name> [--report <file>]: adds the English texts of the keys that a pack lacks to its file
            string packTemplate = Option(args, "--pack-template");
            string packAdd = Option(args, "--pack-add-missing");
            if (packTemplate != null || packAdd != null)
            {
                string reportPath = Option(args, "--report") ?? Path.Combine(Ctx.BaseDir, "pack-tools-report.txt");
                string msg;
                int rc = 0;
                try
                {
                    VoicePacks.Refresh();
                    if (packTemplate != null)
                    {
                        string lang = Option(args, "--lang") ?? "";
                        msg = PackCheck.ExportTemplate(PackCheck.Reference(), Option(args, "--name") ?? lang, lang, packTemplate);
                    }
                    else
                    {
                        VoicePack target = VoicePacks.Find(packAdd);
                        if (target == null) throw new InvalidOperationException("The pack " + packAdd + " is not installed.");
                        msg = PackCheck.AddMissing(target, PackCheck.Reference());
                    }
                }
                catch (Exception ex) { msg = "ERROR: " + ex.Message; rc = 1; }
                File.WriteAllText(reportPath, msg + Environment.NewLine);
                return rc;
            }

            // --audio-devices <report>: the output devices of Windows and a silent test of each one (0.3 s of silence, nothing is heard): the open path of the audio output
            string devReport = Option(args, "--audio-devices");
            if (devReport != null)
            {
                StringBuilder rep = new StringBuilder();
                Ctx.Log = delegate (string s) { rep.AppendLine("log: " + s); };
                byte[] silence = new byte[44 + 13230];
                Encoding.ASCII.GetBytes("RIFF").CopyTo(silence, 0); BitConverter.GetBytes(36 + 13230).CopyTo(silence, 4); Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(silence, 8);
                BitConverter.GetBytes(16).CopyTo(silence, 16); BitConverter.GetBytes((short)1).CopyTo(silence, 20); BitConverter.GetBytes((short)1).CopyTo(silence, 22);
                BitConverter.GetBytes(22050).CopyTo(silence, 24); BitConverter.GetBytes(44100).CopyTo(silence, 28); BitConverter.GetBytes((short)2).CopyTo(silence, 32); BitConverter.GetBytes((short)16).CopyTo(silence, 34);
                Encoding.ASCII.GetBytes("data").CopyTo(silence, 36); BitConverter.GetBytes(13230).CopyTo(silence, 40);
                List<string> names = WaveOutDevices.Names();
                rep.AppendLine("devices: " + names.Count);
                for (int i = -1; i < names.Count; i++)
                {
                    bool ok = false;
                    try { ok = new WaveOutPlayback().Play(silence, i); } catch (Exception ex) { rep.AppendLine("error: " + ex.Message); }
                    rep.AppendLine((i < 0 ? "default" : i + " " + names[i]) + ": " + (ok ? "ok" : "FAILED"));
                }
                rep.AppendLine("resolve empty: " + WaveOutDevices.Resolve(""));
                if (names.Count > 0) rep.AppendLine("resolve " + names[0] + ": " + WaveOutDevices.Resolve(names[0]));
                rep.AppendLine("resolve unknown: " + WaveOutDevices.Resolve("no such device"));
                File.WriteAllText(devReport, rep.ToString(), new UTF8Encoding(false));
                return 0;
            }

            // --expand <report> --text "<tagged text>" [--repeat N] [--seed N]: what the program would speak for a mission text (no audio)
            string expandReport = Option(args, "--expand");
            if (expandReport != null)
            {
                StringBuilder rep = new StringBuilder();
                Ctx.Log = delegate (string s) { rep.AppendLine("log: " + s); };
                string seedArg = Option(args, "--seed");
                int seedValue;
                if (seedArg != null && int.TryParse(seedArg, out seedValue)) Say.Seed(seedValue);
                string repeatArg = Option(args, "--repeat");
                int repeat;
                if (repeatArg == null || !int.TryParse(repeatArg, out repeat) || repeat < 1) repeat = 1;
                VoiceLibrary.Refresh();
                VoicePacks.Refresh();
                Dictionary<string, double> watchSeen = new Dictionary<string, double>();
                Say.Sink = delegate (string s) { rep.AppendLine("bridge speaks: " + s); };
                List<string> texts = new List<string>();
                string linesFile = Option(args, "--lines");
                if (linesFile != null) { foreach (string ln in File.ReadAllLines(linesFile, Encoding.UTF8)) if (ln.Trim().Length > 0) texts.Add(ln); }
                else texts.Add(Option(args, "--text") ?? "");
                for (int r = 0; r < repeat * texts.Count; r++)
                {
                    string text = texts[r % texts.Count];
                    rep.AppendLine("input: " + text);
                    // test lines for the simulator-variable bridge: "@watch NAME VALUE" (a variable changes; the first value of a name is the baseline),
                    // "@settings variety=coherent names=random bridge=EN name.PLT=Mark"
                    if (text.StartsWith("@"))
                    {
                        string[] w = text.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (w[0] == "@watch" && w.Length >= 3)
                        {
                            double val; double prev;
                            string wname = string.Join(" ", w, 1, w.Length - 2);       // a name may have spaces (CAMERA STATE)
                            double.TryParse(w[w.Length - 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out val);
                            bool first = !watchSeen.TryGetValue(wname, out prev);
                            Say.OnWatch(wname, first ? 0 : prev, val, first);
                            watchSeen[wname] = val;
                        }
                        else if (w[0] == "@settings")
                        {
                            for (int k = 1; k < w.Length; k++)
                            {
                                string[] kv = w[k].Split('=');
                                if (kv.Length != 2) continue;
                                if (kv[0] == "variety") Ctx.Settings.Variety = kv[1];
                                else if (kv[0] == "names") Ctx.Settings.Names = kv[1];
                                else if (kv[0] == "bridge") Ctx.Settings.BridgePack = kv[1];
                                else if (kv[0].StartsWith("name.")) Ctx.Settings.CrewNames[kv[0].Substring(5).ToUpperInvariant()] = kv[1];
                            }
                            Ctx.Settings.Normalize();
                            Say.ApplySettings();
                        }
                        continue;
                    }
                    List<string> notes = new List<string>();
                    string wavFile;
                    string expanded = Say.Process(text, notes, out wavFile);
                    rep.AppendLine("expanded: " + expanded);
                    if (wavFile != null) rep.AppendLine("sound file: " + wavFile);
                    List<Segment> parsed = TagParser.Parse(expanded, notes);
                    Say.ApplyDelivery(parsed);
                    foreach (Segment s in parsed)
                    {
                        Plan p = Resolver.Resolve(s, Ctx.Settings, Ctx.Voices);
                        rep.AppendLine("segment: actor=" + p.Actor + " voice=" + (p.Voice != null ? p.Voice.Display : "(none)") + " speed=" + p.Speed + " vol=" + p.Volume + " level=" + (p.Volume * Mixer.MissionFactor(p.Actor) * Mixer.SpatialFactor(p.Radio, Say.SpeakerInside(p.Actor))).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " radio=" + p.Radio + (p.Radio && p.Fx != null ? " fx=" + p.Fx : "") + (p.Far ? " far=1" : "") + (p.Radio && p.Beep != null ? " beep=" + p.Beep : "") + " text=" + s.Text);
                    }
                    foreach (string n in notes) rep.AppendLine("note: " + n);
                }
                File.WriteAllText(expandReport, rep.ToString());
                PiperEngine.StopAll();
                return 0;
            }

            if (listFile != null)
            {
                VoiceLibrary.Refresh();
                List<string> lines = new List<string>();
                foreach (VoiceInfo v in Ctx.Voices) lines.Add(v.Family + " | " + v.Engine + " | " + v.Display + " | " + v.Id);
                File.WriteAllLines(listFile, lines.ToArray());
                return 0;
            }

            if (selftest != null)
            {
                string text = Option(args, "--text") ?? "Selftest of the voice hub.";
                StringBuilder report = new StringBuilder();
                try
                {
                    VoiceLibrary.Refresh();
                    List<string> notes = new List<string>();
                    List<Segment> segs = TagParser.Parse(text, notes);
                    foreach (string n in notes) report.AppendLine("note: " + n);
                    foreach (Segment s in segs)
                    {
                        Plan p = Resolver.Resolve(s, Ctx.Settings, Ctx.Voices);
                        report.AppendLine("segment: actor=" + s.Actor + " voice=" + s.Voice + " lang=" + s.Lang + " speed=" + s.Speed + " vol=" + s.Volume + " radio=" + s.Radio + " text=" + s.Text);
                        report.AppendLine("  plan: " + (p.Voice != null ? p.Voice.Display : "(none)") + " speed=" + p.Speed + " lang=" + p.Lang + " " + p.Note);
                    }
                    int code = 1;
                    if (segs.Count > 0)
                    {
                        Plan p = Resolver.Resolve(segs[0], Ctx.Settings, Ctx.Voices);
                        if (p.Voice != null)
                        {
                            DateTime t0 = DateTime.UtcNow;
                            byte[] raw = Synth.Render(p.Voice, p.Speed, segs[0].Text);
                            report.AppendLine("rendered " + raw.Length + " bytes in " + (int)(DateTime.UtcNow - t0).TotalMilliseconds + " ms: " + AudioFx.Describe(raw));
                            byte[] wav = AudioFx.Apply(raw, p.Volume, Ctx.Settings.MasterVolume, p.Radio, Ctx.Settings.Radio);
                            report.AppendLine("after effects (vol " + p.Volume + ", radio " + p.Radio + "): " + AudioFx.Describe(wav));
                            File.WriteAllBytes(selftest, wav);
                            code = wav.Length > 1000 ? 0 : 1;
                        }
                    }
                    File.WriteAllText(selftest + ".txt", report.ToString());
                    PiperEngine.StopAll();
                    return code;
                }
                catch (Exception ex)
                {
                    report.AppendLine("ERROR: " + ex);
                    File.WriteAllText(selftest + ".txt", report.ToString());
                    PiperEngine.StopAll();
                    return 1;
                }
            }

            bool createdNew = true;
            Mutex mutex = null;
            if (Option(args, "--screenshot") == null)         // a screenshot run may happen while the program is running
            {
                mutex = new Mutex(true, "Local\\HPGMissionCompanion", out createdNew);
                if (!createdNew) return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Ctx.Player = new Player();
            MainForm.QuietPort = Option(args, "--screenshot") != null;
            MainForm form = new MainForm(Has(args, "--minimized"), Has(args, "--exit-with-sim"), logFile, simRoot);
            string shot = Option(args, "--screenshot");
            if (shot != null) { form.Snapshot(shot); return 0; }
            Application.Run(form);
            GC.KeepAlive(mutex);
            return 0;
        }

        static bool Has(string[] args, string name)
        {
            foreach (string a in args) if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string Option(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
