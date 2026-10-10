// HPG Mission Companion - tools: the mission work folder (find, open, clean with a backup) and the mission index update.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace HPGMissionCompanion
{
    internal sealed class WorkFolderInfo
    {
        public string Path;
        public string Label;
        public int Files;
        public override string ToString() { return Label; }
    }

    internal sealed class CleanGroup
    {
        public string Key;
        public string Title;
        public string Note;
        public bool Default;
        public List<string> Files = new List<string>();
        public long Bytes;
        public override string ToString()
        {
            return Title + " - " + Files.Count + " file" + (Files.Count == 1 ? "" : "s") + ", " + (Bytes < 1024 ? Bytes + " B" : (Bytes / 1024) + " KB");
        }
    }

    internal static class Tools
    {
        // ---- where things are --------------------------------------------------------------------
        static List<string> WasmRoots()
        {
            string local = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
            string roaming = Environment.GetEnvironmentVariable("APPDATA") ?? "";
            return new List<string>(new string[]
            {
                Path.Combine(local, "Packages\\Microsoft.Limitless_8wekyb3d8bbwe\\LocalState\\WASM"),
                Path.Combine(local, "Packages\\Microsoft.FlightSimulator_8wekyb3d8bbwe\\LocalState\\WASM"),
                Path.Combine(roaming, "Microsoft Flight Simulator\\WASM"),
                Path.Combine(roaming, "Microsoft Flight Simulator 2024\\WASM")
            });
        }

        static List<string> CommunityRoots()
        {
            string local = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
            string roaming = Environment.GetEnvironmentVariable("APPDATA") ?? "";
            return new List<string>(new string[]
            {
                Path.Combine(local, "Packages\\Microsoft.Limitless_8wekyb3d8bbwe\\LocalCache\\Packages\\Community"),
                Path.Combine(local, "Packages\\Microsoft.FlightSimulator_8wekyb3d8bbwe\\LocalCache\\Packages\\Community"),
                Path.Combine(roaming, "Microsoft Flight Simulator\\Packages\\Community"),
                Path.Combine(roaming, "Microsoft Flight Simulator 2024\\Packages\\Community")
            });
        }

        public static int CountFiles(string dir)
        {
            try { return Directory.Exists(dir) ? Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length : 0; }
            catch (Exception) { return 0; }
        }

        // the work folders of the HPG packages (the mission writes its tables to hpg-airbus-h145-ap\work), the one with files first
        public static List<WorkFolderInfo> FindWorkFolders(string custom)
        {
            List<WorkFolderInfo> list = new List<WorkFolderInfo>();
            foreach (string root in WasmRoots())
            {
                if (!Directory.Exists(root)) continue;
                foreach (string sim in SafeDirs(root))
                {
                    foreach (string pkg in SafeDirs(sim))
                    {
                        string name = Path.GetFileName(pkg);
                        if (!name.StartsWith("hpg-", StringComparison.OrdinalIgnoreCase)) continue;
                        string work = Path.Combine(pkg, "work");
                        if (!Directory.Exists(work)) continue;
                        WorkFolderInfo w = new WorkFolderInfo();
                        w.Path = work;
                        w.Files = CountFiles(work);
                        w.Label = name + " (" + Path.GetFileName(sim) + ", " + w.Files + " files)";
                        list.Add(w);
                    }
                }
            }
            if (!string.IsNullOrEmpty(custom) && Directory.Exists(custom))
            {
                bool known = false;
                foreach (WorkFolderInfo w in list) if (string.Equals(w.Path, custom, StringComparison.OrdinalIgnoreCase)) known = true;
                if (!known)
                {
                    WorkFolderInfo c = new WorkFolderInfo();
                    c.Path = custom; c.Files = CountFiles(custom); c.Label = custom + " (" + c.Files + " files)";
                    list.Add(c);
                }
            }
            list.Sort(delegate (WorkFolderInfo a, WorkFolderInfo b)
            {
                int ra = Rank(a), rb = Rank(b);
                return ra != rb ? ra - rb : string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        static int Rank(WorkFolderInfo w)
        {
            int r = 0;
            if (w.Files == 0) r += 10;
            if (w.Path.IndexOf("h145-ap", StringComparison.OrdinalIgnoreCase) < 0) r += 5;
            return r;
        }

        static IEnumerable<string> SafeDirs(string dir)
        {
            try { return Directory.GetDirectories(dir); }
            catch (Exception) { return new string[0]; }
        }

        // every "Update Mission Index.cmd" of the HPG packages in the Community folders
        public static List<string> FindIndexTools(string custom)
        {
            List<string> list = new List<string>();
            foreach (string root in CommunityRoots())
            {
                if (!Directory.Exists(root)) continue;
                foreach (string pkg in SafeDirs(root))
                {
                    if (!Path.GetFileName(pkg).StartsWith("hpg-", StringComparison.OrdinalIgnoreCase)) continue;
                    string cmd = Path.Combine(Path.Combine(pkg, "Tools"), "Update Mission Index.cmd");
                    if (File.Exists(cmd)) list.Add(cmd);
                }
            }
            if (!string.IsNullOrEmpty(custom) && File.Exists(custom) && !list.Contains(custom)) list.Add(custom);
            return list;
        }

        // ---- cleaning the work folder ----------------------------------------------------------
        public static List<CleanGroup> Scan(string work)
        {
            Dictionary<string, CleanGroup> groups = new Dictionary<string, CleanGroup>();
            string[] order = new string[] { "safe", "config", "settings", "other" };
            Add(groups, "safe", "Safe to delete (debug)", "The debug snapshots of the mission (table_R&E_debug_*: automatic, manual, dispatch and previous-mission) and the tables written by the test missions (table_R&E_osm_*, table_R&E_*_test*). Nothing of yours is in them.", true);
            Add(groups, "config", "R&E configuration files (resets saved games and profiles)", "Your saved games, custom settings profiles, enabled accidents, aircraft profile links, custom missions and the other table_R&E_* files (custom hospitals, DF stations). They are lost for good: leave the backup ticked.", false);
            Add(groups, "settings", "User settings: global.json (use it to reset to default, or if the mission does not start)", "global.json holds the global variables of ALL the HPG missions, that is your settings and their state. It sometimes gets corrupted and then the mission does not start. Deleting it resets every setting to its default and the mission writes a new one at the next start. Close the simulator first.", false);
            Add(groups, "other", "Other files (not of this mission)", "table_* files of other missions, Flight_* records, PersistedAircraftState and files that are not recognised. Normally leave them.", false);
            if (Directory.Exists(work))
            {
                string root = work.TrimEnd('\\', '/');
                foreach (string file in Directory.GetFiles(work, "*", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileName(file);
                    string key = Classify(name);
                    // global.json counts only in the work folder itself: one in a subfolder belongs to something else
                    if (key == "settings" && !string.Equals(Path.GetDirectoryName(file).TrimEnd('\\', '/'), root, StringComparison.OrdinalIgnoreCase)) key = "other";
                    CleanGroup g = groups[key];
                    g.Files.Add(file);
                    try { g.Bytes += new FileInfo(file).Length; } catch (Exception) { }
                }
            }
            List<CleanGroup> result = new List<CleanGroup>();
            foreach (string k in order) if (groups[k].Files.Count > 0) result.Add(groups[k]);
            return result;
        }

        static void Add(Dictionary<string, CleanGroup> d, string key, string title, string note, bool def)
        {
            CleanGroup g = new CleanGroup();
            g.Key = key; g.Title = title; g.Note = note; g.Default = def;
            d[key] = g;
        }

        public static string Classify(string name)
        {
            string n = name.ToLowerInvariant();
            if (n == "global.json") return "settings";
            if (n.StartsWith("table_r&e_debug_")) return "safe";
            if (n.StartsWith("table_r&e_osm_") || (n.StartsWith("table_r&e_") && n.Contains("_test"))) return "safe";
            if (n.StartsWith("table_r&e_")) return "config";      // saved games, custom settings and profiles, enabled accidents, aircraft profile links, custom missions, hospitals, DF stations
            return "other";
        }

        // zips the files (relative names kept) and returns the zip path
        public static string Backup(string work, List<string> files, string backupDir)
        {
            Directory.CreateDirectory(backupDir);
            string zip = Path.Combine(backupDir, "work-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
            using (ZipArchive za = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                foreach (string f in files)
                {
                    string rel = f.StartsWith(work, StringComparison.OrdinalIgnoreCase) ? f.Substring(work.Length).TrimStart('\\', '/') : Path.GetFileName(f);
                    try { za.CreateEntryFromFile(f, rel.Replace('\\', '/')); } catch (Exception) { }
                }
            }
            return zip;
        }

        // returns the number of files deleted; failures (a file in use) are added to the list
        public static int Delete(List<string> files, List<string> failed)
        {
            int n = 0;
            foreach (string f in files)
            {
                try { File.Delete(f); n++; }
                catch (Exception ex) { failed.Add(Path.GetFileName(f) + ": " + ex.Message); }
            }
            return n;
        }

        // ---- mission index ----------------------------------------------------------------------
        // Runs the HPG tool "Update Mission Index.cmd" with its console input closed (the tool ends with a pause) and returns its exit code.
        public static int RunIndex(string cmd, Action<string> output)
        {
            cmd = cmd.Trim().Trim('"');
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"\"" + cmd + "\" < nul\"");
            psi.WorkingDirectory = Path.GetDirectoryName(cmd);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = new Process())
            {
                p.StartInfo = psi;
                p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e) { if (!string.IsNullOrEmpty(e.Data)) output(e.Data); };
                p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (!string.IsNullOrEmpty(e.Data)) output("[stderr] " + e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(180000))
                {
                    try { p.Kill(); } catch (Exception) { }
                    output("[ERROR] the index tool did not finish in 3 minutes");
                    return -1;
                }
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        // the generated index file of a tool: ..\html_ui\<folder>\GeneratedMissionIndex.json
        public static string IndexFileOf(string cmd)
        {
            try
            {
                string pkg = Path.GetDirectoryName(Path.GetDirectoryName(cmd));
                string ui = Path.Combine(pkg, "html_ui");
                if (!Directory.Exists(ui)) return null;
                foreach (string f in Directory.GetFiles(ui, "GeneratedMissionIndex.json", SearchOption.AllDirectories)) return f;
            }
            catch (Exception) { }
            return null;
        }

        public static bool SimulatorRunning()
        {
            return Process.GetProcessesByName("FlightSimulator").Length > 0 || Process.GetProcessesByName("FlightSimulator2024").Length > 0;
        }
    }
}
