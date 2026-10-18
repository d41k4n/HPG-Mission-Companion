// HPG Mission Companion - the window "World Database Update": one tab for each thing that can be added to or removed from the world data (hangar, hospital,
// airport, apron, hospital helipad, helipad, a point to remove). Each tab has only the fields it needs; before anything is saved the texts are checked for characters
// that are not plain ASCII (the logic is in WorldDb.cs). What is saved goes to the user's own files and is merged with the shared data by the Tools tab.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace HPGMissionCompanion
{
    // checks from the command line: the logic without a window, and a picture of every tab
    internal static class WorldDbSelfTest
    {
        public static int Run(string report)
        {
            StringBuilder sb = new StringBuilder();
            int failed = 0;
            Action<string, bool> t = delegate (string name, bool ok) { sb.AppendLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failed++; };
            try
            {
                if (System.IO.Directory.Exists(SharedData.StateDir)) System.IO.Directory.Delete(SharedData.StateDir, true);
                // the unicode check
                t("plain ASCII is accepted", WorldDb.Check("Name", "REGA 3 (LSZB Bern-Belp), A/B: 1+2 'x' \"y\"").Count == 0);
                List<WorldDb.BadChar> u = WorldDb.Check("Name", "D\u00FCbendorf");
                t("u with a diaeresis is named, position 2, can be written u", u.Count == 1 && u[0].Position == 2 && u[0].CodePoint == 0xFC && u[0].Suggest == "u" && u[0].Describe().IndexOf("U+00FC", StringComparison.Ordinal) > 0);
                t("Replace gives plain letters (Zuerich style names, dashes, quotes)", WorldDb.Replace("D\u00FCbendorf \u2013 Z\u00FCrich \u2018x\u2019 \u0141\u00F3d\u017A \u00DF") == "Dubendorf - Zurich 'x' Lodz ss");
                List<WorldDb.BadChar> cyr = WorldDb.Check("Name", "\u0417\u0438\u043C\u043D\u0438\u0446\u0430");
                t("Cyrillic is named and has no plain letter", cyr.Count == 7 && cyr[0].Suggest.Length == 0 && cyr[0].Describe().IndexOf("no plain letter", StringComparison.Ordinal) > 0);
                List<WorldDb.BadChar> emoji = WorldDb.Check("Name", "A\uD83D\uDE00B");
                t("an emoji (surrogate pair) is one character, U+1F600, position 2", emoji.Count == 1 && emoji[0].CodePoint == 0x1F600 && emoji[0].Position == 2 && emoji[0].Suggest.Length == 0);
                t("a tab is a control character", WorldDb.Check("Name", "a\tb").Count == 1 && WorldDb.Check("Name", "a\tb")[0].Describe().IndexOf("control character", StringComparison.Ordinal) >= 0);
                // numbers
                string la, lo;
                t("a pair pasted in one box is split", WorldDb.SplitPair("46.9096, 7.5049", out la, out lo) && la == "46.9096" && lo == "7.5049");
                t("a decimal comma is a number", WorldDb.SplitPair("46,9096;7,5049", out la, out lo) && la == "46,9096");
                double v;
                t("a decimal comma is read", WorldDb.ParseNumber("46,9096", out v) && Math.Abs(v - 46.9096) < 1e-9);
                // the records
                string err;
                Dictionary<string, object> h = WorldDb.BuildWaypoint(true, "TEST HANGAR", "me", "46.909643", "7.504900", "520", true, false, new string[] { "46.90961", "46.90958", "" }, new string[] { "7.50482", "7.50476", "" }, false, "", "", out err);
                t("hangar record built (2 points, no marshal)", h != null && (int)h["WPtot"] == 2 && h["WP3lat"] == null && (string)h["technician_deboarding"] == "no" && (int)h["max_rng_m"] == 520 && (string)h["marshal_present"] == "no");
                t("a gap in the points is refused", WorldDb.BuildWaypoint(true, "X", "", "46", "7", "520", true, true, new string[] { "", "46.1", "" }, new string[] { "", "7.1", "" }, false, "", "", out err) == null && err.IndexOf("in order", StringComparison.Ordinal) > 0);
                t("a latitude out of range is refused", WorldDb.BuildWaypoint(true, "X", "", "146", "7", "520", true, true, new string[] { "", "", "" }, new string[] { "", "", "" }, false, "", "", out err) == null);
                t("a marshal needs a position", WorldDb.BuildWaypoint(false, "H", "", "46", "7", "", false, false, new string[] { "", "", "" }, new string[] { "", "", "" }, true, "", "", out err) == null);
                Dictionary<string, object> o = WorldDb.BuildWaypoint(false, "TEST HOSPITAL", "", "46.97", "7.45", "", false, false, new string[] { "46.971", "", "" }, new string[] { "7.451", "", "" }, true, "46.972", "7.452", out err);
                t("hospital record built with a marshal", o != null && o.ContainsKey("WPdoorlat") && (int)o["WPtot"] == 1 && (string)o["marshal_present"] == "yes" && SharedData.CheckWaypoint(o, false) == null);
                List<object> ap = WorldDb.BuildDbRecord("airport", "10.5", "20.5", "Test Airfield", "zzzz", "zz", "", out err);
                t("airport record: capitals, five values", ap != null && ap.Count == 5 && (string)ap[3] == "ZZZZ" && (string)ap[4] == "ZZ");
                t("an ICAO of five letters is refused", WorldDb.BuildDbRecord("airport", "1", "2", "A", "ABCDE", "", "", out err) == null);
                List<object> hos = WorldDb.BuildDbRecord("H_hospital", "45.5", "9.1", "Test Hospital", "", "", "PAD X", out err);
                t("hospital helipad: [lat, lon, pad, name]", hos != null && hos.Count == 4 && (string)hos[2] == "PAD X" && (string)hos[3] == "Test Hospital");
                t("a hospital needs a name or a pad", WorldDb.BuildDbRecord("H_hospital", "45.5", "9.1", "", "", "", "", out err) == null);
                List<object> apr = WorldDb.BuildDbRecord("apron", "45.5", "9.1", "", "", "", "", out err);
                t("an apron may have no name", apr != null && apr.Count == 3 && (string)apr[2] == "");
                Dictionary<string, object> rule = WorldDb.BuildRemoval("43.43", "-5.83", "", "x", out err);
                t("removal: radius 30 when empty", rule != null && (int)rule["radius_m"] == 30 && (string)rule["name"] == "x");
                // the user's files
                WorldDb.SaveWaypoint("hangar_wps", h);
                Dictionary<string, object> h2 = WorldDb.BuildWaypoint(true, "test hangar", "", "46.5", "7.5", "300", true, true, new string[] { "", "", "" }, new string[] { "", "", "" }, false, "", "", out err);
                WorldDb.SaveWaypoint("hangar_wps", h2);
                t("the same name (any case) replaces the record", WorldDb.ListWaypoints("hangar_wps").Count == 1);
                WorldDb.SaveWaypoint("hospitals_wps", o);
                t("a hospital is kept apart", WorldDb.ListWaypoints("hospitals_wps").Count == 1 && WorldDb.ListWaypoints("hangar_wps").Count == 1);
                t("a record twice is added once", WorldDb.SaveDbRecord("airport", ap) && !WorldDb.SaveDbRecord("airport", ap));
                WorldDb.SaveDbRecord("H_hospital", hos);
                WorldDb.SaveDbRemoval("H_civil", rule);
                t("the lists show what was added", WorldDb.ListDb("airport", false).Count == 1 && WorldDb.ListDb("H_hospital", false).Count == 1 && WorldDb.ListDb("H_civil", true).Count == 1);
                // the files read back, with the rules of the shared data
                Dictionary<string, object> wp = Js.Obj(Js.Parse(System.IO.File.ReadAllText(SharedData.LocalWaypointsPath)));
                IList hl = Js.List(wp["hangar_wps"]);
                double spawn = 0;
                t("local-waypoints.json is valid JSON and the record passes the checks", hl.Count == 1 && SharedData.CheckWaypoint(Js.Obj(hl[0]), true) == null && Js.Num(Js.Obj(hl[0])["WPspawnlat"], out spawn) && spawn == 46.5);
                Dictionary<string, object> db = Js.Obj(Js.Parse(System.IO.File.ReadAllText(SharedData.LocalDbPath)));
                t("local-db.json holds the three kinds", Js.Obj(db["airport"]) != null && Js.Obj(db["H_hospital"]) != null && Js.List(Js.Obj(db["H_civil"])["remove"]).Count == 1);
                sb.AppendLine("--- local-waypoints.json"); sb.AppendLine(System.IO.File.ReadAllText(SharedData.LocalWaypointsPath));
                sb.AppendLine("--- local-db.json"); sb.AppendLine(System.IO.File.ReadAllText(SharedData.LocalDbPath));
                WorldDb.RemoveWaypoint("hangar_wps", "test hangar");
                WorldDb.RemoveDbLine("airport", false, 0);
                t("the entries can be removed", WorldDb.ListWaypoints("hangar_wps").Count == 0 && WorldDb.ListDb("airport", false).Count == 0);
            }
            catch (Exception ex) { sb.AppendLine("ERROR: " + ex); failed++; }
            sb.AppendLine(failed == 0 ? "ALL PASS" : failed + " FAILED");
            System.IO.File.WriteAllText(report, sb.ToString());
            return failed == 0 ? 0 : 1;
        }

        public static int Shot(string basePath)
        {
            using (WorldDbForm f = new WorldDbForm())
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(40, 40);
                f.Show();
                for (int i = 0; i < 6; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(80); }
                TabControl tc = null;
                foreach (Control c in f.Controls) if (c is TabControl) tc = (TabControl)c;
                for (int i = 0; tc != null && i < tc.TabCount; i++)
                {
                    tc.SelectedIndex = i;
                    for (int k = 0; k < 4; k++) { Application.DoEvents(); System.Threading.Thread.Sleep(60); }
                    using (Bitmap bmp = new Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                        bmp.Save(basePath + "-" + i + ".png", System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                f.Close();
            }
            return 0;
        }
    }

    internal sealed class WorldDbForm : Form
    {
        readonly TabControl tabs = new TabControl();
        readonly Label status = new Label();
        public bool WaypointsChanged;
        public bool DatabasesChanged;

        public WorldDbForm()
        {
            Text = "World Database Update";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(740, 610);
            Font = new Font("Segoe UI", 9F);

            tabs.Location = new Point(10, 10);
            tabs.Size = new Size(720, 545);
            Controls.Add(tabs);
            BuildWaypointTab(true);
            BuildWaypointTab(false);
            BuildDbTab("Airport", new string[] { "airport", "airport-nocode", "airport-mil" }, new string[] { "Primary airport", "Secondary airport (no code)", "Military airport" }, "Name of the airport", true, false);
            BuildDbTab("Apron", new string[] { "apron" }, null, "Name of the apron (may be empty)", false, false);
            BuildDbTab("Hospital helipad", new string[] { "H_hospital" }, null, "Name of the hospital", false, true);
            BuildDbTab("Helipad", new string[] { "H_civil" }, null, "Name of the helipad (may be empty)", false, false);
            BuildRemoveTab();

            status.Location = new Point(12, 566);
            status.Size = new Size(610, 36);
            status.ForeColor = SystemColors.GrayText;
            status.Text = "Your entries are kept in your own files. Press Update now (waypoints) or Apply now (databases) in the Tools tab when you are done.";
            Controls.Add(status);
            Button close = new Button();
            close.Text = "Close";
            close.Location = new Point(640, 566);
            close.Size = new Size(90, 30);
            close.Click += delegate { Close(); };
            Controls.Add(close);
            CancelButton = close;
        }

        // ---- helpers -----------------------------------------------------------------------------------------
        static Label Cap(Control parent, string text, int x, int y, int w)
        {
            Label l = new Label();
            l.Text = text;
            l.Location = new Point(x, y);
            l.Size = new Size(w, 18);
            parent.Controls.Add(l);
            return l;
        }

        static TextBox Box(Control parent, int x, int y, int w, string text)
        {
            TextBox t = new TextBox();
            t.Location = new Point(x, y);
            t.Size = new Size(w, 24);
            t.Text = text;
            parent.Controls.Add(t);
            return t;
        }

        // a latitude and a longitude; "46.9, 7.5" pasted in the first one fills both
        static void LatLon(Control parent, int x, int y, string caption, out TextBox lat, out TextBox lon)
        {
            Cap(parent, caption, x, y, 650);
            Cap(parent, "Latitude", x, y + 18, 120);
            Cap(parent, "Longitude", x + 150, y + 18, 120);
            TextBox a = Box(parent, x, y + 36, 140, ""), b = Box(parent, x + 150, y + 36, 140, "");
            a.TextChanged += delegate
            {
                string la, lo;
                if (WorldDb.SplitPair(a.Text, out la, out lo)) { b.Text = lo; a.Text = la; }
            };
            lat = a; lon = b;
        }

        static string Plain(TextBox t) { return t.Text.Trim(); }

        // every text of a record is checked before it is saved: a character that is not plain ASCII is named; the ones that have a plain letter can be replaced at once
        bool AsciiOk(List<KeyValuePair<string, TextBox>> fields)
        {
            List<WorldDb.BadChar> bad = new List<WorldDb.BadChar>();
            foreach (KeyValuePair<string, TextBox> f in fields) bad.AddRange(WorldDb.Check(f.Key, f.Value.Text));
            if (bad.Count == 0) return true;
            StringBuilder sb = new StringBuilder("These characters are not allowed (the mission and the tablet of HPG show plain ASCII only):\n\n");
            bool allFixable = true;
            for (int i = 0; i < bad.Count && i < 12; i++) sb.AppendLine(bad[i].Describe());
            if (bad.Count > 12) sb.AppendLine("... and " + (bad.Count - 12) + " more");
            foreach (WorldDb.BadChar b in bad) if (b.Suggest.Length == 0) allFixable = false;
            if (allFixable)
            {
                sb.AppendLine().Append("Replace them with the plain letters and go on?");
                if (MessageBox.Show(this, sb.ToString(), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return false;
                foreach (KeyValuePair<string, TextBox> f in fields) f.Value.Text = WorldDb.Replace(f.Value.Text);
                return true;
            }
            MessageBox.Show(this, sb.ToString(), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        void Fail(string message) { MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }

        void Saved(string what, bool waypoints)
        {
            if (waypoints) WaypointsChanged = true; else DatabasesChanged = true;
            status.ForeColor = Color.FromArgb(0, 110, 40);
            status.Text = what + " saved in your list. Press " + (waypoints ? "Update now" : "Apply now") + " in the Tools tab when you are done.";
        }

        // ---- hangar and hospital ----------------------------------------------------------------------------------
        void BuildWaypointTab(bool hangar)
        {
            TabPage page = new TabPage(hangar ? "Hangar" : "Hospital");
            tabs.TabPages.Add(page);
            string key = hangar ? "hangar_wps" : "hospitals_wps";
            Cap(page, hangar ? "Name of the hangar (for example the base and the airport)" : "Name of the hospital", 10, 8, 380);
            TextBox name = Box(page, 10, 26, 380, "");
            Cap(page, "Your name (optional)", 410, 8, 200);
            TextBox by = Box(page, 410, 26, 200, "");
            TextBox lat, lon;
            LatLon(page, 10, 62, hangar ? "Hangar door (where the technician appears)" : "Hospital door", out lat, out lon);
            TextBox range = null;
            CheckBox boarding = null, deboarding = null;
            int y = 136;
            if (hangar)
            {
                Cap(page, "Range (m) around the door", 10, y, 160);
                range = Box(page, 10, y + 18, 80, "520");
                boarding = new CheckBox(); boarding.Text = "Technician at boarding"; boarding.Checked = true; boarding.AutoSize = true; boarding.Location = new Point(130, y + 20); page.Controls.Add(boarding);
                deboarding = new CheckBox(); deboarding.Text = "Technician at deboarding"; deboarding.Checked = true; deboarding.AutoSize = true; deboarding.Location = new Point(320, y + 20); page.Controls.Add(deboarding);
                y += 56;
            }
            Cap(page, hangar ? "Path from the hangar door to the helicopter, in order (up to 3 points, leave the others empty)" : "Path from the helicopter to the hospital door, in order (up to 3 points; point 1 is where the ground staff wait)", 10, y, 700);
            TextBox[] pl = new TextBox[3], pn = new TextBox[3];
            for (int k = 0; k < 3; k++)
            {
                Cap(page, "Point " + (k + 1), 10, y + 24 + k * 30 + 3, 60);
                pl[k] = Box(page, 70, y + 22 + k * 30, 140, "");
                pn[k] = Box(page, 220, y + 22 + k * 30, 140, "");
                TextBox a = pl[k], b = pn[k];
                a.TextChanged += delegate { string la, lo; if (WorldDb.SplitPair(a.Text, out la, out lo)) { b.Text = lo; a.Text = la; } };
            }
            y += 22 + 3 * 30 + 6;
            CheckBox marshal = new CheckBox(); marshal.Text = "Marshal at this place"; marshal.AutoSize = true; marshal.Location = new Point(10, y + 4); page.Controls.Add(marshal);
            TextBox ml = Box(page, 190, y, 140, ""), mn = Box(page, 340, y, 140, "");
            ml.Enabled = false; mn.Enabled = false;
            marshal.CheckedChanged += delegate { ml.Enabled = marshal.Checked; mn.Enabled = marshal.Checked; };
            ml.TextChanged += delegate { string la, lo; if (WorldDb.SplitPair(ml.Text, out la, out lo)) { mn.Text = lo; ml.Text = la; } };
            y += 40;

            ListBox list = new ListBox();
            list.Location = new Point(10, y + 40);
            list.Size = new Size(580, 90);
            page.Controls.Add(list);
            Action refresh = delegate { list.Items.Clear(); foreach (string s in WorldDb.ListWaypoints(key)) list.Items.Add(s); };
            Cap(page, "Your " + (hangar ? "hangars" : "hospitals") + " (a new one with the same name replaces it)", 10, y + 20, 500);
            Button remove = new Button(); remove.Text = "Remove selected"; remove.Location = new Point(596, y + 40); remove.Size = new Size(112, 30); page.Controls.Add(remove);
            remove.Click += delegate
            {
                string id = list.SelectedItem as string;
                if (id == null) return;
                try { WorldDb.RemoveWaypoint(key, id); WaypointsChanged = true; refresh(); }
                catch (Exception ex) { Fail(ex.Message); }
            };
            Button add = new Button(); add.Text = "Add to my list"; add.Location = new Point(10, y - 4); add.Size = new Size(140, 28); page.Controls.Add(add);
            add.Click += delegate
            {
                List<KeyValuePair<string, TextBox>> fields = new List<KeyValuePair<string, TextBox>>();
                fields.Add(new KeyValuePair<string, TextBox>("Name", name));
                fields.Add(new KeyValuePair<string, TextBox>("Your name", by));
                if (!AsciiOk(fields)) return;
                string error;
                Dictionary<string, object> rec = WorldDb.BuildWaypoint(hangar, Plain(name), Plain(by), Plain(lat), Plain(lon), range == null ? "" : Plain(range),
                    boarding != null && boarding.Checked, deboarding != null && deboarding.Checked,
                    new string[] { Plain(pl[0]), Plain(pl[1]), Plain(pl[2]) }, new string[] { Plain(pn[0]), Plain(pn[1]), Plain(pn[2]) },
                    marshal.Checked, Plain(ml), Plain(mn), out error);
                if (rec == null) { Fail(error); return; }
                try { WorldDb.SaveWaypoint(key, rec); }
                catch (Exception ex) { Fail(ex.Message); return; }
                Saved((hangar ? "Hangar \"" : "Hospital \"") + Plain(name) + "\"", true);
                refresh();
            };
            refresh();
        }

        // ---- the databases ---------------------------------------------------------------------------------------------
        void BuildDbTab(string title, string[] types, string[] typeTitles, string nameCaption, bool airport, bool hospital)
        {
            TabPage page = new TabPage(title);
            tabs.TabPages.Add(page);
            ComboBox kind = null;
            if (typeTitles != null)
            {
                Cap(page, "Kind", 410, 8, 200);
                kind = new ComboBox(); kind.DropDownStyle = ComboBoxStyle.DropDownList; kind.Location = new Point(410, 26); kind.Size = new Size(240, 24);
                foreach (string s in typeTitles) kind.Items.Add(s);
                kind.SelectedIndex = 0;
                page.Controls.Add(kind);
            }
            Cap(page, nameCaption, 10, 8, 380);
            TextBox name = Box(page, 10, 26, 380, "");
            TextBox icao = null, iata = null, pad = null;
            int y = 62;
            if (airport)
            {
                Cap(page, "ICAO code (optional)", 10, y, 150);
                icao = Box(page, 10, y + 18, 100, "");
                Cap(page, "IATA code (optional)", 170, y, 150);
                iata = Box(page, 170, y + 18, 100, "");
                y += 56;
            }
            if (hospital)
            {
                Cap(page, "Name of the helipad (optional)", 410, 8, 250);
                pad = Box(page, 410, 26, 240, "");
            }
            TextBox lat, lon;
            LatLon(page, 10, y, "Position", out lat, out lon);
            y += 80;
            ListBox list = new ListBox();
            list.Location = new Point(10, y + 50);
            list.Size = new Size(580, 150);
            page.Controls.Add(list);
            Func<string> currentType = delegate { return kind == null ? types[0] : types[Math.Max(0, kind.SelectedIndex)]; };
            Action refresh = delegate { list.Items.Clear(); foreach (string s in WorldDb.ListDb(currentType(), false)) list.Items.Add(s); };
            if (kind != null) kind.SelectedIndexChanged += delegate { refresh(); };
            Cap(page, "Your additions of this kind (latitude, longitude, then the texts)", 10, y + 30, 500);
            Button remove = new Button(); remove.Text = "Remove selected"; remove.Location = new Point(596, y + 50); remove.Size = new Size(112, 30); page.Controls.Add(remove);
            remove.Click += delegate
            {
                if (list.SelectedIndex < 0) return;
                try { WorldDb.RemoveDbLine(currentType(), false, list.SelectedIndex); DatabasesChanged = true; refresh(); }
                catch (Exception ex) { Fail(ex.Message); }
            };
            Button add = new Button(); add.Text = "Add to my list"; add.Location = new Point(10, y - 2); add.Size = new Size(140, 28); page.Controls.Add(add);
            add.Click += delegate
            {
                List<KeyValuePair<string, TextBox>> fields = new List<KeyValuePair<string, TextBox>>();
                fields.Add(new KeyValuePair<string, TextBox>("Name", name));
                if (icao != null) fields.Add(new KeyValuePair<string, TextBox>("ICAO", icao));
                if (iata != null) fields.Add(new KeyValuePair<string, TextBox>("IATA", iata));
                if (pad != null) fields.Add(new KeyValuePair<string, TextBox>("Helipad", pad));
                if (!AsciiOk(fields)) return;
                string error;
                List<object> rec = WorldDb.BuildDbRecord(currentType(), Plain(lat), Plain(lon), Plain(name), icao == null ? "" : Plain(icao), iata == null ? "" : Plain(iata), pad == null ? "" : Plain(pad), out error);
                if (rec == null) { Fail(error); return; }
                bool added;
                try { added = WorldDb.SaveDbRecord(currentType(), rec); }
                catch (Exception ex) { Fail(ex.Message); return; }
                if (!added) { Fail("This record is already in your list."); return; }
                Saved(title + " \"" + Plain(name) + "\"", false);
                refresh();
            };
            refresh();
        }

        void BuildRemoveTab()
        {
            TabPage page = new TabPage("Remove a point");
            tabs.TabPages.Add(page);
            string[] types = new string[] { "airport", "airport-nocode", "airport-mil", "apron", "H_civil", "H_hospital", "runway" };
            string[] titles = new string[] { "Primary airport", "Secondary airport (no code)", "Military airport", "Apron", "Helipad", "Hospital helipad", "Runway" };
            Cap(page, "Remove from this list of the tablet", 10, 8, 300);
            ComboBox kind = new ComboBox(); kind.DropDownStyle = ComboBoxStyle.DropDownList; kind.Location = new Point(10, 26); kind.Size = new Size(260, 24);
            foreach (string s in titles) kind.Items.Add(s);
            kind.SelectedIndex = 0;
            page.Controls.Add(kind);
            TextBox lat, lon;
            LatLon(page, 10, 62, "Position of the point", out lat, out lon);
            Cap(page, "Radius (m)", 330, 80, 100);
            TextBox radius = Box(page, 330, 98, 100, "");
            Cap(page, "Only if the name is (optional)", 450, 80, 250);
            TextBox name = Box(page, 450, 98, 250, "");
            Label help = Cap(page, "Every point of that list within the radius of this position is removed (30 m when the radius is empty; with a name, only the points with that name).", 10, 142, 700);
            help.ForeColor = SystemColors.GrayText;
            ListBox list = new ListBox();
            list.Location = new Point(10, 250);
            list.Size = new Size(580, 150);
            page.Controls.Add(list);
            Action refresh = delegate { list.Items.Clear(); foreach (string s in WorldDb.ListDb(types[Math.Max(0, kind.SelectedIndex)], true)) list.Items.Add(s); };
            kind.SelectedIndexChanged += delegate { refresh(); };
            Cap(page, "Your removals of this list", 10, 230, 400);
            Button remove = new Button(); remove.Text = "Remove selected"; remove.Location = new Point(596, 250); remove.Size = new Size(112, 30); page.Controls.Add(remove);
            remove.Click += delegate
            {
                if (list.SelectedIndex < 0) return;
                try { WorldDb.RemoveDbLine(types[Math.Max(0, kind.SelectedIndex)], true, list.SelectedIndex); DatabasesChanged = true; refresh(); }
                catch (Exception ex) { Fail(ex.Message); }
            };
            Button add = new Button(); add.Text = "Add to my list"; add.Location = new Point(10, 180); add.Size = new Size(140, 28); page.Controls.Add(add);
            add.Click += delegate
            {
                List<KeyValuePair<string, TextBox>> fields = new List<KeyValuePair<string, TextBox>>();
                fields.Add(new KeyValuePair<string, TextBox>("Name", name));
                if (!AsciiOk(fields)) return;
                string error;
                Dictionary<string, object> rule = WorldDb.BuildRemoval(Plain(lat), Plain(lon), Plain(radius), Plain(name), out error);
                if (rule == null) { Fail(error); return; }
                bool added;
                try { added = WorldDb.SaveDbRemoval(types[Math.Max(0, kind.SelectedIndex)], rule); }
                catch (Exception ex) { Fail(ex.Message); return; }
                if (!added) { Fail("This removal is already in your list."); return; }
                Saved("The removal", false);
                refresh();
            };
            refresh();
        }
    }
}
