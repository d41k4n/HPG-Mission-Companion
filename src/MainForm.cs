// HPG Mission Companion - main window: service, actors and voices, Piper, radio effect, tools, about.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace HPGMissionCompanion
{
    internal sealed class CleanDialog : Form
    {
        readonly CheckedListBox list = new CheckedListBox();
        readonly Label note = new Label();
        readonly CheckBox backup = new CheckBox();
        readonly List<CleanGroup> groups;

        public CleanDialog(List<CleanGroup> groups)
        {
            this.groups = groups;
            Text = "Clean the mission work folder";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 372);

            Label info = new Label();
            info.Text = "Tick what to delete. Only the first group is ticked for you.";
            info.Location = new Point(12, 12);
            info.Size = new Size(536, 20);
            Controls.Add(info);

            list.Location = new Point(12, 36);
            list.Size = new Size(536, 148);
            list.CheckOnClick = true;
            foreach (CleanGroup g in groups) list.Items.Add(g, g.Default);
            list.SelectedIndexChanged += delegate { ShowNote(); };
            Controls.Add(list);

            note.Location = new Point(12, 192);
            note.Size = new Size(536, 84);
            note.ForeColor = SystemColors.GrayText;
            Controls.Add(note);

            backup.Text = "Back up the files to a zip first (folder backups next to the program)";
            backup.Checked = true;
            backup.AutoSize = true;
            backup.Location = new Point(12, 288);
            Controls.Add(backup);

            Button ok = new Button();
            ok.Text = "Delete";
            ok.Location = new Point(368, 328);
            ok.Size = new Size(85, 30);
            ok.Click += delegate { Confirm(); };
            Controls.Add(ok);
            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(463, 328);
            cancel.Size = new Size(85, 30);
            Controls.Add(cancel);
            CancelButton = cancel;
            if (list.Items.Count > 0) list.SelectedIndex = 0;
        }

        void ShowNote()
        {
            CleanGroup g = list.SelectedItem as CleanGroup;
            note.Text = g != null ? g.Note : "";
        }

        public bool BackupFirst { get { return backup.Checked; } }

        public List<CleanGroup> Selected
        {
            get
            {
                List<CleanGroup> r = new List<CleanGroup>();
                foreach (object o in list.CheckedItems) r.Add((CleanGroup)o);
                return r;
            }
        }

        void Confirm()
        {
            List<CleanGroup> sel = Selected;
            int n = 0;
            foreach (CleanGroup g in sel) n += g.Files.Count;
            if (n == 0) { MessageBox.Show("Nothing is ticked.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            bool config = false, settings = false;
            foreach (CleanGroup g in sel) { if (g.Key == "config") config = true; if (g.Key == "settings") settings = true; }
            string warn = "Delete " + n + " file" + (n == 1 ? "" : "s") + "?"
                + (config ? "\n\nThis includes your saved games and profiles." : "")
                + (settings ? "\n\nThis includes global.json: every setting of every HPG mission goes back to its default." : "")
                + (backup.Checked ? "\n\nA zip backup is made first." : "\n\nNo backup will be made.");
            if (MessageBox.Show(warn, Text, MessageBoxButtons.YesNo, config || settings ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.Yes) return;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    internal sealed class MainForm : Form
    {
        readonly bool startHidden;
        readonly bool exitWithSimArg;
        readonly string simRoot;
        bool allowShow;
        readonly Icon appIcon;

        readonly TabControl tabs = new TabControl();
        readonly Button saveButton = new Button();
        readonly CheckBox listenAllBox = new CheckBox();
        readonly Label saveLabel = new Label();
        bool suppress;
        bool dirty;

        // service tab
        readonly Label serviceLabel = new Label();
        readonly Button startStop = new Button();
        readonly NumericUpDown portBox = new NumericUpDown();
        readonly TextBox audioBox = new TextBox();
        readonly ComboBox defaultLangBox = new ComboBox();
        readonly TrackBar masterBar = new TrackBar();
        readonly Label masterLabel = new Label();
        readonly CheckBox simCheck = new CheckBox();
        readonly CheckBox minimizedCheck = new CheckBox();
        readonly CheckBox exitCheck = new CheckBox();
        readonly Label simInfo = new Label();
        readonly CheckBox inhibitCheck = new CheckBox();
        readonly Label inhibitInfo = new Label();
        readonly TextBox testBox = new TextBox();
        readonly RichTextBox logBox = new RichTextBox();
        readonly Button exitButton = new Button();
        readonly CheckBox autoIndexCheck = new CheckBox();
        readonly SynchronizationContext uiContext;
        bool indexRunning;
        List<SimTarget> sims = new List<SimTarget>();

        // actors tab
        readonly ComboBox langBox = new ComboBox();
        readonly Label[] actorLabel = new Label[Actors.Count];
        readonly ComboBox[] voiceBox = new ComboBox[Actors.Count];
        readonly TrackBar[] speedBar = new TrackBar[Actors.Count];
        readonly Label[] speedLabel = new Label[Actors.Count];
        readonly Button[] testButton = new Button[Actors.Count];
        readonly TextBox[] nameBox = new TextBox[7];       // the names of the crew and the technicians (actors 1-7), used when the crew names are not random
        static readonly string[] NameRoles = { "PLT", "CPL", "PAX3", "PAX1", "PAX2", "TECHM", "TECHF" };      // role of actor 1..7

        // piper tab
        readonly Label engineLabel = new Label();
        readonly Button engineButton = new Button();
        readonly ListView catalogList = new ListView();
        static readonly string[] CatalogColumns = { "Voice", "Region", "Quality", "Size", "Voice type", "Status" };
        int catalogSortColumn;                  // click a column header of the Piper voices list to sort by it; click again to reverse
        bool catalogSortAscending = true;
        readonly Button installButton = new Button();
        readonly Button removeButton = new Button();
        readonly Button testVoiceButton = new Button();
        readonly Button refreshButton = new Button();
        readonly ProgressBar progress = new ProgressBar();
        readonly Label progressLabel = new Label();
        Label piperHint;
        List<CatalogVoice> catalog = new List<CatalogVoice>();
        bool busy;
        bool catalogRequested;

        // voice packs tab
        readonly ListView packList = new ListView();
        readonly Label packInfo = new Label();
        readonly Panel dropZone = new Panel();
        readonly ComboBox bridgeBox = new ComboBox();
        readonly RadioButton varRandom = new RadioButton();
        readonly RadioButton varCoherent = new RadioButton();
        readonly RadioButton namesFunc = new RadioButton();
        readonly RadioButton namesRandom = new RadioButton();
        readonly Label bridgeInfo = new Label();

        // volume tab
        readonly CheckBox missionVolCheck = new CheckBox();
        readonly TrackBar attInBar = new TrackBar();
        readonly TrackBar attOutBar = new TrackBar();
        readonly Label attInLabel = new Label();
        readonly Label attOutLabel = new Label();
        readonly Label mixInfo = new Label();

        // radio tab
        readonly TrackBar noiseBar = new TrackBar();
        readonly Label noiseLabel = new Label();
        readonly ComboBox beepBox = new ComboBox();
        readonly TrackBar beepBar = new TrackBar();
        readonly Label beepLabel = new Label();
        readonly CheckBox tailCheck = new CheckBox();
        readonly ComboBox qualityBox = new ComboBox();

        // tools tab
        readonly ComboBox workCombo = new ComboBox();
        readonly Label workInfo = new Label();
        readonly ComboBox indexCombo = new ComboBox();
        readonly Button indexButton = new Button();
        readonly TextBox toolsOut = new TextBox();
        readonly ComboBox missionCombo = new ComboBox();
        readonly Button updateButton = new Button();
        readonly Label updateInfo = new Label();
        bool updateRunning;
        bool updateBalloon;
        readonly CheckBox autoUpdateCheck = new CheckBox();
        readonly Button whatsNewButton = new Button();

        Server server;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer();
        readonly ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();
        readonly string logFile;
        bool reallyExit;
        bool simSeen;
        DateTime simLastSeen = DateTime.UtcNow;
        DateTime nextSimCheck = DateTime.UtcNow;
        bool started;

        public MainForm(bool startHidden, bool exitWithSim, string logFile, string simRoot)
        {
            this.startHidden = startHidden;
            this.exitWithSimArg = exitWithSim;
            this.logFile = logFile;
            this.simRoot = simRoot;
            allowShow = !startHidden;

            Icon ico = null;
            try { ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            appIcon = ico ?? SystemIcons.Application;

            Text = AppInfo.Title;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(760, 650);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Icon = appIcon;

            uiContext = SynchronizationContext.Current;
            Ctx.Log = AppendLog;

            tabs.Location = new Point(12, 12);
            tabs.Size = new Size(736, 582);
            Controls.Add(tabs);
            BuildServiceTab();
            BuildActorsTab();
            BuildPiperTab();
            BuildPacksTab();
            BuildRadioTab();
            BuildVolumeTab();
            BuildToolsTab();
            BuildAboutTab();
            tabs.SelectedIndexChanged += delegate { if (tabs.SelectedIndex == 2) EnsureCatalog(); };

            saveButton.Text = "Save settings";
            saveButton.Location = new Point(12, 606);
            saveButton.Size = new Size(120, 32);
            saveButton.Click += delegate { SaveSettings(); };
            Controls.Add(saveButton);
            saveLabel.AutoSize = true;
            saveLabel.Location = new Point(144, 614);
            Controls.Add(saveLabel);
            exitButton.Text = "Exit program";
            exitButton.Location = new Point(628, 606);
            exitButton.Size = new Size(120, 32);
            exitButton.Click += delegate { ExitApp(true); };
            Controls.Add(exitButton);

            tray.Icon = appIcon;
            tray.Text = AppInfo.Name;
            ContextMenu menu = new ContextMenu();
            menu.MenuItems.Add("Open", delegate { ShowWindow(); });
            menu.MenuItems.Add("Exit", delegate { ExitApp(true); });
            tray.ContextMenu = menu;
            tray.DoubleClick += delegate { ShowWindow(); };
            tray.BalloonTipClicked += delegate
            {
                // the balloon of an automatic check that found something newer: open the Tools tab and ask the question
                if (!updateBalloon) return;
                updateBalloon = false;
                ShowWindow();
                foreach (TabPage tp in tabs.TabPages) if (tp.Text == "Tools") tabs.SelectedTab = tp;
                CheckUpdates(false);
            };
            tray.Visible = true;

            tick.Interval = 300;
            tick.Tick += delegate { OnTick(); };
            tick.Start();
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(allowShow ? value : false);
        }

        // test helper: saves a picture of every tab (--screenshot <base path>), taken from the screen so that every control is drawn
        public void Snapshot(string basePath)
        {
            allowShow = true;
            Show();
            Activate();
            for (int i = 0; i < 20; i++) { Application.DoEvents(); Thread.Sleep(100); }
            AppendLog("[SERVER] Started on 0.0.0.0:5997 (sample line)");
            AppendLog("[ACCEPT] client 127.0.0.1:50000 (sample line)");
            AppendLog("[RAW] {\"Text\":\"<actor1><vol:40><radio>Pilot to dispatch.\"} (sample line)");
            AppendLog("[VOICE] actor 1, EN, ryan - high - male (Piper, en_US), speed 1.0, vol 40, radio: Pilot to dispatch. (sample line)");
            AppendLog("[RENDER] 120 ms, 118 KB (sample line)");
            AppendLog("[WARN] voice 'xyz' not installed, using the actor voice (sample line)");
            AppendLog("[ERROR] File does not exist: missing.wav (sample line)");
            AppendLog("[INFO] Start with the simulator ON (--minimized --exit-with-sim) (sample line)");
            AppendLog("[INTERRUPT] (sample line)");
            for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(100); }
            for (int t = 0; t < tabs.TabPages.Count; t++)
            {
                tabs.SelectedIndex = t;
                for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(80); }
                SaveShot(basePath + "-" + t + ".png");
            }
            tabs.SelectedIndex = 0;
            MarkDirty();
            for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(80); }
            SaveShot(basePath + "-dirty.png");
            reallyExit = true;
            StopServer();
            PiperEngine.StopAll();
            tray.Visible = false;
        }

        void SaveShot(string path)
        {
            Rectangle r = Bounds;
            using (Bitmap bmp = new Bitmap(r.Width, r.Height))
            {
                try
                {
                    using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                }
                catch (Exception)
                {
                    DrawToBitmap(bmp, new Rectangle(0, 0, r.Width, r.Height));
                }
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        void ShowWindow()
        {
            allowShow = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        // ---- small helpers ----------------------------------------------------------------------
        static Label Lbl(Control page, string text, int x, int y, int w, int h)
        {
            Label l = new Label();
            l.Text = text;
            l.Location = new Point(x, y);
            if (w > 0) l.Size = new Size(w, h); else l.AutoSize = true;
            page.Controls.Add(l);
            return l;
        }

        static Button Btn(Control page, string text, int x, int y, int w, int h)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            page.Controls.Add(b);
            return b;
        }

        // ---- startup, log, timer ----------------------------------------------------------------
        void OnTick()
        {
            if (!started)
            {
                started = true;
                Startup();
            }
            string line;
            bool any = false;
            while (logQueue.TryDequeue(out line))
            {
                AppendColored(line);
                any = true;
            }
            if (any)
            {
                if (logBox.TextLength > 60000)
                {
                    int cut = logBox.GetFirstCharIndexFromLine(logBox.Lines.Length / 2);
                    if (cut > 0) { logBox.Select(0, cut); logBox.SelectedText = ""; }
                }
                logBox.SelectionStart = logBox.TextLength;
                logBox.ScrollToCaret();
            }
            if (inhibitInfo.Text != SimLink.Status) inhibitInfo.Text = SimLink.Status;
            string mix = Mixer.Describe();
            if (mixInfo.Text != mix) mixInfo.Text = mix;
            string bridge = Say.BridgeStatus();
            if (bridgeInfo.Text != bridge) bridgeInfo.Text = bridge;
            if (exitWithSimArg && DateTime.UtcNow >= nextSimCheck)
            {
                nextSimCheck = DateTime.UtcNow.AddSeconds(5);
                if (Tools.SimulatorRunning()) { simSeen = true; simLastSeen = DateTime.UtcNow; }
                else if (simSeen && (DateTime.UtcNow - simLastSeen).TotalSeconds > 15)
                {
                    AppendLog("[INFO] The simulator was closed: leaving.");
                    ExitApp(false);
                }
            }
        }

        // the log is dark green; each kind of message has its own colour, voices in yellow
        static Color LogColor(string line)
        {
            if (line.IndexOf("[ERROR]", StringComparison.Ordinal) >= 0) return Color.FromArgb(255, 110, 110);
            if (line.IndexOf("[WARN]", StringComparison.Ordinal) >= 0) return Color.FromArgb(255, 180, 70);
            if (line.IndexOf("[VOICE]", StringComparison.Ordinal) >= 0) return Color.FromArgb(255, 232, 110);
            if (line.IndexOf("[RAW]", StringComparison.Ordinal) >= 0) return Color.FromArgb(160, 210, 170);
            if (line.IndexOf("[SERVER]", StringComparison.Ordinal) >= 0 || line.IndexOf("[ACCEPT]", StringComparison.Ordinal) >= 0
                || line.IndexOf("[DISCONNECT]", StringComparison.Ordinal) >= 0) return Color.FromArgb(110, 225, 255);
            if (line.IndexOf("[INTERRUPT]", StringComparison.Ordinal) >= 0 || line.IndexOf("[PLAY]", StringComparison.Ordinal) >= 0
                || line.IndexOf("[RENDER]", StringComparison.Ordinal) >= 0 || line.IndexOf("[SILENT]", StringComparison.Ordinal) >= 0) return Color.FromArgb(175, 190, 255);
            if (line.IndexOf("[INFO]", StringComparison.Ordinal) >= 0) return Color.FromArgb(235, 255, 235);
            return Color.FromArgb(205, 240, 205);
        }

        void AppendColored(string line)
        {
            logBox.SelectionStart = logBox.TextLength;
            logBox.SelectionLength = 0;
            logBox.SelectionColor = LogColor(line);
            logBox.AppendText(line + "\n");
        }

        void AppendLog(string line)
        {
            string stamped = DateTime.Now.ToString("HH:mm:ss") + " " + line;
            logQueue.Enqueue(stamped);
            if (logFile != null)
            {
                try { lock (logQueue) { File.AppendAllText(logFile, stamped + Environment.NewLine); } } catch (Exception) { }
            }
        }

        void Startup()
        {
            VoiceLibrary.Refresh();
            VoicePacks.Refresh();
            LoadCatalogFromCache();
            sims = SimAutostart.Detect(simRoot);
            suppress = true;
            simCheck.Checked = false;
            if (sims.Count == 0)
            {
                simCheck.Enabled = false;
                simInfo.Text = "No Microsoft Flight Simulator installation found on this PC (the simulator writes UserCfg.opt in its folder once it has run).";
            }
            else
            {
                bool any = false;
                StringBuilder sb = new StringBuilder("Simulators found: ");
                for (int i = 0; i < sims.Count; i++)
                {
                    if (i > 0) sb.Append("; ");
                    sb.Append(sims[i].Name);
                    if (SimAutostart.IsEnabled(sims[i])) any = true;
                }
                simInfo.Text = sb.ToString();
                simCheck.Checked = any;
            }
            suppress = false;
            UpdateSimOptions();
            FillActors();
            FillCatalogList();
            FillPacks();
            UpdateEngineState();
            RefreshTools();
            if (Ctx.Settings.AutoIndex && indexCombo.Items.Count > 0) UpdateIndex(true);
            if (Ctx.Settings.AutoUpdateCheck) CheckUpdates(true);
            if (Ctx.Voices.Count == 0) AppendLog("[ERROR] No voice is installed.");
            StartServer(true);
            SimLink.Start(delegate { return Ctx.Settings.AutoAudioInhibit; });
        }

        void MarkDirty()
        {
            dirty = true;
            saveLabel.ForeColor = Color.Red;
            saveLabel.Text = "Modified settings, please save";
        }

        void SetSaveInfo(string text)
        {
            saveLabel.ForeColor = SystemColors.ControlText;
            saveLabel.Text = text;
        }

        void SaveSettings()
        {
            LangInfo li = CurrentLang();
            if (li != null)
            {
                for (int i = 0; i < Actors.Count; i++)
                {
                    VoiceInfo v = voiceBox[i].SelectedItem as VoiceInfo;
                    if (v != null) Ctx.Settings.Get(li.Code, i + 1).Voice = v.Id;
                }
            }
            try
            {
                Ctx.Settings.Save(Ctx.SettingsPath);
                dirty = false;
                SetSaveInfo("Settings saved.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Cannot save: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ---- service tab -------------------------------------------------------------------------
        void BuildServiceTab()
        {
            TabPage page = new TabPage("Service");
            tabs.TabPages.Add(page);

            startStop.Text = "Stop service";
            startStop.Location = new Point(10, 10);
            startStop.Size = new Size(130, 30);
            startStop.Click += delegate { if (server != null) StopServer(); else StartServer(false); };
            page.Controls.Add(startStop);
            Lbl(page, "Port:", 160, 17, 0, 0);
            portBox.Minimum = 1024;
            portBox.Maximum = 65535;
            portBox.Value = Ctx.Settings.Port;
            portBox.Location = new Point(196, 13);
            portBox.Size = new Size(70, 22);
            portBox.ValueChanged += delegate { Ctx.Settings.Port = (int)portBox.Value; MarkDirty(); };
            page.Controls.Add(portBox);
            serviceLabel.AutoSize = true;
            serviceLabel.Location = new Point(290, 17);
            serviceLabel.Text = "Service: stopped";
            page.Controls.Add(serviceLabel);
            listenAllBox.Text = "Accept connections from other computers (Windows Firewall may ask to open the port)";
            listenAllBox.AutoSize = true;
            listenAllBox.Location = new Point(160, 34);
            listenAllBox.Checked = Ctx.Settings.ListenAll;
            listenAllBox.CheckedChanged += delegate
            {
                Ctx.Settings.ListenAll = listenAllBox.Checked;
                MarkDirty();
                if (server != null) { StopServer(); StartServer(false); }      // the new address takes effect at once
            };
            page.Controls.Add(listenAllBox);

            Lbl(page, "Audio folder (wav files of the mission):", 10, 56, 0, 0);
            audioBox.Location = new Point(10, 76);
            audioBox.Size = new Size(610, 22);
            audioBox.Text = Ctx.AudioDir;
            audioBox.TextChanged += delegate { Ctx.AudioDir = audioBox.Text; Ctx.Settings.AudioFolder = audioBox.Text; MarkDirty(); };
            page.Controls.Add(audioBox);
            Button browse = Btn(page, "Browse...", 628, 74, 90, 26);
            browse.Click += delegate
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog())
                {
                    d.SelectedPath = Ctx.AudioDir;
                    if (d.ShowDialog(this) == DialogResult.OK) audioBox.Text = d.SelectedPath;
                }
            };

            Lbl(page, "Language of the texts that have no language tag:", 10, 114, 0, 0);
            defaultLangBox.DropDownStyle = ComboBoxStyle.DropDownList;
            defaultLangBox.Location = new Point(300, 110);
            defaultLangBox.Width = 140;
            foreach (LangInfo l in LangInfo.All) defaultLangBox.Items.Add(l);
            defaultLangBox.SelectedItem = LangInfo.ByCode(Ctx.Settings.DefaultLanguage);
            defaultLangBox.SelectedIndexChanged += delegate
            {
                LangInfo l = defaultLangBox.SelectedItem as LangInfo;
                if (l != null) { Ctx.Settings.DefaultLanguage = l.Code; MarkDirty(); }
            };
            page.Controls.Add(defaultLangBox);

            Lbl(page, "Master volume of the voices:", 10, 150, 0, 0);
            masterBar.Minimum = 0; masterBar.Maximum = 100; masterBar.TickFrequency = 10; masterBar.LargeChange = 10;
            masterBar.Value = Math.Max(0, Math.Min(100, Ctx.Settings.MasterVolume));
            masterBar.Location = new Point(190, 142);
            masterBar.Size = new Size(260, 36);
            masterBar.ValueChanged += delegate
            {
                masterLabel.Text = masterBar.Value + "%";
                Ctx.Settings.MasterVolume = masterBar.Value;
                MarkDirty();
            };
            page.Controls.Add(masterBar);
            masterLabel.Text = masterBar.Value + "%";
            masterLabel.AutoSize = true;
            masterLabel.Location = new Point(456, 150);
            page.Controls.Add(masterLabel);
            Lbl(page, "(the mission can set a text with <vol:50>)", 500, 150, 0, 0).ForeColor = SystemColors.GrayText;

            simCheck.Text = "Start this program when Microsoft Flight Simulator starts (2020 and 2024)";
            simCheck.AutoSize = true;
            inhibitCheck.Text = "Auto set default audio inhibit L:AUDIOINHIBIT";
            inhibitCheck.AutoSize = true;
            inhibitCheck.Checked = Ctx.Settings.AutoAudioInhibit;
            inhibitCheck.Location = new Point(10, 182);
            inhibitCheck.CheckedChanged += delegate { Ctx.Settings.AutoAudioInhibit = inhibitCheck.Checked; MarkDirty(); };
            page.Controls.Add(inhibitCheck);
            inhibitInfo.AutoSize = true;
            inhibitInfo.Location = new Point(360, 184);
            inhibitInfo.ForeColor = SystemColors.GrayText;
            inhibitInfo.Text = SimLink.Status;
            page.Controls.Add(inhibitInfo);

            simCheck.Location = new Point(10, 204);
            simCheck.CheckedChanged += delegate { UpdateSimOptions(); if (!suppress) ApplyAutostart(); };
            page.Controls.Add(simCheck);
            minimizedCheck.Text = "Start minimized, in the notification area next to the clock";
            minimizedCheck.AutoSize = true;
            minimizedCheck.Checked = Ctx.Settings.StartMinimized;
            minimizedCheck.Location = new Point(30, 226);
            minimizedCheck.CheckedChanged += delegate
            {
                Ctx.Settings.StartMinimized = minimizedCheck.Checked;
                if (!suppress) { MarkDirty(); if (simCheck.Checked) ApplyAutostart(); }
            };
            page.Controls.Add(minimizedCheck);
            exitCheck.Text = "Close it when the simulator closes";
            exitCheck.AutoSize = true;
            exitCheck.Checked = Ctx.Settings.ExitWithSim;
            exitCheck.Location = new Point(30, 248);
            exitCheck.CheckedChanged += delegate
            {
                Ctx.Settings.ExitWithSim = exitCheck.Checked;
                if (!suppress) { MarkDirty(); if (simCheck.Checked) ApplyAutostart(); }
            };
            page.Controls.Add(exitCheck);
            simInfo.Location = new Point(30, 270);
            simInfo.Size = new Size(690, 32);
            simInfo.ForeColor = SystemColors.GrayText;
            page.Controls.Add(simInfo);

            GroupBox box = new GroupBox();
            box.Text = "Mission text test and log";
            box.Location = new Point(10, 304);
            box.Size = new Size(708, 222);
            page.Controls.Add(box);
            Lbl(box, "Try a mission text (tags: <actor3> <Microsoft David> <ryan> <speed:1.2> <vol:60> <radio> <EN> <IT> <FR> <DE>):", 10, 22, 0, 0);
            testBox.Location = new Point(10, 44);
            testBox.Size = new Size(590, 22);
            testBox.Text = "<actor3><speed:1.1>Crew three, ready for the next step.";
            box.Controls.Add(testBox);
            Button speak = Btn(box, "Speak", 608, 42, 88, 26);
            speak.Click += delegate { Ctx.Player.EnqueueText(testBox.Text, true); };

            logBox.ReadOnly = true;
            logBox.BackColor = Color.FromArgb(10, 42, 24);
            logBox.ForeColor = Color.FromArgb(205, 240, 205);
            logBox.BorderStyle = BorderStyle.FixedSingle;
            logBox.ScrollBars = RichTextBoxScrollBars.Vertical;
            logBox.WordWrap = true;
            logBox.Font = new Font("Consolas", 9F);
            logBox.Location = new Point(10, 76);
            logBox.Size = new Size(686, 136);
            box.Controls.Add(logBox);

            Lbl(page, "Closing the window keeps the program running in the notification area.", 10, 532, 0, 0).ForeColor = SystemColors.GrayText;
        }
        // the two sub-options only make sense with the simulator option
        void UpdateSimOptions()
        {
            bool on = simCheck.Checked && sims.Count > 0;
            minimizedCheck.Enabled = on;
            exitCheck.Enabled = on;
        }

        string AutostartArgs()
        {
            List<string> a = new List<string>();
            if (minimizedCheck.Checked) a.Add("--minimized");
            if (exitCheck.Checked) a.Add("--exit-with-sim");
            return string.Join(" ", a.ToArray());
        }

        void ApplyAutostart()
        {
            string exe = Application.ExecutablePath;
            Ctx.Settings.StartWithSim = simCheck.Checked;
            StringBuilder done = new StringBuilder();
            foreach (SimTarget t in sims)
            {
                try
                {
                    if (simCheck.Checked) SimAutostart.Enable(t, exe, AutostartArgs()); else SimAutostart.Disable(t);
                    done.Append(t.Name).Append("; ");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Cannot change " + t.ExeXml + ":\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            AppendLog("[INFO] Start with the simulator " + (simCheck.Checked ? "ON (" + AutostartArgs() + ")" : "OFF") + ": " + done);
            try { Ctx.Settings.Save(Ctx.SettingsPath); } catch (Exception) { }
        }

        void StartServer(bool automatic)
        {
            Server s = new Server();
            int port = Ctx.Settings.Port;
            try
            {
                s.Start(port, Ctx.Settings.ListenAll);
            }
            catch (SocketException)
            {
                List<Process> old = new List<Process>();
                foreach (string n in new string[] { "HPGMissionAudioService", "HPGMissionAudioOneCore", "HPGMissionAudioLauncher", "HPGVoiceHub" })
                    old.AddRange(Process.GetProcessesByName(n));
                DialogResult answer = old.Count > 0
                    ? MessageBox.Show("Port " + port + " is busy: another HPG audio service is running.\nStop it and start this one?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                    : MessageBox.Show("Port " + port + " is busy: another program uses it.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) { SetServiceState(false); return; }
                foreach (Process p in old) { try { p.Kill(); p.WaitForExit(3000); } catch (Exception) { } }
                try { s.Start(port, Ctx.Settings.ListenAll); }
                catch (Exception ex) { AppendLog("[ERROR] " + ex.Message); SetServiceState(false); return; }
            }
            server = s;
            SetServiceState(true);
        }

        void StopServer()
        {
            if (server == null) return;
            server.Stop();
            server = null;
            SetServiceState(false);
        }

        void SetServiceState(bool running)
        {
            startStop.Text = running ? "Stop service" : "Start service";
            serviceLabel.Text = running ? "Service: running on port " + Ctx.Settings.Port : "Service: stopped";
            tray.Text = running ? AppInfo.Name + " - running" : AppInfo.Name + " - stopped";
        }

        // ---- actors tab --------------------------------------------------------------------------
        void BuildActorsTab()
        {
            TabPage page = new TabPage("Actors and voices");
            tabs.TabPages.Add(page);
            Lbl(page, "Language:", 10, 17, 0, 0);
            langBox.DropDownStyle = ComboBoxStyle.DropDownList;
            langBox.Location = new Point(78, 13);
            langBox.Width = 160;
            foreach (LangInfo l in LangInfo.All) langBox.Items.Add(l);
            langBox.SelectedIndex = 0;
            langBox.SelectedIndexChanged += delegate { FillActors(); FillCatalogList(); };
            page.Controls.Add(langBox);
            Lbl(page, "Choose the voice and the usual speed of each actor, for the language selected above. The mission picks the actor with a tag, " +
                "for example  <actor3> Hello  (the voice, the speed and the volume can also be forced by the mission). Changes work at once; press Save settings to keep them.",
                10, 46, 706, 36);
            Lbl(page, "Name (optional)", 376, 84, 0, 0).ForeColor = SystemColors.GrayText;
            for (int i = 0; i < Actors.Count; i++)
            {
                int index = i;
                int y = 102 + i * 38;
                actorLabel[i] = Lbl(page, (i + 1) + "  " + Actors.Names[i], 10, y + 5, 140, 20);

                voiceBox[i] = new ComboBox();
                voiceBox[i].DropDownStyle = ComboBoxStyle.DropDownList;
                voiceBox[i].Location = new Point(155, y + 2);
                voiceBox[i].Size = new Size(215, 24);
                if (i < nameBox.Length)
                {
                    nameBox[i] = new TextBox();
                    nameBox[i].Location = new Point(376, y + 2);
                    nameBox[i].Size = new Size(90, 24);
                    nameBox[i].MaxLength = 20;
                    nameBox[i].Enabled = Ctx.Settings.Names != "random";
                    string role = NameRoles[i];
                    string cur;
                    if (Ctx.Settings.CrewNames.TryGetValue(role, out cur)) nameBox[i].Text = cur;
                    nameBox[i].TextChanged += delegate
                    {
                        if (suppress) return;
                        string nm = nameBox[index].Text.Trim();
                        if (nm.Length == 0) Ctx.Settings.CrewNames.Remove(NameRoles[index]); else Ctx.Settings.CrewNames[NameRoles[index]] = nm;
                        MarkDirty();
                        Say.ApplyUserNamesNow();
                    };
                    page.Controls.Add(nameBox[i]);
                }
                voiceBox[i].SelectedIndexChanged += delegate { OnVoiceChanged(index); };
                page.Controls.Add(voiceBox[i]);

                speedBar[i] = new TrackBar();
                speedBar[i].Minimum = 5;
                speedBar[i].Maximum = 25;
                speedBar[i].Value = 10;
                speedBar[i].TickFrequency = 5;
                speedBar[i].LargeChange = 5;
                speedBar[i].Location = new Point(472, y - 3);
                speedBar[i].Size = new Size(150, 36);
                speedBar[i].ValueChanged += delegate { OnSpeedChanged(index); };
                page.Controls.Add(speedBar[i]);

                speedLabel[i] = Lbl(page, "1.0", 626, y + 5, 34, 20);

                testButton[i] = Btn(page, "Test", 662, y + 1, 56, 26);
                testButton[i].Click += delegate { TestActor(index); };
            }
            Lbl(page, "A name is spoken where a text uses it ({@PLT}); an empty name keeps the job role (Pilot, Copilot, Doc ...). Names are ignored while the crew names are random (Voice packs tab). To install additional voice packs, choose the Piper voices tab.", 10, 102 + Actors.Count * 38 + 10, 706, 34).ForeColor = SystemColors.GrayText;
        }

        LangInfo CurrentLang() { return (LangInfo)langBox.SelectedItem; }

        // the name boxes of the Actors tab work only while the crew names are not random
        void UpdateNameBoxes()
        {
            foreach (TextBox b in nameBox) if (b != null) b.Enabled = Ctx.Settings.Names != "random";
        }

        void FillActors()
        {
            LangInfo li = CurrentLang();
            if (li == null) return;
            List<VoiceInfo> fam = VoiceLibrary.OfFamily(Ctx.Voices, li.Family);
            suppress = true;
            for (int i = 0; i < Actors.Count; i++)
            {
                ActorSetting a = Ctx.Settings.Get(li.Code, i + 1);
                voiceBox[i].Items.Clear();
                foreach (VoiceInfo v in fam) voiceBox[i].Items.Add(v);
                VoiceInfo sel = VoiceLibrary.ById(fam, a.Voice);
                if (sel == null && fam.Count > 0) sel = fam[i % fam.Count];
                voiceBox[i].SelectedItem = sel;
                voiceBox[i].Enabled = fam.Count > 0;
                int sv = (int)Math.Round(a.Speed * 10);
                speedBar[i].Value = Math.Max(speedBar[i].Minimum, Math.Min(speedBar[i].Maximum, sv));
                speedLabel[i].Text = (speedBar[i].Value / 10.0).ToString("0.0", CultureInfo.InvariantCulture);
            }
            suppress = false;
            if (fam.Count == 0 && !dirty) SetSaveInfo("No " + li.Name + " voice is installed: add one in the Piper voices tab or in Windows (Settings).");
            else if (!dirty && saveLabel.Text.StartsWith("No ")) SetSaveInfo("");
        }

        void OnVoiceChanged(int i)
        {
            if (suppress) return;
            VoiceInfo v = voiceBox[i].SelectedItem as VoiceInfo;
            if (v == null) return;
            Ctx.Settings.Get(CurrentLang().Code, i + 1).Voice = v.Id;
            MarkDirty();
        }

        void OnSpeedChanged(int i)
        {
            speedLabel[i].Text = (speedBar[i].Value / 10.0).ToString("0.0", CultureInfo.InvariantCulture);
            if (suppress) return;
            Ctx.Settings.Get(CurrentLang().Code, i + 1).Speed = speedBar[i].Value / 10.0;
            MarkDirty();
        }

        void TestActor(int i)
        {
            LangInfo li = CurrentLang();
            string who = Actors.Names[i];
            int p = who.IndexOf(" (");
            if (p > 0) who = who.Substring(0, p);
            Ctx.Player.EnqueueText("<actor" + (i + 1) + "><" + li.Code + ">" + string.Format(li.Sample, who), true);
        }

        // ---- piper tab ---------------------------------------------------------------------------
        void BuildPiperTab()
        {
            TabPage page = new TabPage("Piper voices");
            tabs.TabPages.Add(page);
            engineLabel.Location = new Point(10, 14);
            engineLabel.Size = new Size(470, 40);
            page.Controls.Add(engineLabel);
            engineButton.Location = new Point(490, 10);
            engineButton.Size = new Size(228, 32);
            engineButton.Click += delegate { InstallEngine(); };
            page.Controls.Add(engineButton);

            piperHint = Lbl(page, "Select a voice and press Install (60 to 140 MB each, downloaded from the Piper voices repository).", 10, 60, 708, 34);

            catalogList.View = View.Details;
            catalogList.FullRowSelect = true;
            catalogList.MultiSelect = false;
            catalogList.HideSelection = false;
            catalogList.Location = new Point(10, 98);
            catalogList.Size = new Size(708, 330);
            catalogList.Columns.Add("Voice", 190);
            catalogList.Columns.Add("Region", 70);
            catalogList.Columns.Add("Quality", 80);
            catalogList.Columns.Add("Size", 70);
            catalogList.Columns.Add("Voice type", 90);
            catalogList.Columns.Add("Status", 190);
            catalogList.SelectedIndexChanged += delegate { UpdateEngineState(); };
            catalogList.ColumnClick += delegate (object s, ColumnClickEventArgs e) { OnCatalogColumnClick(e.Column); };
            page.Controls.Add(catalogList);

            installButton.Text = "Install voice";
            installButton.Location = new Point(10, 438);
            installButton.Size = new Size(120, 30);
            installButton.Click += delegate { InstallSelectedVoice(); };
            page.Controls.Add(installButton);
            removeButton.Text = "Remove voice";
            removeButton.Location = new Point(138, 438);
            removeButton.Size = new Size(120, 30);
            removeButton.Click += delegate { RemoveSelectedVoice(); };
            page.Controls.Add(removeButton);
            testVoiceButton.Text = "Listen";
            testVoiceButton.Location = new Point(266, 438);
            testVoiceButton.Size = new Size(100, 30);
            testVoiceButton.Click += delegate { TestSelectedVoice(); };
            page.Controls.Add(testVoiceButton);
            refreshButton.Text = "Refresh list";
            refreshButton.Location = new Point(598, 438);
            refreshButton.Size = new Size(120, 30);
            refreshButton.Click += delegate { FetchCatalog(); };
            page.Controls.Add(refreshButton);

            progress.Location = new Point(10, 482);
            progress.Size = new Size(708, 20);
            page.Controls.Add(progress);
            progressLabel.Location = new Point(10, 508);
            progressLabel.Size = new Size(708, 40);
            page.Controls.Add(progressLabel);
        }

        void UpdateEngineState()
        {
            if (Ctx.PiperInstalled)
            {
                engineLabel.Text = "Piper engine: installed.";
                engineButton.Text = "Reinstall the Piper engine";
            }
            else
            {
                engineLabel.Text = "Piper engine: not installed. It is needed to use the Piper voices (22 MB download).";
                engineButton.Text = "Install the Piper engine (22 MB)";
            }
            engineButton.Enabled = !busy;
            CatalogVoice sel = SelectedCatalog();
            bool installed = sel != null && PiperInstaller.IsInstalled(sel);
            installButton.Enabled = !busy && sel != null && !installed;
            removeButton.Enabled = !busy && installed;
            testVoiceButton.Enabled = !busy && installed && Ctx.PiperInstalled;
            refreshButton.Enabled = !busy;
        }

        CatalogVoice SelectedCatalog()
        {
            return catalogList.SelectedItems.Count > 0 ? catalogList.SelectedItems[0].Tag as CatalogVoice : null;
        }

        void LoadCatalogFromCache()
        {
            try
            {
                if (File.Exists(Ctx.CatalogPath)) catalog = PiperInstaller.LoadCatalog(File.ReadAllText(Ctx.CatalogPath, Encoding.UTF8));
            }
            catch (Exception) { }
        }

        void EnsureCatalog()
        {
            if (catalogRequested || catalog.Count > 0) return;
            FetchCatalog();
        }

        void FetchCatalog()
        {
            if (busy) return;
            catalogRequested = true;
            busy = true;
            UpdateEngineState();
            progressLabel.Text = "Getting the list of voices...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                List<CatalogVoice> list = null;
                try
                {
                    string json = Net.GetString(PiperInstaller.CatalogUrl);
                    list = PiperInstaller.LoadCatalog(json);
                    Directory.CreateDirectory(Path.GetDirectoryName(Ctx.CatalogPath));
                    File.WriteAllText(Ctx.CatalogPath, json, new UTF8Encoding(false));
                }
                catch (Exception ex) { error = ex.Message; }
                Ui(delegate
                {
                    busy = false;
                    if (error != null) progressLabel.Text = "Cannot get the list of voices (" + error + "). Check the internet connection and press Refresh list.";
                    else { catalog = list; progressLabel.Text = list.Count + " voices in the list."; }
                    FillCatalogList();
                    UpdateEngineState();
                });
            });
        }

        // the sort of the Piper voices list: column 0 name, 1 region, 2 quality, 3 size, 4 voice type, 5 installed; ties by name, then best quality first
        int CompareCatalog(CatalogVoice a, CatalogVoice b)
        {
            int r = 0;
            switch (catalogSortColumn)
            {
                case 1: r = string.Compare(a.LangCode, b.LangCode, StringComparison.OrdinalIgnoreCase); break;
                case 2: r = VoiceLibrary.QualityRank(a.Quality) - VoiceLibrary.QualityRank(b.Quality); break;
                case 3: r = a.OnnxSize.CompareTo(b.OnnxSize); break;
                case 4: r = string.Compare(a.Speakers > 1 ? "mixed" : VoiceLibrary.GenderHint(a.Name), b.Speakers > 1 ? "mixed" : VoiceLibrary.GenderHint(b.Name), StringComparison.OrdinalIgnoreCase); break;
                case 5: r = (PiperInstaller.IsInstalled(a) ? 0 : 1) - (PiperInstaller.IsInstalled(b) ? 0 : 1); break;      // ascending: installed first
            }
            if (r != 0) return catalogSortAscending ? r : -r;
            r = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            if (r != 0) return catalogSortColumn == 0 && !catalogSortAscending ? -r : r;
            return VoiceLibrary.QualityRank(b.Quality) - VoiceLibrary.QualityRank(a.Quality);
        }

        void OnCatalogColumnClick(int column)
        {
            if (column == catalogSortColumn) catalogSortAscending = !catalogSortAscending;
            else { catalogSortColumn = column; catalogSortAscending = true; }
            FillCatalogList();
        }

        void FillCatalogList()
        {
            LangInfo li = CurrentLang();
            if (li == null) return;
            CatalogVoice keep = SelectedCatalog();
            for (int i = 0; i < CatalogColumns.Length && i < catalogList.Columns.Count; i++)
                catalogList.Columns[i].Text = CatalogColumns[i] + (i == catalogSortColumn ? (catalogSortAscending ? "  ^" : "  v") : "");
            catalogList.Items.Clear();
            if (piperHint != null) piperHint.Text = "Voices for " + li.Name + " (change the language in the Actors and voices tab). Select one and press Install (60 to 140 MB each, from the Piper voices repository).";
            List<CatalogVoice> rows = new List<CatalogVoice>();
            foreach (CatalogVoice c in catalog) if (c.Family == li.Family) rows.Add(c);
            rows.Sort(CompareCatalog);
            foreach (CatalogVoice c in rows)
            {
                string gender = VoiceLibrary.GenderHint(c.Name);
                ListViewItem item = new ListViewItem(c.Speakers > 1 ? c.Name + " (" + c.Speakers + " speakers)" : c.Name);
                item.SubItems.Add(c.LangCode);
                item.SubItems.Add(c.Quality);
                item.SubItems.Add((c.OnnxSize / 1048576) + " MB");
                item.SubItems.Add(c.Speakers > 1 ? "mixed" : gender);
                item.SubItems.Add(PiperInstaller.IsInstalled(c) ? "Installed" : "");
                item.Tag = c;
                catalogList.Items.Add(item);
                if (keep != null && ReferenceEquals(keep, c)) { item.Selected = true; item.EnsureVisible(); }
            }
            if (rows.Count == 0 && catalog.Count == 0) progressLabel.Text = "The list of voices is not loaded yet.";
        }

        void InstallEngine()
        {
            if (busy) return;
            busy = true;
            UpdateEngineState();
            progress.Value = 0;
            progressLabel.Text = "Downloading the Piper engine...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                try { PiperInstaller.InstallEngine(ProgressReporter("Downloading the Piper engine")); }
                catch (Exception ex) { error = ex.Message; }
                Ui(delegate
                {
                    busy = false;
                    progress.Value = error == null ? 100 : 0;
                    progressLabel.Text = error == null ? "Piper engine installed." : "Cannot install the Piper engine: " + error;
                    VoiceLibrary.Refresh();
                    FillActors();
                    UpdateEngineState();
                });
            });
        }

        void InstallSelectedVoice()
        {
            CatalogVoice c = SelectedCatalog();
            if (c == null || busy) return;
            if (!Ctx.PiperInstalled)
            {
                MessageBox.Show("Install the Piper engine first (button at the top of this page).", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            busy = true;
            UpdateEngineState();
            progress.Value = 0;
            string label = "Downloading the voice " + c.Name + " (" + c.Quality + ")";
            progressLabel.Text = label + "...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                try { PiperInstaller.InstallVoice(c, ProgressReporter(label)); }
                catch (Exception ex) { error = ex.Message; }
                Ui(delegate
                {
                    busy = false;
                    progress.Value = error == null ? 100 : 0;
                    progressLabel.Text = error == null
                        ? "Voice " + c.Name + " installed. Its licence is in voices\\piper\\" + c.Key + ".MODEL_CARD.txt"
                        : "Cannot install the voice: " + error;
                    VoiceLibrary.Refresh();
                    FillActors();
                    FillCatalogList();
                    UpdateEngineState();
                });
            });
        }

        void RemoveSelectedVoice()
        {
            CatalogVoice c = SelectedCatalog();
            if (c == null || busy) return;
            if (MessageBox.Show("Remove the voice " + c.Name + " (" + c.Quality + ") from this PC?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            PiperInstaller.RemoveVoice(c);
            VoiceLibrary.Refresh();
            FillActors();
            FillCatalogList();
            UpdateEngineState();
            progressLabel.Text = "Voice " + c.Name + " removed.";
        }

        void TestSelectedVoice()
        {
            CatalogVoice c = SelectedCatalog();
            LangInfo li = CurrentLang();
            if (c == null || li == null) return;
            Ctx.Player.EnqueueText("<" + c.Key + "><" + li.Code + ">" + string.Format(li.Sample, c.Name), true);
        }

        Action<long, long> ProgressReporter(string label)
        {
            int last = -1;
            return delegate (long done, long total)
            {
                int pct = total > 0 ? (int)Math.Min(100, done * 100 / total) : 0;
                if (pct == last) return;
                last = pct;
                Ui(delegate
                {
                    progress.Value = pct;
                    progressLabel.Text = label + "... " + pct + "% (" + (done / 1048576) + " of " + (total / 1048576) + " MB)";
                });
            };
        }

        void Ui(Action a)
        {
            if (IsDisposed) return;
            try
            {
                if (uiContext != null) uiContext.Post(delegate { if (!IsDisposed) a(); }, null);
                else a();
            }
            catch (Exception) { }
        }

        // ---- voice packs tab ---------------------------------------------------------------------
        void BuildPacksTab()
        {
            TabPage page = new TabPage("Voice packs");
            tabs.TabPages.Add(page);
            Lbl(page, "A voice pack is an XML file with the texts of the mission messages, several options for each. The mission speaks a message with the tag <say:KEY>. " +
                "The packs are kept in the voicepacks folder next to this program; a new version of a pack moves the old one to voicepacks\\obsolete.", 10, 10, 706, 38);

            packList.View = View.Details;
            packList.FullRowSelect = true;
            packList.GridLines = true;
            packList.HideSelection = false;
            packList.MultiSelect = false;
            packList.Location = new Point(10, 54);
            packList.Size = new Size(706, 150);
            packList.Columns.Add("Pack", 110);
            packList.Columns.Add("Language", 70);
            packList.Columns.Add("Version", 62);
            packList.Columns.Add("Messages", 72);
            packList.Columns.Add("State", 370);
            page.Controls.Add(packList);

            dropZone.Location = new Point(10, 214);
            dropZone.Size = new Size(706, 70);
            dropZone.BorderStyle = BorderStyle.FixedSingle;
            dropZone.BackColor = Color.FromArgb(236, 244, 255);
            dropZone.AllowDrop = true;
            dropZone.Paint += delegate (object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Color.FromArgb(90, 130, 190), 2))
                {
                    pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                    e.Graphics.DrawRectangle(pen, 6, 6, dropZone.Width - 14, dropZone.Height - 14);
                }
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    e.Graphics.DrawString("Drop a voice pack here (.xml file), or press Browse", Font, Brushes.SteelBlue, new RectangleF(0, 0, dropZone.Width, dropZone.Height), sf);
                }
            };
            dropZone.DragEnter += OnPackDrag;
            dropZone.DragOver += OnPackDrag;
            dropZone.DragDrop += delegate (object sender, DragEventArgs e)
            {
                string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null) InstallPacks(files);
            };
            page.Controls.Add(dropZone);

            Button browse = Btn(page, "Browse...", 10, 292, 100, 30);
            browse.Click += delegate
            {
                using (OpenFileDialog d = new OpenFileDialog())
                {
                    d.Title = "Choose a voice pack";
                    d.Filter = "Voice pack (*.xml)|*.xml|All files (*.*)|*.*";
                    d.Multiselect = true;
                    if (d.ShowDialog(this) == DialogResult.OK) InstallPacks(d.FileNames);
                }
            };
            Button open = Btn(page, "Open folder", 120, 292, 110, 30);
            open.Click += delegate
            {
                try { Directory.CreateDirectory(VoicePacks.Dir); System.Diagnostics.Process.Start(VoicePacks.Dir); }
                catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            Button reload = Btn(page, "Reload", 240, 292, 90, 30);
            reload.Click += delegate { VoicePacks.Refresh(); FillPacks(); packInfo.Text = "Packs read again from " + VoicePacks.Dir; };
            Button remove = Btn(page, "Remove selected", 340, 292, 130, 30);
            remove.Click += delegate { RemovePack(); };

            GroupBox gb = new GroupBox();
            gb.Text = "Simulator link of the packs";
            gb.Location = new Point(10, 330);
            gb.Size = new Size(706, 128);
            page.Controls.Add(gb);
            Lbl(gb, "Pack played when the mission increments its LVARs (L:AUDIOINHIBIT = 1, voice code APP):", 10, 22, 0, 0);
            bridgeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            bridgeBox.Location = new Point(10, 42);
            bridgeBox.Width = 200;
            bridgeBox.SelectedIndexChanged += delegate
            {
                if (suppress || bridgeBox.SelectedItem == null) return;
                Ctx.Settings.BridgePack = (string)bridgeBox.SelectedItem;
                MarkDirty();
                Say.ApplySettings();
            };
            gb.Controls.Add(bridgeBox);
            Lbl(gb, "Texts:", 230, 46, 0, 0);
            Panel varPanel = new Panel();            // radio buttons of the same container exclude each other: one panel for each pair
            varPanel.Location = new Point(276, 38);
            varPanel.Size = new Size(420, 26);
            gb.Controls.Add(varPanel);
            varRandom.Text = "Random each time";
            varRandom.AutoSize = true;
            varRandom.Location = new Point(4, 4);
            varCoherent.Text = "Coherent in a conversation";
            varCoherent.AutoSize = true;
            varCoherent.Location = new Point(144, 4);
            varPanel.Controls.Add(varRandom);
            varPanel.Controls.Add(varCoherent);
            Lbl(gb, "Crew names:", 10, 78, 0, 0);
            Panel namesPanel = new Panel();
            namesPanel.Location = new Point(86, 70);
            namesPanel.Size = new Size(500, 26);
            gb.Controls.Add(namesPanel);
            namesFunc.Text = "Job roles, or the names typed in Actors";
            namesFunc.AutoSize = true;
            namesFunc.Location = new Point(4, 4);
            namesRandom.Text = "Random names for each shift";
            namesRandom.AutoSize = true;
            namesRandom.Location = new Point(270, 4);
            namesPanel.Controls.Add(namesFunc);
            namesPanel.Controls.Add(namesRandom);
            varRandom.Checked = Ctx.Settings.Variety != "coherent"; varCoherent.Checked = Ctx.Settings.Variety == "coherent";
            namesFunc.Checked = Ctx.Settings.Names != "random"; namesRandom.Checked = Ctx.Settings.Names == "random";
            varCoherent.CheckedChanged += delegate { if (suppress) return; Ctx.Settings.Variety = varCoherent.Checked ? "coherent" : "random"; MarkDirty(); Say.ApplySettings(); };
            namesRandom.CheckedChanged += delegate { if (suppress) return; Ctx.Settings.Names = namesRandom.Checked ? "random" : "functions"; MarkDirty(); Say.ApplySettings(); UpdateNameBoxes(); };
            UpdateNameBoxes();
            bridgeInfo.Location = new Point(10, 102);
            bridgeInfo.Size = new Size(686, 22);
            bridgeInfo.ForeColor = SystemColors.GrayText;
            gb.Controls.Add(bridgeInfo);

            packInfo.Location = new Point(10, 464);
            packInfo.Size = new Size(706, 34);
            packInfo.ForeColor = SystemColors.GrayText;
            page.Controls.Add(packInfo);
            Lbl(page, "In the mission:  {\"speak\":\"<pack:EN:1>\"}  declares the pack and the version it needs;  {\"speak\":\"<radio><say:CPL.5>\"}  speaks a message;  " +
                "{\"speak\":{\"text\":\"<say:KEY>{0}//{1}\",\"params\":[...]}}  fills {0} and {1} in the text. All the tags and the pack format are in the guide of the About tab.",
                10, 500, 706, 48);
        }

        static bool HasXml(DragEventArgs e)
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return false;
            foreach (string f in files) if (f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        void OnPackDrag(object sender, DragEventArgs e)
        {
            e.Effect = HasXml(e) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        void FillPacks()
        {
            suppress = true;
            bridgeBox.Items.Clear();
            foreach (VoicePack bp in VoicePacks.Packs) if (bp.Usable) bridgeBox.Items.Add(bp.Name);
            string chosen = Say.BridgePackName;
            if (bridgeBox.Items.Contains(chosen)) bridgeBox.SelectedItem = chosen; else if (bridgeBox.Items.Count > 0) bridgeBox.SelectedIndex = 0;
            suppress = false;
            packList.Items.Clear();
            foreach (VoicePack p in VoicePacks.Packs)
            {
                ListViewItem it = new ListViewItem(p.Name.Length > 0 ? p.Name : "(" + Path.GetFileName(p.File) + ")");
                it.SubItems.Add(p.Language);
                it.SubItems.Add(p.Version > 0 ? p.Version.ToString() : "-");
                it.SubItems.Add(p.Messages.Count.ToString());
                string state = p.Problems.Count == 0 ? "OK" : (p.Usable ? p.Problems.Count + " problem(s): " : "NOT USABLE: ") + p.Problems[0];
                it.SubItems.Add(state);
                it.Tag = p;
                if (!p.Usable) it.ForeColor = Color.Firebrick;
                else if (p.Problems.Count > 0) it.ForeColor = Color.DarkOrange;
                packList.Items.Add(it);
            }
            if (packList.Items.Count == 0) packInfo.Text = "No voice pack installed. Drop an XML file above.";
        }

        void InstallPacks(string[] files)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string f in files)
            {
                if (!f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) { sb.AppendLine(Path.GetFileName(f) + ": not an XML file."); continue; }
                try
                {
                    sb.AppendLine(VoicePacks.Install(f, delegate (string q) { return MessageBox.Show(this, q, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes; }));
                }
                catch (Exception ex) { sb.AppendLine("ERROR: " + ex.Message); }
            }
            FillPacks();
            packInfo.ForeColor = sb.ToString().Contains("ERROR") ? Color.Firebrick : SystemColors.ControlText;
            packInfo.Text = sb.ToString().Trim();
            AppendLog("[INFO] Voice packs: " + sb.ToString().Trim().Replace("\r\n", " "));
        }

        void RemovePack()
        {
            if (packList.SelectedItems.Count == 0) { packInfo.Text = "Select a pack in the list first."; return; }
            VoicePack p = packList.SelectedItems[0].Tag as VoicePack;
            if (p == null) return;
            if (MessageBox.Show(this, "Remove the pack " + (p.Name.Length > 0 ? p.Name : Path.GetFileName(p.File)) + "?\n\nThe file moves to voicepacks\\obsolete.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                Directory.CreateDirectory(VoicePacks.ObsoleteDir);
                File.Move(p.File, Path.Combine(VoicePacks.ObsoleteDir, Path.GetFileNameWithoutExtension(p.File) + "-removed-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".xml"));
                VoicePacks.Refresh();
                FillPacks();
                packInfo.ForeColor = SystemColors.ControlText;
                packInfo.Text = "Removed " + p.Name + ".";
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        // ---- radio tab ---------------------------------------------------------------------------
        void BuildRadioTab()
        {
            TabPage page = new TabPage("Radio effect");
            tabs.TabPages.Add(page);
            RadioSettings rs = Ctx.Settings.Radio;
            Lbl(page, "A text with the tag <radio> is played like a radio call: a beep at the start, hiss under the voice, a band filter like a radio speaker and a short squelch burst at the end. " +
                "<radio:off> ends the effect. Without the tag the voice is clean. These values are the same for every voice.", 10, 12, 706, 50);

            Lbl(page, "Hiss under the voice:", 10, 82, 0, 0);
            noiseBar.Minimum = 0; noiseBar.Maximum = 100; noiseBar.TickFrequency = 10; noiseBar.LargeChange = 10;
            noiseBar.Value = rs.NoiseLevel;
            noiseBar.Location = new Point(190, 74);
            noiseBar.Size = new Size(300, 36);
            noiseBar.ValueChanged += delegate { noiseLabel.Text = noiseBar.Value + "%"; if (!suppress) { Ctx.Settings.Radio.NoiseLevel = noiseBar.Value; MarkDirty(); } };
            page.Controls.Add(noiseBar);
            noiseLabel.Text = rs.NoiseLevel + "%";
            noiseLabel.AutoSize = true;
            noiseLabel.Location = new Point(496, 82);
            page.Controls.Add(noiseLabel);

            Lbl(page, "Start beep:", 10, 130, 0, 0);
            beepBox.DropDownStyle = ComboBoxStyle.DropDownList;
            beepBox.Location = new Point(190, 126);
            beepBox.Width = 160;
            foreach (string b in AudioFx.BeepTypes) beepBox.Items.Add(b);
            beepBox.SelectedItem = rs.Beep;
            if (beepBox.SelectedIndex < 0) beepBox.SelectedItem = "Beep";
            beepBox.SelectedIndexChanged += delegate { if (!suppress && beepBox.SelectedItem != null) { Ctx.Settings.Radio.Beep = (string)beepBox.SelectedItem; MarkDirty(); } };
            page.Controls.Add(beepBox);

            Lbl(page, "Beep volume:", 10, 174, 0, 0);
            beepBar.Minimum = 0; beepBar.Maximum = 100; beepBar.TickFrequency = 10; beepBar.LargeChange = 10;
            beepBar.Value = rs.BeepLevel;
            beepBar.Location = new Point(190, 166);
            beepBar.Size = new Size(300, 36);
            beepBar.ValueChanged += delegate { beepLabel.Text = beepBar.Value + "%"; if (!suppress) { Ctx.Settings.Radio.BeepLevel = beepBar.Value; MarkDirty(); } };
            page.Controls.Add(beepBar);
            beepLabel.Text = rs.BeepLevel + "%";
            beepLabel.AutoSize = true;
            beepLabel.Location = new Point(496, 174);
            page.Controls.Add(beepLabel);

            tailCheck.Text = "Squelch burst when the transmission ends";
            tailCheck.AutoSize = true;
            tailCheck.Checked = rs.Tail;
            tailCheck.Location = new Point(10, 218);
            tailCheck.CheckedChanged += delegate { if (!suppress) { Ctx.Settings.Radio.Tail = tailCheck.Checked; MarkDirty(); } };
            page.Controls.Add(tailCheck);

            Lbl(page, "Radio sound:", 10, 254, 0, 0);
            qualityBox.DropDownStyle = ComboBoxStyle.DropDownList;
            qualityBox.Location = new Point(190, 250);
            qualityBox.Width = 160;
            foreach (string q in AudioFx.Qualities) qualityBox.Items.Add(q);
            qualityBox.SelectedItem = rs.Quality;
            if (qualityBox.SelectedIndex < 0) qualityBox.SelectedItem = "Aviation";
            qualityBox.SelectedIndexChanged += delegate { if (!suppress && qualityBox.SelectedItem != null) { Ctx.Settings.Radio.Quality = (string)qualityBox.SelectedItem; MarkDirty(); } };
            page.Controls.Add(qualityBox);
            Lbl(page, "Clean: wide band.  Aviation: narrow band, compressed.  Rough: narrower, distorted.", 190, 282, 0, 0).ForeColor = SystemColors.GrayText;

            Button withRadio = Btn(page, "Listen with the radio effect", 10, 316, 210, 32);
            withRadio.Click += delegate { TestRadio(true); };
            Button without = Btn(page, "Listen without it", 230, 316, 150, 32);
            without.Click += delegate { TestRadio(false); };

            Lbl(page, "In the mission text:", 10, 370, 0, 0);
            Lbl(page, "<radio>Pilot to dispatch, ready.", 30, 392, 0, 0);
            Lbl(page, "<vol:60><radio>Quieter radio call.", 30, 414, 0, 0);
            Lbl(page, "<radio:off>Back to the clean voice.", 30, 436, 0, 0);
            Lbl(page, "<radio><beep:double>Radio call with another beep (none, click, beep, double, chirp).", 30, 458, 0, 0);
            Lbl(page, "The beep, the hiss and the filter follow the volume of the text (<vol:..>) and the master volume of the Service tab.", 10, 488, 706, 40).ForeColor = SystemColors.GrayText;
        }

        // ---- volume tab --------------------------------------------------------------------------
        void BuildVolumeTab()
        {
            TabPage page = new TabPage("Volume");
            tabs.TabPages.Add(page);
            Lbl(page, "How loud a voice is: the master volume of the Service tab, the volume tag of the text (<vol:..>), the volumes of the mission and the position of the view. " +
                "The level is applied to each line when its turn to be spoken comes (not while it is playing): a change of a slider is heard from the next line.", 10, 12, 706, 50);

            missionVolCheck.Text = "Follow the volumes of the mission";
            missionVolCheck.AutoSize = true;
            missionVolCheck.Location = new Point(10, 74);
            missionVolCheck.Checked = Ctx.Settings.UseMissionVolumes;
            missionVolCheck.CheckedChanged += delegate { Ctx.Settings.UseMissionVolumes = missionVolCheck.Checked; if (!suppress) MarkDirty(); };
            page.Controls.Add(missionVolCheck);
            Lbl(page, "Crew voices and sounds follow L:VOLUME_CREW, the checklist voice (actor 11) follows L:VOLUME_CHK: the two sliders of the mission tablet. 100 is the full level.", 30, 98, 690, 36).ForeColor = SystemColors.GrayText;

            Lbl(page, "A voice (not a radio call) from inside, view outside the helicopter:", 10, 140, 0, 0);
            attOutBar.Minimum = 0; attOutBar.Maximum = 30; attOutBar.TickFrequency = 3; attOutBar.LargeChange = 3;
            attOutBar.Value = Ctx.Settings.AttenCamOutside;
            attOutBar.Location = new Point(10, 160);
            attOutBar.Size = new Size(420, 36);
            attOutBar.ValueChanged += delegate { attOutLabel.Text = attOutBar.Value == 0 ? "off" : "-" + attOutBar.Value + " dB"; Ctx.Settings.AttenCamOutside = attOutBar.Value; if (!suppress) MarkDirty(); };
            page.Controls.Add(attOutBar);
            attOutLabel.AutoSize = true;
            attOutLabel.Location = new Point(440, 168);
            attOutLabel.Text = attOutBar.Value == 0 ? "off" : "-" + attOutBar.Value + " dB";
            page.Controls.Add(attOutLabel);

            Lbl(page, "A voice from outside (technicians, ground unit, patient, crew on the ground), view inside the helicopter:", 10, 210, 0, 0);
            attInBar.Minimum = 0; attInBar.Maximum = 30; attInBar.TickFrequency = 3; attInBar.LargeChange = 3;
            attInBar.Value = Ctx.Settings.AttenCamInside;
            attInBar.Location = new Point(10, 230);
            attInBar.Size = new Size(420, 36);
            attInBar.ValueChanged += delegate { attInLabel.Text = attInBar.Value == 0 ? "off" : "-" + attInBar.Value + " dB"; Ctx.Settings.AttenCamInside = attInBar.Value; if (!suppress) MarkDirty(); };
            page.Controls.Add(attInBar);
            attInLabel.AutoSize = true;
            attInLabel.Location = new Point(440, 238);
            attInLabel.Text = attInBar.Value == 0 ? "off" : "-" + attInBar.Value + " dB";
            page.Controls.Add(attInLabel);

            Lbl(page, "Radio calls are not changed: they are heard in the headset wherever the view is. Who is inside comes from the context of the mission (L:HMC_CREW_PRESENT), " +
                "the view from the simulator (CAMERA STATE: 2 is the cockpit, every other view counts as outside). Without the mission context or the view nothing is reduced. " +
                "Sound files (ringtones) follow L:VOLUME_CREW only. The attenuation is a fixed level, not a distance: a voice is as loud at one metre as at five.", 10, 284, 706, 80).ForeColor = SystemColors.GrayText;
            mixInfo.Location = new Point(10, 380);
            mixInfo.Size = new Size(706, 22);
            page.Controls.Add(mixInfo);
        }

        void TestRadio(bool radio)
        {
            LangInfo li = CurrentLang();
            Ctx.Player.EnqueueText("<actor1><" + li.Code + ">" + (radio ? "<radio>" : "<radio:off>") + string.Format(li.Sample, Actors.Names[0]), true);
        }

        // ---- tools tab ---------------------------------------------------------------------------
        void BuildToolsTab()
        {
            TabPage page = new TabPage("Tools");
            tabs.TabPages.Add(page);

            Lbl(page, "Mission work folder: the tables, saved games and snapshots the mission writes. Close the simulator before cleaning it.", 10, 12, 706, 36);
            workCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            workCombo.Location = new Point(10, 50);
            workCombo.Size = new Size(540, 24);
            workCombo.SelectedIndexChanged += delegate { UpdateWorkInfo(); };
            page.Controls.Add(workCombo);
            Button browseWork = Btn(page, "Browse...", 558, 48, 76, 27);
            browseWork.Click += delegate
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog())
                {
                    d.Description = "Choose the work folder of the mission (hpg-airbus-h145-ap\\work)";
                    if (d.ShowDialog(this) == DialogResult.OK) { Ctx.Settings.WorkFolder = d.SelectedPath; MarkDirty(); RefreshTools(); }
                }
            };
            Button rescan = Btn(page, "Rescan", 642, 48, 76, 27);
            rescan.Click += delegate { RefreshTools(); };

            Button openWork = Btn(page, "Open folder", 10, 84, 140, 30);
            openWork.Click += delegate { OpenWork(); };
            Button clean = Btn(page, "Clean up...", 160, 84, 140, 30);
            clean.Click += delegate { CleanWork(); };
            workInfo.Size = new Size(708, 18);
            workInfo.Location = new Point(10, 120);
            workInfo.ForeColor = SystemColors.GrayText;
            page.Controls.Add(workInfo);

            Lbl(page, "Mission index: the HPG tool Update Mission Index, to run after a mission file is added or changed in the Community folder.", 10, 150, 706, 36);
            indexCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            indexCombo.Location = new Point(10, 188);
            indexCombo.Size = new Size(624, 24);
            page.Controls.Add(indexCombo);
            Button browseIdx = Btn(page, "Browse...", 642, 186, 76, 27);
            browseIdx.Click += delegate
            {
                using (OpenFileDialog d = new OpenFileDialog())
                {
                    d.Title = "Choose Update Mission Index.cmd";
                    d.Filter = "Update Mission Index.cmd|*.cmd|All files|*.*";
                    if (d.ShowDialog(this) == DialogResult.OK) { Ctx.Settings.IndexTool = d.FileName; MarkDirty(); RefreshTools(); }
                }
            };
            indexButton.Text = "Update mission index";
            indexButton.Location = new Point(10, 222);
            indexButton.Size = new Size(180, 30);
            indexButton.Click += delegate { UpdateIndex(false); };
            page.Controls.Add(indexButton);
            autoIndexCheck.Text = "Update the index automatically when the app starts";
            autoIndexCheck.AutoSize = true;
            autoIndexCheck.Checked = Ctx.Settings.AutoIndex;
            autoIndexCheck.Location = new Point(204, 228);
            autoIndexCheck.CheckedChanged += delegate { Ctx.Settings.AutoIndex = autoIndexCheck.Checked; MarkDirty(); };
            page.Controls.Add(autoIndexCheck);

            Lbl(page, "Updates: the R&&E mission (GitHub release, Google Drive link as a fallback) and this program (GitHub). The check runs when the program starts and on the button, and only reads: " +
                "nothing is downloaded without the question. Then the mission file is replaced (the old one is kept beside it, extension .json.bak), the index is updated and, last, the program.", 10, 262, 706, 50);
            missionCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            missionCombo.Location = new Point(10, 316);
            missionCombo.Size = new Size(624, 24);
            missionCombo.Items.Add("(the mission file to update is looked for when you press Check for updates)");
            missionCombo.SelectedIndex = 0;
            missionCombo.SelectedIndexChanged += delegate
            {
                MissionCopy c = missionCombo.SelectedItem as MissionCopy;
                if (c != null && !string.Equals(Ctx.Settings.MissionFile, c.Path, StringComparison.OrdinalIgnoreCase)) { Ctx.Settings.MissionFile = c.Path; MarkDirty(); }
            };
            page.Controls.Add(missionCombo);
            Button browseMission = Btn(page, "Browse...", 642, 314, 76, 27);
            browseMission.Click += delegate
            {
                using (OpenFileDialog d = new OpenFileDialog())
                {
                    d.Title = "Choose the R&E mission file (everywhere_all.json in a hpgmission folder)";
                    d.Filter = "Mission files|*.json|All files|*.*";
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    Ctx.Settings.MissionFile = d.FileName; MarkDirty();
                    ToolsLog("Mission file chosen: " + d.FileName + " (press Check for updates)");
                }
            };
            updateButton.Text = "Check for updates";
            updateButton.Location = new Point(10, 348);
            updateButton.Size = new Size(180, 30);
            updateButton.Click += delegate { CheckUpdates(false); };
            page.Controls.Add(updateButton);
            autoUpdateCheck.Text = "Check for updates when the program starts";
            autoUpdateCheck.AutoSize = true;
            autoUpdateCheck.Checked = Ctx.Settings.AutoUpdateCheck;
            whatsNewButton.Text = "What's new...";
            whatsNewButton.Location = new Point(200, 348);
            whatsNewButton.Size = new Size(120, 30);
            whatsNewButton.Click += delegate { ShowWhatsNew(); };
            page.Controls.Add(whatsNewButton);
            autoUpdateCheck.Location = new Point(332, 354);
            autoUpdateCheck.CheckedChanged += delegate { Ctx.Settings.AutoUpdateCheck = autoUpdateCheck.Checked; MarkDirty(); };
            page.Controls.Add(autoUpdateCheck);
            updateInfo.AutoSize = true;
            updateInfo.Location = new Point(620, 355);
            updateInfo.ForeColor = SystemColors.GrayText;
            updateInfo.Text = "Program version " + AppInfo.Version;
            page.Controls.Add(updateInfo);

            toolsOut.Multiline = true;
            toolsOut.ReadOnly = true;
            toolsOut.ScrollBars = ScrollBars.Vertical;
            toolsOut.Font = new Font("Consolas", 9F);
            toolsOut.Location = new Point(10, 388);
            toolsOut.Size = new Size(708, 154);
            page.Controls.Add(toolsOut);
        }

        void ToolsLog(string line)
        {
            toolsOut.AppendText(line + Environment.NewLine);
        }

        void RefreshTools()
        {
            string keepWork = (workCombo.SelectedItem as WorkFolderInfo) != null ? ((WorkFolderInfo)workCombo.SelectedItem).Path : Ctx.Settings.WorkFolder;
            workCombo.Items.Clear();
            List<WorkFolderInfo> works = Tools.FindWorkFolders(Ctx.Settings.WorkFolder);
            foreach (WorkFolderInfo w in works) workCombo.Items.Add(w);
            for (int i = 0; i < workCombo.Items.Count; i++)
                if (string.Equals(((WorkFolderInfo)workCombo.Items[i]).Path, keepWork, StringComparison.OrdinalIgnoreCase)) { workCombo.SelectedIndex = i; break; }
            if (workCombo.SelectedIndex < 0 && workCombo.Items.Count > 0) workCombo.SelectedIndex = 0;
            UpdateWorkInfo();

            indexCombo.Items.Clear();
            foreach (string t in Tools.FindIndexTools(Ctx.Settings.IndexTool)) indexCombo.Items.Add(t);
            if (indexCombo.Items.Count > 0)
            {
                int pick = 0;
                for (int i = 0; i < indexCombo.Items.Count; i++)
                    if (((string)indexCombo.Items[i]).IndexOf("hpg-airbus-h145\\", StringComparison.OrdinalIgnoreCase) >= 0) { pick = i; break; }
                indexCombo.SelectedIndex = pick;
            }
            indexButton.Enabled = indexCombo.Items.Count > 0;
            AppendLog("[INFO] Tools: " + works.Count + " work folder(s), " + indexCombo.Items.Count + " index tool(s) found");
        }

        void UpdateWorkInfo()
        {
            WorkFolderInfo w = workCombo.SelectedItem as WorkFolderInfo;
            workInfo.Text = w == null ? "No work folder found: use Browse." : w.Path;
        }

        void OpenWork()
        {
            WorkFolderInfo w = workCombo.SelectedItem as WorkFolderInfo;
            if (w == null) { MessageBox.Show("No work folder found: use Browse.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            try { Process.Start("explorer.exe", "\"" + w.Path + "\""); }
            catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        void CleanWork()
        {
            WorkFolderInfo w = workCombo.SelectedItem as WorkFolderInfo;
            if (w == null) { MessageBox.Show("No work folder found: use Browse.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (Tools.SimulatorRunning() &&
                MessageBox.Show("Microsoft Flight Simulator is running. Files in use cannot be deleted and the mission can write them again.\n\nContinue anyway?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            List<CleanGroup> groups = Tools.Scan(w.Path);
            if (groups.Count == 0) { MessageBox.Show("There is nothing to clean in this folder.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            List<string> files = new List<string>();
            bool backup;
            using (CleanDialog d = new CleanDialog(groups))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                backup = d.BackupFirst;
                foreach (CleanGroup g in d.Selected) files.AddRange(g.Files);
            }
            ToolsLog("--- Clean up " + DateTime.Now.ToString("HH:mm:ss") + " " + w.Path);
            if (backup)
            {
                try
                {
                    string zip = Tools.Backup(w.Path, files, Path.Combine(Ctx.BaseDir, "backups"));
                    ToolsLog("Backup: " + zip);
                }
                catch (Exception ex)
                {
                    ToolsLog("[ERROR] the backup failed, nothing was deleted: " + ex.Message);
                    return;
                }
            }
            List<string> failed = new List<string>();
            int n = Tools.Delete(files, failed);
            ToolsLog("Deleted " + n + " of " + files.Count + " files.");
            foreach (string f in failed) ToolsLog("[not deleted] " + f);
            RefreshTools();
        }

        void UpdateIndex(bool automatic)
        {
            string cmd = indexCombo.SelectedItem as string;
            if (cmd == null || indexRunning) return;
            indexRunning = true;
            indexButton.Enabled = false;
            ToolsLog("--- Update mission index " + DateTime.Now.ToString("HH:mm:ss") + (automatic ? " (automatic, at start) " : " ") + cmd);
            ThreadPool.QueueUserWorkItem(delegate
            {
                int rc = -1;
                string error = null;
                try { rc = Tools.RunIndex(cmd, delegate (string line) { Ui(delegate { ToolsLog(line); }); }); }
                catch (Exception ex) { error = ex.Message; }
                string file = Tools.IndexFileOf(cmd);
                string fileInfo = "";
                if (file != null)
                {
                    FileInfo fi = new FileInfo(file);
                    fileInfo = "Index file: " + file + " (" + (fi.Length / 1024) + " KB, written " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") + ")";
                }
                Ui(delegate
                {
                    if (error != null) ToolsLog("[ERROR] " + error);
                    else ToolsLog("Finished, exit code " + rc + ".");
                    if (fileInfo.Length > 0) ToolsLog(fileInfo);
                    ToolsLog("In the simulator press Refresh in the mission app to pick up the new index.");
                    indexRunning = false;
                    indexButton.Enabled = indexCombo.Items.Count > 0;
                });
            });
        }

        // ---- updates -----------------------------------------------------------------------------
        // Check for updates (user click only): the installed mission file and the index, the published mission, the published program.
        void CheckUpdates(bool automatic)
        {
            if (updateRunning) return;
            if (automatic)
            {
                // at most one automatic check every 6 hours
                DateTime last;
                if (DateTime.TryParse(Ctx.Settings.LastUpdateCheck, null, System.Globalization.DateTimeStyles.RoundtripKind, out last) && (DateTime.UtcNow - last).TotalHours < 6) return;
            }
            updateRunning = true;
            updateButton.Enabled = false;
            updateInfo.Text = "Checking...";
            Ctx.Settings.LastUpdateCheck = DateTime.UtcNow.ToString("o");
            try { Ctx.Settings.Save(Ctx.SettingsPath); } catch (Exception) { }
            string fallbackUrl = Ctx.Settings.MissionUrl;
            string chosen = missionCombo.SelectedItem is MissionCopy ? ((MissionCopy)missionCombo.SelectedItem).Path : Ctx.Settings.MissionFile;
            string indexCmd = indexCombo.SelectedItem as string;
            if (!automatic) ToolsLog("--- Check for updates " + DateTime.Now.ToString("HH:mm:ss"));
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> lines = new List<string>();
                List<MissionCopy> copies = new List<MissionCopy>();
                MissionCopy target = null;
                RemoteMission rm = null;
                RemoteApp ra = null;
                bool indexKnown = false, indexListsFile = false;
                try
                {
                    copies = Updates.FindInstalled(Tools.CommunityRoots(), chosen);
                    foreach (MissionCopy c in copies) if (chosen.Length > 0 && string.Equals(c.Path, chosen, StringComparison.OrdinalIgnoreCase)) target = c;
                    if (target == null && copies.Count > 0 && copies[0].Official) target = copies[0];
                    lines.Add("R&E mission files found in the Community folders: " + copies.Count);
                    foreach (MissionCopy c in copies) lines.Add("  " + (c == target ? "> " : "  ") + c);
                    if (target != null && indexCmd != null)
                    {
                        string indexFile = Tools.IndexFileOf(indexCmd);
                        if (indexFile != null)
                        {
                            indexKnown = true;
                            List<MissionRelease> rel = Updates.ReadIndexReleases(indexFile);
                            foreach (MissionRelease r in rel) if (r.CompareTo(target.Release) == 0 && r.HasRelease == target.Release.HasRelease) indexListsFile = true;
                            StringBuilder sb = new StringBuilder();
                            foreach (MissionRelease r in rel) { if (sb.Length > 0) sb.Append(", "); sb.Append(r.ToString()); }
                            lines.Add("Mission index " + indexFile + " lists R&E: " + (sb.Length > 0 ? sb.ToString() : "nothing"));
                        }
                    }
                }
                catch (Exception ex) { lines.Add("[ERROR] reading the installed mission: " + ex.Message); }
                try
                {
                    rm = Updates.ReadRemoteMissionAuto(fallbackUrl, delegate (string s) { lines.Add("  " + s); });
                    lines.Add("Published mission: " + rm.Release + " (" + rm.Source + ", " + (rm.Size / 1024) + " KB)");
                }
                catch (Exception ex) { lines.Add("[ERROR] the published mission could not be read: " + ex.Message); }
                try
                {
                    ra = Updates.ReadRemoteApp(Updates.ReleasesApi);
                    lines.Add("Published program: " + ra.Tag + (ra.AssetUrl == null ? " (no setup file in the release)" : "") + ", this one is " + AppInfo.Version);
                }
                catch (Exception ex) { lines.Add("[ERROR] the published program could not be read: " + ex.Message); }

                bool missionNew = rm != null && target != null && rm.Release.CompareTo(target.Release) > 0;
                bool appNew = ra != null && ra.AssetUrl != null && Updates.AppIsNewer(ra);
                if (rm != null && target == null) lines.Add("No R&E mission file was identified: choose it with Browse and check again.");
                if (target != null && indexKnown && !indexListsFile && !missionNew)
                    lines.Add("[NOTICE] the mission index does not list the release of your mission file: press Update mission index.");
                if (!missionNew && !appNew) lines.Add("Nothing to update.");

                Ui(delegate
                {
                    if (automatic)
                    {
                        bool any = missionNew || appNew;
                        if (any)
                        {
                            foreach (string l in lines) ToolsLog(l);
                            string what = (missionNew ? "mission " + rm.Release : "") + (missionNew && appNew ? " and " : "") + (appNew ? "program " + ra.Tag : "");
                            AppendLog("[INFO] Updates: " + what + " available");
                            updateBalloon = true;
                            tray.ShowBalloonTip(10000, AppInfo.Name, "Update available: " + what + ". Click here to install it.", ToolTipIcon.Info);
                        }
                        else AppendLog("[INFO] Updates: nothing newer" + (lines.Exists(delegate (string x) { return x.StartsWith("[ERROR]"); }) ? " (a source could not be read, see the Tools tab: press Check for updates)" : ""));
                        updateRunning = false; updateButton.Enabled = true; updateInfo.Text = "Program version " + AppInfo.Version;
                        return;
                    }
                    foreach (string l in lines) ToolsLog(l);
                    if (copies.Count > 0)
                    {
                        missionCombo.Items.Clear();
                        foreach (MissionCopy c in copies) missionCombo.Items.Add(c);
                        if (target != null) missionCombo.SelectedItem = target;
                    }
                    bool go = false;
                    if (missionNew || appNew)
                    {
                        StringBuilder q = new StringBuilder();
                        if (missionNew) q.AppendLine("Mission: " + rm.Release + " is available (yours: " + (target.Missing ? "none installed" : target.Release.ToString()) + ").");
                        if (appNew) q.AppendLine("Program: " + ra.Tag + " is available (yours: " + AppInfo.Version + ").");
                        q.AppendLine();
                        if (missionNew && target.Missing) q.AppendLine("The mission file does not exist yet: it will be created in " + Path.GetDirectoryName(target.Path) + ".\n");
                        else if (missionNew && !target.Release.HasRelease) q.AppendLine("The mission file to replace is a file without a release number (" + target.Path + ").\n");
                        if (Tools.SimulatorRunning()) q.AppendLine("Microsoft Flight Simulator is running: restart the mission after the update.\n");
                        q.Append("Download and install now?\n\n");
                        if (missionNew) q.Append("The mission file is replaced first (the old one is kept beside it with the extension .json.bak) and the mission index is updated. ");
                        if (appNew) q.Append("Then the program is updated: it closes, the setup installs the new version and starts it again.");
                        StringBuilder notes = new StringBuilder();
                        if (missionNew) notes.AppendLine("WHAT'S NEW IN THE MISSION, " + rm.Release + "\n\n" + (string.IsNullOrEmpty(rm.Notes) ? "No release notes are published for this source." : Updates.PlainNotes(rm.Notes)) + "\n");
                        if (appNew) notes.AppendLine("WHAT'S NEW IN THE PROGRAM, " + ra.Tag + "\n\n" + (string.IsNullOrEmpty(ra.Notes) ? "No release notes are published for this version." : Updates.PlainNotes(ra.Notes)));
                        go = NotesForm.Ask(this, Text + " - update available", q.ToString().TrimEnd(), notes.ToString()) == DialogResult.Yes;
                    }
                    if (!go) { updateRunning = false; updateButton.Enabled = true; updateInfo.Text = "Program version " + AppInfo.Version; return; }
                    InstallUpdates(missionNew ? target : null, missionNew ? rm : null, appNew ? ra : null, indexCmd);
                });
            });
        }

        // the release notes of the mission and of the program as published on GitHub, also when nothing is newer
        void ShowWhatsNew()
        {
            whatsNewButton.Enabled = false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                StringBuilder n = new StringBuilder();
                try
                {
                    string label;
                    string text = Updates.ReadReleaseNotes(Updates.MissionReleasesApi, out label);
                    n.AppendLine("MISSION " + label + "\n\n" + (text.Length > 0 ? text : "The release has no notes.") + "\n");
                }
                catch (Exception ex) { n.AppendLine("MISSION\n\nThe notes of the mission could not be read: " + ex.Message + "\n"); }
                try
                {
                    string label;
                    string text = Updates.ReadReleaseNotes(Updates.ReleasesApi, out label);
                    n.AppendLine("PROGRAM " + label + " (this one is " + AppInfo.Version + ")\n\n" + (text.Length > 0 ? text : "The release has no notes."));
                }
                catch (Exception ex) { n.AppendLine("PROGRAM\n\nThe notes of the program could not be read: " + ex.Message); }
                Ui(delegate
                {
                    whatsNewButton.Enabled = true;
                    NotesForm.View(this, Text + " - what's new", "Release notes of the latest published versions (GitHub).", n.ToString());
                });
            });
        }

        // mission first (download, backup, replace, index), then the program (download, setup, exit)
        void InstallUpdates(MissionCopy target, RemoteMission rm, RemoteApp ra, string indexCmd)
        {
            updateInfo.Text = "Updating...";
            string tmp = Path.Combine(Path.GetTempPath(), "hmc-update");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                string setup = null;
                try
                {
                    Directory.CreateDirectory(tmp);
                    if (target != null && rm != null)
                    {
                        string file = Path.Combine(tmp, "everywhere_all-" + rm.Release.Major + "." + rm.Release.Minor + ".json");
                        long shown = -1;
                        Ui(delegate { ToolsLog("Downloading the mission " + rm.Release + " (" + rm.Source + ")..."); });
                        Updates.DownloadMission(rm, file, delegate (long done, long total)
                        {
                            long step = total > 0 ? done * 4 / total : 0;
                            if (step != shown) { shown = step; Ui(delegate { ToolsLog("  " + (done / 1024) + " of " + (total / 1024) + " KB"); }); }
                        });
                        string backup = Updates.ReplaceInstalled(target.Path, file, null);
                        try { File.Delete(file); } catch (Exception) { }
                        Ui(delegate { ToolsLog((backup == null ? "Mission file created: " : "Mission file replaced: ") + target.Path); if (backup != null) ToolsLog("The previous file is kept beside it: " + backup); });
                        if (indexCmd != null)
                        {
                            Ui(delegate { ToolsLog("Updating the mission index..."); });
                            int rc = Tools.RunIndex(indexCmd, delegate (string line) { Ui(delegate { ToolsLog(line); }); });
                            Ui(delegate { ToolsLog("Index tool finished, exit code " + rc + ". In the simulator press Refresh in the mission app."); });
                        }
                        else Ui(delegate { ToolsLog("[NOTICE] no index tool is known: run Update Mission Index yourself."); });
                    }
                    if (ra != null)
                    {
                        setup = Path.Combine(tmp, "HPGMissionCompanionSetup-" + ra.Tag + ".exe");
                        long shown = -1;
                        Ui(delegate { ToolsLog("Downloading the program " + ra.Tag + "..."); });
                        Updates.DownloadSetup(ra, setup, delegate (long done, long total)
                        {
                            long step = total > 0 ? done * 4 / total : 0;
                            if (step != shown) { shown = step; Ui(delegate { ToolsLog("  " + (done / 1024) + " of " + (total / 1024) + " KB"); }); }
                        });
                    }
                }
                catch (Exception ex) { error = ex.Message; }
                Ui(delegate
                {
                    if (error != null)
                    {
                        ToolsLog("[ERROR] " + error);
                        ToolsLog("The update stopped here: what was already replaced is kept, press Check for updates to go on.");
                        updateRunning = false; updateButton.Enabled = true; updateInfo.Text = "Program version " + AppInfo.Version;
                        return;
                    }
                    if (setup == null)
                    {
                        ToolsLog("Update finished.");
                        updateRunning = false; updateButton.Enabled = true; updateInfo.Text = "Program version " + AppInfo.Version;
                        return;
                    }
                    try
                    {
                        if (dirty) SaveSettings();
                        ToolsLog("Starting the setup: this program closes and starts again.");
                        Process.Start(Updates.SetupStart(setup, Ctx.BaseDir));
                        ExitApp(false);
                    }
                    catch (Exception ex)
                    {
                        ToolsLog("[ERROR] the setup could not be started: " + ex.Message + " (the file is " + setup + ")");
                        updateRunning = false; updateButton.Enabled = true; updateInfo.Text = "Program version " + AppInfo.Version;
                    }
                });
            });
        }

        // ---- about tab ---------------------------------------------------------------------------
        void BuildAboutTab()
        {
            TabPage page = new TabPage("About");
            tabs.TabPages.Add(page);
            PictureBox pic = new PictureBox();
            try { pic.Image = new Icon(appIcon, 64, 64).ToBitmap(); } catch (Exception) { }
            pic.SizeMode = PictureBoxSizeMode.Zoom;
            pic.Location = new Point(14, 14);
            pic.Size = new Size(64, 64);
            page.Controls.Add(pic);
            Label title = Lbl(page, AppInfo.Name, 96, 18, 0, 0);
            title.Font = new Font(Font.FontFamily, 16F, FontStyle.Bold);
            Lbl(page, "Version " + AppInfo.Version, 98, 56, 0, 0);

            string built = "";
            try { built = File.GetLastWriteTime(Application.ExecutablePath).ToString("yyyy-MM-dd HH:mm"); } catch (Exception) { }
            Lbl(page, "Build date:   " + built, 14, 104, 0, 0);
            Lbl(page, "Author:   " + AppInfo.Author, 14, 128, 0, 0);
            Lbl(page, "Discord:", 14, 152, 0, 0);
            LinkLabel invite = new LinkLabel();
            invite.Text = "invitation";
            invite.AutoSize = true;
            invite.Location = new Point(72, 152);
            invite.LinkClicked += delegate { try { System.Diagnostics.Process.Start(AppInfo.DiscordInvite); } catch (Exception) { } };
            page.Controls.Add(invite);
            LinkLabel channel = new LinkLabel();
            channel.Text = "channel of the program";
            channel.AutoSize = true;
            channel.Location = new Point(140, 152);
            channel.LinkClicked += delegate { try { System.Diagnostics.Process.Start(AppInfo.DiscordChannel); } catch (Exception) { } };
            page.Controls.Add(channel);

            Lbl(page, "A companion program for the HPG H145 missions: voices for the mission actors (Windows and Piper voices, volume, radio effect, voice packs), " +
                "the mission work folder and the mission index.", 14, 188, 700, 36);
            Lbl(page, "Credits and licences", 14, 228, 0, 0).Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            Lbl(page, "Text to speech: the voices installed in Windows, and Piper (github.com/rhasspy/piper, MIT licence) with the voices of the rhasspy/piper-voices " +
                "repository. Every Piper voice has its own licence: read voices\\piper\\<voice>.MODEL_CARD.txt before using a voice outside your own PC. " +
                "Piper uses eSpeak NG (GPL) for the phonemes. The engine and the voices are downloaded from their sources when you press Install; they are not part of this program.",
                14, 250, 700, 100);

            Lbl(page, "Script developer guide (tags, voice packs, the say tag, versions, testing)", 14, 358, 0, 0).Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            TextBox guide = new TextBox();
            guide.Multiline = true;
            guide.ReadOnly = true;
            guide.ScrollBars = ScrollBars.Vertical;
            guide.WordWrap = true;
            guide.Font = new Font("Consolas", 8.5F);
            guide.BackColor = Color.White;
            guide.Location = new Point(14, 380);
            guide.Size = new Size(700, 166);
            guide.Text = LoadGuide();
            guide.Select(0, 0);
            page.Controls.Add(guide);
        }

        // the developer guide is compiled into the program (docs\DEVELOPER_GUIDE.md)
        static string LoadGuide()
        {
            try
            {
                using (Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("DEVELOPER_GUIDE.md"))
                {
                    if (s == null) return "(the guide is not in this build; see docs\\DEVELOPER_GUIDE.md in the repository)";
                    using (StreamReader r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd().Replace("\r\n", "\n").Replace("\n", "\r\n");
                }
            }
            catch (Exception ex) { return "(cannot read the guide: " + ex.Message + ")"; }
        }

        // ---- closing -----------------------------------------------------------------------------
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!reallyExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                allowShow = false;
                tray.ShowBalloonTip(2000, AppInfo.Name, "Still running here. Right-click the icon to exit.", ToolTipIcon.Info);
                return;
            }
            base.OnFormClosing(e);
        }

        void ExitApp(bool ask)
        {
            if (ask && dirty)
            {
                DialogResult r = MessageBox.Show("The settings were modified.\n\nSave them before leaving?\n\nYes = save and exit, No = exit without saving, Cancel = stay.", Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.Yes) SaveSettings();
            }
            reallyExit = true;
            tick.Stop();
            SimLink.Stop();
            StopServer();
            PiperEngine.StopAll();
            tray.Visible = false;
            tray.Dispose();
            Application.Exit();
        }
    }
}
