// HPG Mission Companion installer and uninstaller (one executable, .NET Framework 4, no SDK).
// Install: per user, no administrator rights. It writes only the program files, the voice packs through the program's own
// --install-pack (an older version of a pack is archived), the shortcuts and the uninstall entry of the current user.
// It never touches the settings, the Piper engine, the voices, the audio folder or the backups of an earlier install.
// Uninstall: removes what the install wrote; the user data is removed only when the user answers yes (default no).
// Switches: /S silent, /dir=PATH install folder, /nodesktop, /nostartmenu, /nolaunch, /uninstall, /removedata (silent uninstall).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("HPG Mission Companion Setup")]
[assembly: AssemblyProduct("HPG Mission Companion")]
[assembly: AssemblyVersion("0.10.0.0")]
[assembly: AssemblyFileVersion("0.10.0.0")]

namespace HpgSetup
{
    static class Info
    {
        public const string Version = "0.10";
        public const string Name = "HPG Mission Companion";
        public const string Author = "Andrea Daviero";
        public const string ExeName = "HPGMissionCompanion.exe";
        public const string UninstallerName = "Uninstall.exe";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\HPGMissionCompanion";
        public const string Home = "https://github.com/davierosoft/HPG-Mission-Companion";
        public const string Discord = "https://discord.gg/eUqz49jXzT";

        public static string DefaultDir()
        {
            string old = null;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                    if (k != null) old = k.GetValue("InstallLocation") as string;
            }
            catch (Exception) { }
            if (!string.IsNullOrEmpty(old)) return old;
            return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"), "HPG Mission Companion");
        }

