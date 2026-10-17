// HPG Mission Companion - updates: the installed R&E mission (file and mission index), the mission, the program and the voice pack published as GitHub releases.
// Everything is read from GitHub (no Google Drive: the user's decision, the old link is dead); a check runs at every start and on the button "Check for updates".
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace HPGMissionCompanion
{
    // "HEMS RANDOM AND EVERYWHERE MISSIONS 0.997 169.45": family 0.997, release 169 . 45 (two integers: 169.9 comes before 169.45).
    // A title without the release ("... MISSIONS 0.997", the original mission) is release 0 . 0.
    internal struct MissionRelease : IComparable<MissionRelease>
    {
        public string Family;
        public int Major;
        public int Minor;
        public bool HasRelease;

        static readonly Regex Title = new Regex("MISSIONS\\s+(\\d+(?:\\.\\d+)?)(?:\\s+(\\d+)\\.(\\d+))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool TryParse(string title, out MissionRelease r)
        {
            r = new MissionRelease();
            if (string.IsNullOrEmpty(title)) return false;
            Match m = Title.Match(title);
            if (!m.Success) return false;
            r.Family = m.Groups[1].Value;
            if (m.Groups[2].Success)
            {
                r.HasRelease = true;
                int a, b;
                if (!int.TryParse(m.Groups[2].Value, out a) || !int.TryParse(m.Groups[3].Value, out b)) return false;
                r.Major = a; r.Minor = b;
            }
            return true;
        }

        static readonly Regex Loose = new Regex("(\\d+\\.\\d+)\\D+(\\d+)\\.(\\d+)", RegexOptions.CultureInvariant);

        public static bool TryParseLoose(string text, out MissionRelease r)
        {
            r = new MissionRelease();
            if (string.IsNullOrEmpty(text)) return false;
            Match m = Loose.Match(text);
            int a, b;
            if (!m.Success || !int.TryParse(m.Groups[2].Value, out a) || !int.TryParse(m.Groups[3].Value, out b)) return false;
            r.Family = m.Groups[1].Value; r.HasRelease = true; r.Major = a; r.Minor = b;
            return true;
        }

        static double FamilyValue(string f)
        {
            double v;
            return double.TryParse(f, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        public int CompareTo(MissionRelease o)
        {
            int c = FamilyValue(Family).CompareTo(FamilyValue(o.Family));
            if (c != 0) return c;
            c = Major.CompareTo(o.Major);
            return c != 0 ? c : Minor.CompareTo(o.Minor);
        }

        public override string ToString() { return Family + (HasRelease ? " " + Major + "." + Minor : " (no release number)"); }
    }

    internal sealed class MissionCopy
    {
        public string Path;
        public string Package;      // the Community package folder name
        public string Title;
        public MissionRelease Release;
        public long Size;
        public bool Missing;        // the official place has no mission file yet: the update creates it
        // the place of the R&E mission of a package: hpgmission\RANDOM AND EVERYWHERE\everywhere_all.json. Any other copy (a test copy beside it, the file called ORIGINAL) is listed but never chosen by itself.
        public bool Official
        {
            get
            {
                return string.Equals(System.IO.Path.GetFileName(Path), Updates.MissionAsset, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(Path)), Updates.OfficialFolder, StringComparison.OrdinalIgnoreCase);
            }
        }
        public override string ToString() { return Package + "\\" + Updates.OfficialFolderIfAny(Path) + System.IO.Path.GetFileName(Path) + "  -  " + (Missing ? "not installed (the update creates it)" : Release.ToString()); }
    }

    internal sealed class RemoteMission
    {
        public MissionRelease Release;
        public string Title;
        public long Size;           // total bytes of the file on the server
        public string Url;
        public string Sha256;       // from the GitHub asset digest when it gives one
        public string Source;       // "GitHub release <tag>"
        public string Notes;        // the body of the GitHub release (the user changelog of the build)
    }

    internal sealed class RemoteApp
    {
        public string Tag;
        public int Major;
        public int Minor;
        public string AssetName;
        public string AssetUrl;
        public long AssetSize;
        public string Sha256;       // from the GitHub asset digest when it gives one
        public string Page;
        public string Notes;        // the body of the GitHub release
    }

    // The voice pack EN published apart from the program (the release with the tag voicepack-EN holds the file EN.xml, replaced when the pack changes), so that a
    // change of texts reaches the players without a new program. Version and minapp are read from the downloaded file itself.
    internal sealed class RemotePack
    {
        public string Url;
        public long Size;
        public string Sha256;
        public string Notes;        // the body of that release: what changed in the pack
        public string File;         // the downloaded copy, once Fetch has run
        public VoicePack Info;      // that copy as the loader reads it
    }

    internal static class Updates
    {
        public const string PackReleaseApi = "https://api.github.com/repos/davierosoft/HPG-Mission-Companion/releases/tags/voicepack-EN";
        public const string PackAsset = "EN.xml";
        public const string ReleasesApi = "https://api.github.com/repos/davierosoft/HPG-Mission-Companion/releases/latest";
        public const string MissionReleasesApi = "https://api.github.com/repos/davierosoft/HEMS-random-everywhere-mission/releases/latest";
        public const string MissionAsset = "everywhere_all.json";
        public const string MissionId = "HEMSRandE";
        public const string OfficialFolder = "RANDOM AND EVERYWHERE";

        public static string OfficialFolderIfAny(string path)
        {
            string dir = System.IO.Path.GetDirectoryName(path);
            return dir != null && string.Equals(System.IO.Path.GetFileName(dir), OfficialFolder, StringComparison.OrdinalIgnoreCase) ? OfficialFolder + "\\" : "";
        }
        const int HeadBytes = 4096;

        // ---- the mission text ---------------------------------------------------------------------
        // the first bytes of a mission file: its title and its id
        public static bool ParseHead(string head, out string title, out string id)
        {
            title = null; id = null;
            if (head == null) return false;
            Match t = Regex.Match(head, "\"title\"\\s*:\\s*\"([^\"]*)\"");
            Match i = Regex.Match(head, "\"id\"\\s*:\\s*\"([^\"]*)\"");
            if (t.Success) title = t.Groups[1].Value;
            if (i.Success) id = i.Groups[1].Value;
            return title != null && id != null;
        }

        public static string ReadHead(string file)
        {
            using (FileStream f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] buf = new byte[HeadBytes];
                int n = f.Read(buf, 0, buf.Length);
                return Encoding.UTF8.GetString(buf, 0, n).TrimStart((char)0xFEFF);
            }
        }

        // ---- the installed mission ------------------------------------------------------------------
        // every R&E mission file (id HEMSRandE) in the hpgmission folders of the HPG packages of the Community folders
        public static List<MissionCopy> FindInstalled(IEnumerable<string> communityRoots, string custom)
        {
            List<MissionCopy> list = new List<MissionCopy>();
            foreach (string root in communityRoots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (string pkg in SafeDirs(root))
                {
                    if (!System.IO.Path.GetFileName(pkg).StartsWith("hpg-", StringComparison.OrdinalIgnoreCase)) continue;
                    string mdir = System.IO.Path.Combine(pkg, "hpgmission");
                    if (!Directory.Exists(mdir)) continue;
                    foreach (string f in SafeFiles(mdir)) TryAdd(list, f, System.IO.Path.GetFileName(pkg));
                    // the official folder exists but has no everywhere_all.json: the update puts the mission there
                    string official = System.IO.Path.Combine(mdir, OfficialFolder);
                    string target = System.IO.Path.Combine(official, MissionAsset);
                    if (Directory.Exists(official) && !File.Exists(target))
                    {
                        MissionCopy m = new MissionCopy();
                        m.Path = target; m.Package = System.IO.Path.GetFileName(pkg); m.Title = null; m.Missing = true;
                        m.Release = new MissionRelease();
                        list.Add(m);
                    }
                }
            }
            if (!string.IsNullOrEmpty(custom) && File.Exists(custom))
            {
                bool known = false;
                foreach (MissionCopy c in list) if (string.Equals(c.Path, custom, StringComparison.OrdinalIgnoreCase)) known = true;
                if (!known) TryAdd(list, custom, "(chosen)");
            }
            // the official places first (the installed file, then the folder still without it), then the other copies: tester builds with a release number, newest first, then the originals
            list.Sort(delegate (MissionCopy a, MissionCopy b)
            {
                if (a.Official != b.Official) return a.Official ? -1 : 1;
                if (a.Official && a.Missing != b.Missing) return a.Missing ? 1 : -1;
                if (a.Release.HasRelease != b.Release.HasRelease) return a.Release.HasRelease ? -1 : 1;
                int c = b.Release.CompareTo(a.Release);
                return c != 0 ? c : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        static void TryAdd(List<MissionCopy> list, string file, string package)
        {
            try
            {
                FileInfo fi = new FileInfo(file);
                if (fi.Length < 100000) return;                    // the mission is many megabytes; the small examples are not it
                string title, id;
                if (!ParseHead(ReadHead(file), out title, out id) || id != MissionId) return;
                MissionRelease r;
                if (!MissionRelease.TryParse(title, out r)) return;
                MissionCopy c = new MissionCopy();
                c.Path = file; c.Package = package; c.Title = title; c.Release = r; c.Size = fi.Length;
                list.Add(c);
            }
            catch (Exception) { }
        }

        static IEnumerable<string> SafeDirs(string dir)
        {
            try { return Directory.GetDirectories(dir); } catch (Exception) { return new string[0]; }
        }

        static IEnumerable<string> SafeFiles(string dir)
        {
            try { return Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories); } catch (Exception) { return new string[0]; }
        }

        // ---- the mission index ---------------------------------------------------------------------
        // GeneratedMissionIndex.json keeps a copy of every mission file as base64 (the whole file). The releases of the R&E copies in it:
        // every string that starts with base64 is decoded for its first bytes only.
        public static List<MissionRelease> ReadIndexReleases(string indexFile)
        {
            List<MissionRelease> list = new List<MissionRelease>();
            string text = File.ReadAllText(indexFile, Encoding.UTF8);
            int pos = 0;
            const int probe = 1200;                                 // base64 characters decoded per entry: 900 bytes
            while (pos < text.Length)
            {
                int q = text.IndexOf('"', pos);
                if (q < 0 || q + 1 + probe >= text.Length) break;
                int i = q + 1;
                int stop = i + probe;
                bool b64 = true;
                for (int k = i; k < stop; k++)
                {
                    char ch = text[k];
                    if (!((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '+' || ch == '/' || ch == '=')) { b64 = false; break; }
                }
                if (b64)
                {
                    try
                    {
                        string head = Encoding.UTF8.GetString(Convert.FromBase64String(text.Substring(i, probe))).TrimStart((char)0xFEFF);
                        string title, id;
                        MissionRelease r;
                        if (ParseHead(head, out title, out id) && id == MissionId && MissionRelease.TryParse(title, out r)) list.Add(r);
                    }
                    catch (Exception) { }
                    int end = text.IndexOf('"', stop);              // skip the rest of the entry
                    pos = end < 0 ? text.Length : end + 1;
                }
                else pos = q + 1;
            }
            return list;
        }

        // ---- where the published mission is ---------------------------------------------------------
        // the GitHub release of the mission repository (tag with the release, the asset everywhere_all.json with its size and sha256)
        public static RemoteMission ReadRemoteMissionGitHub(string apiUrl)
        {
            Dictionary<string, object> d = GetJson(apiUrl);
            string tag = Str(d, "tag_name");
            string name = Str(d, "name");
            MissionRelease r;
            if (!MissionRelease.TryParseLoose(tag, out r) && !MissionRelease.TryParseLoose(name, out r))
                throw new InvalidOperationException("the latest release has no mission release in its tag (" + tag + ")");
            object assets;
            if (d.TryGetValue("assets", out assets) && assets is System.Collections.IEnumerable)
            {
                foreach (object o in (System.Collections.IEnumerable)assets)
                {
                    Dictionary<string, object> e = o as Dictionary<string, object>;
                    if (e == null || !string.Equals(Str(e, "name"), MissionAsset, StringComparison.OrdinalIgnoreCase)) continue;
                    RemoteMission m = new RemoteMission();
                    m.Release = r; m.Title = tag; m.Url = Str(e, "browser_download_url"); m.Source = "GitHub release " + tag; m.Notes = Str(d, "body");
                    object size;
                    if (e.TryGetValue("size", out size)) m.Size = Convert.ToInt64(size);
                    string digest = Str(e, "digest");
                    if (digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) m.Sha256 = digest.Substring(7).ToLowerInvariant();
                    return m;
                }
            }
            throw new InvalidOperationException("the latest release (" + tag + ") has no " + MissionAsset + " asset");
        }

        // the release notes are Markdown written for the GitHub page: headings in capitals, real bullets, no emphasis marks
        public static string PlainNotes(string md)
        {
            if (string.IsNullOrEmpty(md)) return "";
            StringBuilder sb = new StringBuilder();
            foreach (string raw in md.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.TrimEnd();
                Match h = Regex.Match(line, "^#{1,6}\\s+(.*)$");
                if (h.Success)
                {
                    if (sb.Length > 0) sb.AppendLine();
                    sb.AppendLine(h.Groups[1].Value.Trim().Replace("**", "").ToUpperInvariant());
                    continue;
                }
                if (Regex.IsMatch(line, "^\\s*[-*]\\s+")) line = "\u2022 " + Regex.Replace(line, "^\\s*[-*]\\s+", "");
                sb.AppendLine(line.Replace("**", "").Replace("`", ""));
            }
            return Regex.Replace(sb.ToString().Trim(), "(\r?\n){3,}", "\r\n\r\n");
        }

        // the notes of the latest release of a repository, also when nothing is newer (the What's new button)
        public static string ReadReleaseNotes(string apiUrl, out string label)
        {
            Dictionary<string, object> d = GetJson(apiUrl);
            label = Str(d, "tag_name") ?? Str(d, "name");
            return PlainNotes(Str(d, "body"));
        }

        static Dictionary<string, object> GetJson(string url)
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            return js.Deserialize<Dictionary<string, object>>(Net.GetString(url));
        }

        // downloads the mission to dest (through dest.part, never a .json beside the missions), checks the size and the shape of the file
        public static void DownloadMission(RemoteMission m, string dest, Action<long, long> progress)
        {
            Net.Download(m.Url, dest, progress, null, m.Size);
            FileInfo fi = new FileInfo(dest);
            if (m.Size > 0 && fi.Length != m.Size) { File.Delete(dest); throw new IOException("the download is incomplete (" + fi.Length + " of " + m.Size + " bytes), try again"); }
            string title, id;
            if (!ParseHead(ReadHead(dest), out title, out id) || id != MissionId) { File.Delete(dest); throw new IOException("the downloaded file is not the R&E mission"); }
            if (!TailIsClosed(dest)) { File.Delete(dest); throw new IOException("the downloaded file is cut (it does not end with a closing brace), try again"); }
            if (!string.IsNullOrEmpty(m.Sha256) && Sha256Of(dest) != m.Sha256) { File.Delete(dest); throw new IOException("the mission file is corrupted (sha256 differs), try again"); }
            MissionRelease got;
            if (!MissionRelease.TryParse(title, out got) || got.CompareTo(m.Release) != 0) { File.Delete(dest); throw new IOException("the title of the downloaded file (" + title + ") is not the release announced (" + m.Release + ")"); }
        }

        static string Sha256Of(string file)
        {
            using (SHA256 h = SHA256.Create())
            using (FileStream f = File.OpenRead(file))
                return BitConverter.ToString(h.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
        }

        static bool TailIsClosed(string file)
        {
            using (FileStream f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                int n = (int)Math.Min(64, f.Length);
                byte[] buf = new byte[n];
                f.Seek(-n, SeekOrigin.End);
                f.Read(buf, 0, n);
                string tail = Encoding.UTF8.GetString(buf).TrimEnd();
                return tail.EndsWith("}");
            }
        }

        // Replaces the installed file. The old one is copied first: beside it with the extension .json.bak when backupDir is empty (the index tool lists only *.json,
        // so a .bak beside the mission is not taken for a mission; a .json copy would be), or into backupDir as .json. A file that does not exist yet is simply created.
        // The new one is written beside it as .part and moved over it. Returns the backup path, null when there was nothing to keep.
        public static string ReplaceInstalled(string installed, string newFile, string backupDir)
        {
            string backup = null;
            if (File.Exists(installed))
            {
                bool beside = string.IsNullOrEmpty(backupDir);
                if (!beside) Directory.CreateDirectory(backupDir);
                MissionRelease r;
                string title, id;
                string tag = "unknown";
                try { if (ParseHead(ReadHead(installed), out title, out id) && MissionRelease.TryParse(title, out r)) tag = r.HasRelease ? r.Major + "." + r.Minor : r.Family; } catch (Exception) { }
                string name = "everywhere_all-" + tag + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json";
                backup = beside ? Path.Combine(Path.GetDirectoryName(installed), name + ".bak") : Path.Combine(backupDir, name);
                // not File.Copy: the mission file of the Community folder is encrypted (EFS, the Store keeps it that way) and CopyFile refuses to write an encrypted
                // file where encryption is not possible (a backups folder on OneDrive): "the specified file could not be encrypted". A stream copy writes the bytes only.
                CopyContents(installed, backup, false);
            }
            string part = installed + ".part";
            CopyContents(newFile, part, true);
            if (File.Exists(installed)) File.Delete(installed);
            File.Move(part, installed);
            return backup;
        }

        // copies the bytes of a file (the destination takes the attributes of its own folder, never those of the source)
        static void CopyContents(string src, string dst, bool overwrite)
        {
            using (FileStream i = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (FileStream o = new FileStream(dst, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None))
                i.CopyTo(o);
        }

        // ---- GitHub: the program ---------------------------------------------------------------------
        public static RemoteApp ReadRemoteApp(string apiUrl)
        {
            Dictionary<string, object> d = GetJson(apiUrl);
            RemoteApp a = new RemoteApp();
            a.Tag = Str(d, "tag_name");
            a.Page = Str(d, "html_url");
            a.Notes = Str(d, "body");
            if (!ParseVersion(a.Tag, out a.Major, out a.Minor)) throw new InvalidOperationException("the latest release has no version tag (" + a.Tag + ")");
            object assets;
            if (d.TryGetValue("assets", out assets) && assets is System.Collections.IEnumerable)
            {
                foreach (object o in (System.Collections.IEnumerable)assets)
                {
                    Dictionary<string, object> e = o as Dictionary<string, object>;
                    if (e == null) continue;
                    string name = Str(e, "name");
                    if (!string.Equals(name, "HPGMissionCompanionSetup.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    a.AssetName = name;
                    a.AssetUrl = Str(e, "browser_download_url");
                    object size;
                    if (e.TryGetValue("size", out size)) a.AssetSize = Convert.ToInt64(size);
                    string digest = Str(e, "digest");
                    if (digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) a.Sha256 = digest.Substring(7).ToLowerInvariant();
                }
            }
            return a;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v != null ? v.ToString() : null;
        }

        // ---- GitHub: the voice pack EN ----------------------------------------------------------------
        // null when no pack release exists (404); the file is downloaded to tmpDir and read, so the caller can compare the version
        public static RemotePack ReadRemotePack(string apiUrl, string tmpDir)
        {
            Dictionary<string, object> d;
            try { d = GetJson(apiUrl); }
            catch (WebException ex)
            {
                HttpWebResponse r = ex.Response as HttpWebResponse;
                if (r != null && r.StatusCode == HttpStatusCode.NotFound) return null;
                throw;
            }
            RemotePack p = new RemotePack();
            p.Notes = Str(d, "body");
            object assets;
            if (d.TryGetValue("assets", out assets) && assets is System.Collections.IEnumerable)
            {
                foreach (object o in (System.Collections.IEnumerable)assets)
                {
                    Dictionary<string, object> e = o as Dictionary<string, object>;
                    if (e == null || !string.Equals(Str(e, "name"), PackAsset, StringComparison.OrdinalIgnoreCase)) continue;
                    p.Url = Str(e, "browser_download_url");
                    object size;
                    if (e.TryGetValue("size", out size)) p.Size = Convert.ToInt64(size);
                    string digest = Str(e, "digest");
                    if (digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) p.Sha256 = digest.Substring(7).ToLowerInvariant();
                }
            }
            if (p.Url == null) return null;
            Directory.CreateDirectory(tmpDir);
            p.File = Path.Combine(tmpDir, "EN-published.xml");
            Net.Download(p.Url, p.File, null, null, p.Size);
            if (!string.IsNullOrEmpty(p.Sha256) && Sha256Of(p.File) != p.Sha256) { File.Delete(p.File); throw new IOException("the published voice pack is corrupted (sha256 differs)"); }
            p.Info = VoicePacks.Load(p.File);
            return p;
        }

        // true when the published pack is usable, newer than the installed one with the same name, and not for a newer program than this one
        public static bool PackIsNewer(RemotePack p, out string why)
        {
            why = "";
            if (p == null || p.Info == null || !p.Info.Usable) { why = p != null && p.Info != null && p.Info.Problems.Count > 0 ? p.Info.Problems[0] : "not usable"; return false; }
            int needMajor, needMinor, haveMajor, haveMinor;
            if (p.Info.MinApp.Length > 0 && ParseVersion(p.Info.MinApp, out needMajor, out needMinor) && ParseVersion(AppInfo.Version, out haveMajor, out haveMinor)
                && (needMajor > haveMajor || (needMajor == haveMajor && needMinor > haveMinor)))
            { why = "it needs the program " + p.Info.MinApp + " or newer"; return false; }
            VoicePack installed = VoicePacks.Find(p.Info.Name);
            return installed == null || p.Info.Version > installed.Version;
        }

        // "v0.10", "0.9" -> 0, 10 / 0, 9 (two integers: 0.10 comes after 0.9)
        public static bool ParseVersion(string text, out int major, out int minor)
        {
            major = 0; minor = 0;
            if (string.IsNullOrEmpty(text)) return false;
            Match m = Regex.Match(text, "(\\d+)\\.(\\d+)");
            return m.Success && int.TryParse(m.Groups[1].Value, out major) && int.TryParse(m.Groups[2].Value, out minor);
        }

        public static bool AppIsNewer(RemoteApp a)
        {
            int cm, cn;
            if (!ParseVersion(AppInfo.Version, out cm, out cn)) return false;
            return a.Major > cm || (a.Major == cm && a.Minor > cn);
        }

        public static void DownloadSetup(RemoteApp a, string dest, Action<long, long> progress)
        {
            Net.Download(a.AssetUrl, dest, progress, null, a.AssetSize);
            FileInfo fi = new FileInfo(dest);
            if (a.AssetSize > 0 && fi.Length != a.AssetSize) { File.Delete(dest); throw new IOException("the download is incomplete (" + fi.Length + " of " + a.AssetSize + " bytes), try again"); }
            if (!string.IsNullOrEmpty(a.Sha256) && Sha256Of(dest) != a.Sha256) { File.Delete(dest); throw new IOException("the setup program is corrupted (sha256 differs), try again"); }
        }

        // The setup closes this program (it is running from the folder it installs into), copies the new files and starts it again. Shortcuts are
        // asked for only when this folder was installed by the setup (its uninstaller is there).
        public static ProcessStartInfo SetupStart(string setupFile, string installDir)
        {
            bool installed = File.Exists(Path.Combine(installDir, "Uninstall.exe"));
            // /waitpid: the setup waits for this program to end; /showerrors: a failure is shown in a window (the program is gone by then)
            string args = "/S /showerrors /waitpid=" + System.Diagnostics.Process.GetCurrentProcess().Id + " /dir=\"" + installDir.TrimEnd('\\') + "\"" + (installed ? "" : " /nodesktop /nostartmenu");
            ProcessStartInfo psi = new ProcessStartInfo(setupFile, args);
            psi.UseShellExecute = false;
            psi.WorkingDirectory = Path.GetDirectoryName(setupFile);
            return psi;
        }
    }
}