        public static string StartMenuDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "HPG Mission Companion");
        }

        public static string DesktopLink()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "HPG Mission Companion.lnk");
        }

        public static void Log(string line)
        {
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "HPGMissionCompanionSetup.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + Environment.NewLine); }
            catch (Exception) { }
        }
    }

    class Options
    {
        public bool Silent;
        public bool Uninstall;
        public string Dir;
        public bool Desktop = true;
        public bool StartMenu = true;
        public bool Launch = true;
        public bool RemoveData;
        public string Screenshot;

        public static Options Parse(string[] args)
        {
            Options o = new Options();
            foreach (string raw in args)
            {
                string a = raw.Trim();
                string l = a.ToLowerInvariant();
                if (l == "/s" || l == "/silent" || l == "-s" || l == "--silent") o.Silent = true;
                else if (l == "/uninstall" || l == "--uninstall") o.Uninstall = true;
                else if (l == "/nodesktop") o.Desktop = false;
                else if (l == "/nostartmenu") o.StartMenu = false;
                else if (l == "/nolaunch") o.Launch = false;
                else if (l == "/removedata") o.RemoveData = true;
                else if (l.StartsWith("/dir=")) o.Dir = a.Substring(5).Trim('"');
                else if (l.StartsWith("/screenshot=")) o.Screenshot = a.Substring(12).Trim('"');
            }
            return o;
        }
    }

    static class Ops
    {
        public static Stream Resource(string name)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        }

        public static bool HasResource(string name)
        {
            foreach (string n in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                if (string.Equals(n, name, StringComparison.Ordinal)) return true;
            return false;
        }

        static void Extract(string name, string path)
        {
            using (Stream s = Resource(name))
            {
                if (s == null) throw new InvalidOperationException("missing resource " + name);
                using (FileStream f = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    byte[] buf = new byte[65536];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) f.Write(buf, 0, n);
                }
            }
        }

        public static string ValidateDir(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return "Choose an install folder.";
            try
            {
                string full = Path.GetFullPath(dir);
                if (!Path.IsPathRooted(full)) return "The folder must be an absolute path.";
                if (string.Equals(full.TrimEnd('\\'), Path.GetPathRoot(full).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return "Do not install in the root of a drive.";
            }
            catch (Exception) { return "The folder name is not valid."; }
            return null;
        }

        static List<Process> RunningFrom(string dir)
        {
            List<Process> list = new List<Process>();
            string exe = Path.GetFullPath(Path.Combine(dir, Info.ExeName));
            foreach (Process p in Process.GetProcessesByName("HPGMissionCompanion"))
            {
                try { if (string.Equals(p.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase)) list.Add(p); }
                catch (Exception) { }
            }
            return list;
        }

        public static bool IsRunning(string dir) { return RunningFrom(dir).Count > 0; }

        // asks the program to close; if it only hides in the notification area, ends it with its child processes (Piper)
        public static void CloseRunning(string dir, Action<string> log)
        {
            foreach (Process p in RunningFrom(dir))
            {
                log("Closing the running program");
                try
                {
                    p.CloseMainWindow();
                    if (!p.WaitForExit(4000))
                    {
                        ProcessStartInfo psi = new ProcessStartInfo("taskkill.exe", "/PID " + p.Id + " /T /F");
                        psi.UseShellExecute = false;
                        psi.CreateNoWindow = true;
                        using (Process k = Process.Start(psi)) { if (k != null) k.WaitForExit(8000); }
                        p.WaitForExit(4000);
                    }
                }
                catch (Exception ex) { Info.Log("close failed: " + ex.Message); }
            }
        }

        static void MakeShortcut(string lnk, string target, string arguments, string workDir, string description)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) throw new InvalidOperationException("WScript.Shell is not available");
            object sh = Activator.CreateInstance(t);
            object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
            Type st = sc.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
            st.InvokeMember("Arguments", BindingFlags.SetProperty, null, sc, new object[] { arguments });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
            st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { description });
            st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }

        static bool FilesEqual(string a, string b)
        {
            byte[] x = File.ReadAllBytes(a);
            byte[] y = File.ReadAllBytes(b);
            if (x.Length != y.Length) return false;
            for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
            return true;
        }

        static long DirSize(string dir)
        {
            long n = 0;
            try { foreach (string f in Directory.GetFiles(dir)) n += new FileInfo(f).Length; }
            catch (Exception) { }
            return n;
        }

        public static void Install(Options o, Action<string> log)
        {
            string dir = Path.GetFullPath(o.Dir);
            string err = ValidateDir(dir);
            if (err != null) throw new InvalidOperationException(err);
            Info.Log("install to " + dir);

            CloseRunning(dir, log);
            Directory.CreateDirectory(dir);

            log("Copying the program");
            Extract("app.exe", Path.Combine(dir, Info.ExeName));
            if (HasResource("SimConnect.dll"))
            {
                log("Copying SimConnect.dll");
                Extract("SimConnect.dll", Path.Combine(dir, "SimConnect.dll"));
            }
            else log("SimConnect.dll is not part of this installer: copy it from the MSFS SDK next to the program to use L:AUDIOINHIBIT");

            // the voice packs go through the program itself (an older version of the same pack is archived in voicepacks\obsolete)
            string tmp = Path.Combine(Path.GetTempPath(), "hmc-setup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                foreach (string res in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                {
                    if (!res.StartsWith("pack.", StringComparison.Ordinal)) continue;
                    string file = Path.Combine(tmp, res.Substring(5));
                    Extract(res, file);
                    string installed = Path.Combine(Path.Combine(dir, "voicepacks"), res.Substring(5));
                    if (File.Exists(installed) && FilesEqual(installed, file))
                    {
                        log("Voice pack " + res.Substring(5) + ": already up to date");
                        continue;
                    }
                    string report = file + ".report.txt";
                    ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(dir, Info.ExeName), "--install-pack \"" + file + "\" --report \"" + report + "\"");
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.WorkingDirectory = dir;
                    using (Process p = Process.Start(psi))
                    {
                        if (p != null && !p.WaitForExit(30000)) { try { p.Kill(); } catch (Exception) { } }
                    }
                    string msg = File.Exists(report) ? File.ReadAllText(report).Trim() : "no report";
                    log("Voice pack " + res.Substring(5) + ": " + msg);
                }
            }
            finally { try { Directory.Delete(tmp, true); } catch (Exception) { } }

            string uninstaller = Path.Combine(dir, Info.UninstallerName);
            string self = Assembly.GetExecutingAssembly().Location;
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(uninstaller), StringComparison.OrdinalIgnoreCase))
                File.Copy(self, uninstaller, true);

            string exe = Path.Combine(dir, Info.ExeName);
            if (o.StartMenu)
            {
                try
                {
                    Directory.CreateDirectory(Info.StartMenuDir());
                    MakeShortcut(Path.Combine(Info.StartMenuDir(), "HPG Mission Companion.lnk"), exe, "", dir, Info.Name);
                    MakeShortcut(Path.Combine(Info.StartMenuDir(), "Uninstall HPG Mission Companion.lnk"), uninstaller, "/uninstall", dir, "Uninstall " + Info.Name);
                    log("Start menu shortcuts created");
                }
                catch (Exception ex) { log("Start menu shortcut failed: " + ex.Message); }
            }
            if (o.Desktop)
            {
                try { MakeShortcut(Info.DesktopLink(), exe, "", dir, Info.Name); log("Desktop shortcut created"); }
                catch (Exception ex) { log("Desktop shortcut failed: " + ex.Message); }
            }

            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Info.UninstallKey))
            {
                k.SetValue("DisplayName", Info.Name);
                k.SetValue("DisplayVersion", Info.Version);
                k.SetValue("Publisher", Info.Author);
                k.SetValue("InstallLocation", dir);
                k.SetValue("DisplayIcon", exe + ",0");
                k.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall");
                k.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /uninstall /S");
                k.SetValue("URLInfoAbout", Info.Home);
                k.SetValue("HelpLink", Info.Discord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(DirSize(dir) / 1024), RegistryValueKind.DWord);
            }
            log("Installed version " + Info.Version + " in " + dir);
        }

        static void TryDeleteFile(string path, Action<string> log)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { log("Could not delete " + path + ": " + ex.Message); }
        }

        public static void Uninstall(string dir, bool removeData, Action<string> log)
        {
            dir = Path.GetFullPath(dir);
            Info.Log("uninstall from " + dir + " removeData=" + removeData);
            string exe = Path.Combine(dir, Info.ExeName);
            CloseRunning(dir, log);

            // the program removes itself from the EXE.xml of the simulators (its own code edits those files)
            if (File.Exists(exe))
            {
                try
                {
                    string report = Path.Combine(Path.GetTempPath(), "hmc-autostart-off.txt");
                    ProcessStartInfo psi = new ProcessStartInfo(exe, "--autostart off --report \"" + report + "\"");
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.WorkingDirectory = dir;
                    using (Process p = Process.Start(psi)) { if (p != null) p.WaitForExit(30000); }
                    if (File.Exists(report))
                    {
                        string txt = File.ReadAllText(report).Trim();
                        if (txt.Length > 0) log("Simulator start entry: " + txt.Replace(Environment.NewLine, "; "));
                        File.Delete(report);
                    }
                }
                catch (Exception ex) { log("Simulator start entry not changed: " + ex.Message); }
            }

            TryDeleteFile(exe, log);
            TryDeleteFile(Path.Combine(dir, "SimConnect.dll"), log);
            TryDeleteFile(Path.Combine(dir, "autostart-report.txt"), log);
            TryDeleteFile(Path.Combine(dir, "install-pack-report.txt"), log);

            try { if (Directory.Exists(Info.StartMenuDir())) Directory.Delete(Info.StartMenuDir(), true); }
            catch (Exception ex) { log("Start menu folder: " + ex.Message); }
            TryDeleteFile(Info.DesktopLink(), log);
            try { Registry.CurrentUser.DeleteSubKeyTree(Info.UninstallKey, false); }
            catch (Exception ex) { log("Registry: " + ex.Message); }

            if (removeData)
            {
                log("Removing the user data");
                TryDeleteFile(Path.Combine(dir, "HPGMissionCompanion.json"), log);
                foreach (string name in new string[] { "piper", "voices", "audio", "backups", "voicepacks" })
                {
                    try { string d = Path.Combine(dir, name); if (Directory.Exists(d)) Directory.Delete(d, true); }
                    catch (Exception ex) { log("Could not delete " + name + ": " + ex.Message); }
                }
                try { foreach (string f in Directory.GetFiles(dir, "*.log")) TryDeleteFile(f, log); } catch (Exception) { }
            }

            // this executable may be the uninstaller in the folder: it is deleted after it ends
            string self = Assembly.GetExecutingAssembly().Location;
            bool selfInDir = string.Equals(Path.GetDirectoryName(Path.GetFullPath(self)), dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            string cmd = "/c ping 127.0.0.1 -n 3 >nul & ";
            if (selfInDir) cmd += "del /f /q \"" + self + "\" & ";
            cmd += "rmdir \"" + dir + "\"";
            ProcessStartInfo del = new ProcessStartInfo("cmd.exe", cmd);
            del.UseShellExecute = false;
            del.CreateNoWindow = true;
            del.WorkingDirectory = Path.GetTempPath();
            Process.Start(del);
            log("Uninstalled");
        }
    }

    class SetupForm : Form
    {
        TextBox dirBox = new TextBox();
        CheckBox desktop = new CheckBox();
        CheckBox startMenu = new CheckBox();
        CheckBox launch = new CheckBox();
        TextBox logBox = new TextBox();
        Button install = new Button();
        Button close = new Button();
        Options opt;
        bool done;

        public SetupForm(Options o)
        {
            opt = o;
            Text = Info.Name + " " + Info.Version + " Setup";
            ClientSize = new Size(560, 400);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch (Exception) { }

            Label title = new Label();
            title.Text = Info.Name + " " + Info.Version;
            title.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
            title.SetBounds(16, 12, 528, 32);
            Controls.Add(title);

            Label sub = new Label();
            sub.Text = "Voice service and tools for the HPG H145 missions. Author: " + Info.Author + ". Installs for the current user; no administrator rights needed. Settings, Piper and the voices of an earlier install are kept.";
            sub.SetBounds(16, 48, 528, 48);
            Controls.Add(sub);

            Label lf = new Label();
            lf.Text = "Install folder";
            lf.SetBounds(16, 104, 200, 18);
            Controls.Add(lf);
            dirBox.Text = o.Dir ?? Info.DefaultDir();
            dirBox.SetBounds(16, 124, 440, 24);
            Controls.Add(dirBox);
            Button browse = new Button();
            browse.Text = "Browse...";
            browse.SetBounds(464, 122, 80, 27);
            browse.Click += delegate
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog())
                {
                    d.Description = "Choose the install folder";
                    if (d.ShowDialog(this) == DialogResult.OK) dirBox.Text = d.SelectedPath;
                }
            };
            Controls.Add(browse);

            startMenu.Text = "Start menu shortcut";
            startMenu.Checked = o.StartMenu;
            startMenu.SetBounds(16, 160, 260, 22);
            Controls.Add(startMenu);
            desktop.Text = "Desktop shortcut";
            desktop.Checked = o.Desktop;
            desktop.SetBounds(16, 184, 260, 22);
            Controls.Add(desktop);
            launch.Text = "Start the program when the install ends";
            launch.Checked = o.Launch;
            launch.SetBounds(16, 208, 360, 22);
            Controls.Add(launch);

            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.SetBounds(16, 240, 528, 108);
            Controls.Add(logBox);

            install.Text = "Install";
            install.SetBounds(372, 360, 84, 28);
            install.Click += OnInstall;
            Controls.Add(install);
            close.Text = "Cancel";
            close.SetBounds(464, 360, 80, 28);
            close.Click += delegate { Close(); };
            Controls.Add(close);
            AcceptButton = install;
        }

        void AddLog(string line)
        {
            logBox.AppendText(line + Environment.NewLine);
            Info.Log(line);
            Application.DoEvents();
        }

        void OnInstall(object sender, EventArgs e)
        {
            if (done) { Close(); return; }
            opt.Dir = dirBox.Text.Trim().Trim('"');
            opt.StartMenu = startMenu.Checked;
            opt.Desktop = desktop.Checked;
            opt.Launch = launch.Checked;
            string err = Ops.ValidateDir(opt.Dir);
            if (err != null) { MessageBox.Show(this, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            string exe = Path.Combine(opt.Dir, Info.ExeName);
            if (Directory.Exists(opt.Dir) && !File.Exists(exe) && Directory.GetFileSystemEntries(opt.Dir).Length > 0)
            {
                if (MessageBox.Show(this, "The folder is not empty and does not hold the program. Install here anyway? Files of the program are overwritten; nothing else is removed.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            }
            if (Ops.IsRunning(opt.Dir))
            {
                if (MessageBox.Show(this, "The program is running from this folder. It will be closed to update it.", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            }
            install.Enabled = false;
            close.Enabled = false;
            dirBox.Enabled = false;
            try
            {
                Ops.Install(opt, AddLog);
                done = true;
                AddLog("Done.");
                install.Text = "Finish";
                install.Enabled = true;
                close.Visible = false;
                if (opt.Launch)
                {
                    try { Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = opt.Dir, UseShellExecute = false }); }
                    catch (Exception ex) { AddLog("Could not start the program: " + ex.Message); }
                }
            }
            catch (Exception ex)
            {
                AddLog("ERROR: " + ex.Message);
                MessageBox.Show(this, "The install failed: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                install.Enabled = true;
                close.Enabled = true;
                dirBox.Enabled = true;
            }
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Options o = Options.Parse(args);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if (o.Uninstall)
                {
                    string dir = o.Dir ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    if (!o.Silent)
                    {
                        if (MessageBox.Show("Remove " + Info.Name + " from " + dir + "?", Info.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;
                        bool dataThere = File.Exists(Path.Combine(dir, "HPGMissionCompanion.json")) || Directory.Exists(Path.Combine(dir, "voices")) || Directory.Exists(Path.Combine(dir, "piper"));
                        if (dataThere)
                            o.RemoveData = MessageBox.Show("Also delete the settings, the Piper engine, the voices, the audio folder, the backups and the voice packs in this folder?\r\n\r\nChoose No to keep them.", Info.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
                    }
                    List<string> lines = new List<string>();
                    Ops.Uninstall(dir, o.RemoveData, delegate (string s) { lines.Add(s); Info.Log(s); });
                    if (!o.Silent) MessageBox.Show(Info.Name + " was removed.", Info.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                if (o.Silent)
                {
                    if (o.Dir == null) o.Dir = Info.DefaultDir();
                    Ops.Install(o, delegate (string s) { Info.Log(s); });
                    if (o.Launch)
                    {
                        try { Process.Start(new ProcessStartInfo(Path.Combine(o.Dir, Info.ExeName)) { WorkingDirectory = o.Dir, UseShellExecute = false }); }
                        catch (Exception) { }
                    }
                    return 0;
                }
                if (o.Screenshot != null)
                {
                    using (SetupForm f = new SetupForm(o))
                    {
                        f.Show();
                        Application.DoEvents();
                        using (Bitmap bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height))
                        {
                            f.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                            bmp.Save(o.Screenshot, System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    return 0;
                }
                Application.Run(new SetupForm(o));
                return 0;
            }
            catch (Exception ex)
            {
                Info.Log("ERROR: " + ex);
                if (!o.Silent) MessageBox.Show(ex.Message, Info.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
