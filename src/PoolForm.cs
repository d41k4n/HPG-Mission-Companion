using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace HPGMissionCompanion
{
    // The helpers of the voices window of a random actor (the user's request of 2026-10-17): who uses a voice, the sort of the list, and the clipboard text that
    // copies the choices of one random actor into another.
    internal static class VoicePool
    {
        public const string ClipHeader = "HPGMC-RANDOM-POOL 1";

        // voice id -> "used for Pilot, Copilot": the voices that the actors with a fixed voice (not drawn) have in this language, as the Actors tab shows them
        // (an actor with no voice chosen yet shows the voice at its own position in the list)
        public static Dictionary<string, string> UsedMap(string langCode, List<VoiceInfo> fam)
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            if (fam.Count == 0) return map;
            List<VoiceInfo> auto = fam.FindAll(delegate (VoiceInfo x) { return !VoiceLibrary.IsOnline(x); });       // the voices the program takes by itself
            for (int k = 0; k < Actors.Count; k++)
            {
                ActorSetting a = Ctx.Settings.Get(langCode, k + 1);
                if (a.Random && Actors.CanDraw(k + 1)) continue;
                VoiceInfo v = VoiceLibrary.ById(fam, a.Voice);
                if (v == null && auto.Count > 0) v = auto[k % auto.Count];
                if (v == null) continue;
                string who = Actors.Names[k];
                string cur;
                map[v.Id] = map.TryGetValue(v.Id, out cur) ? cur + ", " + who : who;
            }
            Dictionary<string, string> text = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> kv in map) text[kv.Key] = "used for " + kv.Value;
            return text;
        }

        public static string SexText(VoiceInfo v) { return string.IsNullOrEmpty(v.Sex) ? "unknown" : v.Sex; }
        public static string QualityText(VoiceInfo v) { return string.IsNullOrEmpty(v.QualityText) ? "" : v.QualityText; }

        // column 0 name, 1 sex (female, male, unknown), 2 quality (low to high), 3 used (used first); ties by name
        public static int Compare(VoiceInfo a, VoiceInfo b, int column, bool ascending, Dictionary<string, string> used)
        {
            int r = 0;
            switch (column)
            {
                case 1: r = string.Compare(SexText(a), SexText(b), StringComparison.OrdinalIgnoreCase); break;
                case 2: r = a.Rank - b.Rank; break;
                case 3: r = (used.ContainsKey(a.Id) ? 0 : 1) - (used.ContainsKey(b.Id) ? 0 : 1); break;
            }
            if (r != 0) return ascending ? r : -r;
            r = string.Compare(a.Short, b.Short, StringComparison.OrdinalIgnoreCase);
            if (r != 0) return column == 0 && !ascending ? -r : r;
            return string.Compare(a.Id, b.Id, StringComparison.Ordinal);
        }

        // the text of "Copy random to the clipboard": a header, the language and one voice for each line (id, tab, name)
        public static string ClipText(string langCode, List<VoiceInfo> voices)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(ClipHeader).Append("\r\n");
            sb.Append("lang=").Append(langCode).Append("\r\n");
            foreach (VoiceInfo v in voices) sb.Append(v.Id).Append('\t').Append(v.Short).Append("\r\n");
            return sb.ToString();
        }

        // the voice ids of a clipboard text made by ClipText; null when the text is not one
        public static List<string> ParseClip(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 0 || lines[0].Trim() != ClipHeader) return null;
            List<string> ids = new List<string>();
            for (int i = 1; i < lines.Length; i++)
            {
                string l = lines[i].Trim();
                if (l.Length == 0 || l.StartsWith("lang=", StringComparison.Ordinal)) continue;
                int tab = l.IndexOf('\t');
                string id = tab >= 0 ? l.Substring(0, tab).Trim() : l;
                if (id.Length > 0) ids.Add(id);
            }
            return ids;
        }
    }

    // The voices a random actor may take: a list with a check box for each voice of the language, its sex and quality, where it is already used, sortable on every
    // column, with copy and paste of the choices through the clipboard. Nothing checked or everything checked = every voice.
    internal sealed class PoolForm : Form
    {
        readonly string langCode;
        readonly List<VoiceInfo> voices;
        readonly Dictionary<string, string> used;
        readonly HashSet<string> chk = new HashSet<string>();
        readonly ListView list = new ListView();
        readonly Label info = new Label();
        readonly Button copyButton = new Button();
        readonly Button pasteButton = new Button();
        readonly Button allButton = new Button();
        readonly Button noneButton = new Button();
        readonly Action<VoiceInfo> testVoice;
        static readonly string[] Titles = { "Voice", "Sex", "Quality", "Used" };
        int sortColumn;
        bool sortAscending = true;
        bool filling;

        public PoolForm(string title, string langCode, List<VoiceInfo> voices, List<string> pool, Dictionary<string, string> used, Action<VoiceInfo> testVoice)
        {
            this.langCode = langCode; this.voices = voices; this.used = used; this.testVoice = testVoice;
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(680, 480);
            Label hint = new Label();
            hint.Text = "The voice is drawn among the checked voices at every new shift (dispatcher) or dispatch (ground unit, heli rescuer). The actors that are drawn never get the same voice. " +
                "Select a voice and press Test to hear it. Click a column title to sort. Used shows the voices that other actors have chosen in the Actors tab.";
            hint.Location = new Point(10, 8); hint.Size = new Size(660, 52);
            Controls.Add(hint);
            list.View = View.Details;
            list.CheckBoxes = true;
            list.FullRowSelect = true;
            list.MultiSelect = false;
            list.HideSelection = false;
            list.Location = new Point(10, 64); list.Size = new Size(660, 330);
            list.Columns.Add("Voice", 290);
            list.Columns.Add("Sex", 70);
            list.Columns.Add("Quality", 80);
            list.Columns.Add("Used", 200);
            list.ColumnClick += delegate (object s, ColumnClickEventArgs e) { SortBy(e.Column); };
            list.ItemChecked += delegate (object s, ItemCheckedEventArgs e)
            {
                if (filling) return;
                VoiceInfo v = e.Item.Tag as VoiceInfo;
                if (v == null) return;
                if (e.Item.Checked) chk.Add(v.Id); else chk.Remove(v.Id);
            };
            Controls.Add(list);
            info.Location = new Point(10, 398); info.Size = new Size(660, 34);
            info.ForeColor = SystemColors.GrayText;
            Controls.Add(info);

            foreach (VoiceInfo v in voices) if (pool == null || pool.Count == 0 || pool.Contains(v.Id)) chk.Add(v.Id);

            Place(allButton, "All", 10, 440, 70); allButton.Click += delegate { CheckAll(); };
            Place(noneButton, "None", 86, 440, 70); noneButton.Click += delegate { CheckNone(); };
            Button test = new Button(); Place(test, "Test", 162, 440, 60);
            test.Click += delegate
            {
                VoiceInfo v = null;
                if (list.SelectedItems.Count > 0) v = list.SelectedItems[0].Tag as VoiceInfo;
                if (v == null) foreach (VoiceInfo c in this.voices) if (chk.Contains(c.Id)) { v = c; break; }
                if (v != null && this.testVoice != null) this.testVoice(v);
            };
            Place(copyButton, "Copy random to clipboard", 228, 440, 150); copyButton.Click += delegate { CopyToClipboard(); };
            Place(pasteButton, "Paste random from clipboard", 384, 440, 170); pasteButton.Click += delegate { PasteFromClipboard(); };
            Button ok = new Button(); Place(ok, "OK", 566, 440, 50); ok.DialogResult = DialogResult.OK;
            Button cancel = new Button(); Place(cancel, "Cancel", 620, 440, 50); cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok; CancelButton = cancel;
            Fill();
        }

        void Place(Button b, string text, int x, int y, int w)
        {
            b.Text = text; b.Location = new Point(x, y); b.Size = new Size(w, 30);
            Controls.Add(b);
        }

        public Button CopyButton { get { return copyButton; } }
        public Button PasteButton { get { return pasteButton; } }
        public Button NoneButton { get { return noneButton; } }
        public string InfoText { get { return info.Text; } }

        // the ids checked, in the order of the list given to the window
        public List<string> CheckedIds()
        {
            List<string> ids = new List<string>();
            foreach (VoiceInfo v in voices) if (chk.Contains(v.Id)) ids.Add(v.Id);
            return ids;
        }

        public void SetChecked(string id, bool on) { if (on) chk.Add(id); else chk.Remove(id); Fill(); }

        // the voices as the list shows them now
        public List<string> Order()
        {
            List<string> names = new List<string>();
            foreach (ListViewItem it in list.Items) names.Add(((VoiceInfo)it.Tag).Short);
            return names;
        }

        public void SortBy(int column)
        {
            if (column == sortColumn) sortAscending = !sortAscending;
            else { sortColumn = column; sortAscending = true; }
            Fill();
        }

        void Fill()
        {
            string keep = list.SelectedItems.Count > 0 ? ((VoiceInfo)list.SelectedItems[0].Tag).Id : null;
            filling = true;
            for (int i = 0; i < Titles.Length; i++) list.Columns[i].Text = Titles[i] + (i == sortColumn ? (sortAscending ? "  ^" : "  v") : "");
            list.Items.Clear();
            List<VoiceInfo> rows = new List<VoiceInfo>(voices);
            rows.Sort(delegate (VoiceInfo a, VoiceInfo b) { return VoicePool.Compare(a, b, sortColumn, sortAscending, used); });
            foreach (VoiceInfo v in rows)
            {
                ListViewItem item = new ListViewItem(v.Short);
                item.Tag = v;
                item.SubItems.Add(VoicePool.SexText(v));
                item.SubItems.Add(VoicePool.QualityText(v));
                string u;
                item.SubItems.Add(used.TryGetValue(v.Id, out u) ? u : "");
                list.Items.Add(item);
                item.Checked = chk.Contains(v.Id);
                if (keep != null && v.Id == keep) item.Selected = true;
            }
            filling = false;
        }

        public void CheckAll() { foreach (VoiceInfo v in voices) chk.Add(v.Id); Fill(); }
        public void CheckNone() { chk.Clear(); Fill(); }

        public void CopyToClipboard()
        {
            List<VoiceInfo> pick = new List<VoiceInfo>();
            foreach (VoiceInfo v in voices) if (chk.Contains(v.Id)) pick.Add(v);
            bool all = pick.Count == 0;
            if (all) pick = voices;
            try
            {
                Clipboard.SetText(VoicePool.ClipText(langCode, pick));
                info.Text = "Copied " + pick.Count + " voice" + (pick.Count == 1 ? "" : "s") + " to the clipboard" + (all ? " (nothing was checked, which means every voice)." : ".") + " Open the Voices window of another random actor and press Paste.";
            }
            catch (Exception ex) { info.Text = "Cannot use the clipboard: " + ex.Message; }
        }

        public void PasteFromClipboard()
        {
            string text;
            try { text = Clipboard.GetText(); }
            catch (Exception ex) { info.Text = "Cannot use the clipboard: " + ex.Message; return; }
            List<string> ids = VoicePool.ParseClip(text);
            if (ids == null) { info.Text = "The clipboard does not hold a copied random list: nothing changed."; return; }
            HashSet<string> have = new HashSet<string>();
            foreach (VoiceInfo v in voices) have.Add(v.Id);
            HashSet<string> match = new HashSet<string>();
            foreach (string id in ids) if (have.Contains(id)) match.Add(id);
            if (match.Count == 0) { info.Text = "None of the " + ids.Count + " voices of the clipboard is available for this language here: nothing changed."; return; }
            chk.Clear();
            foreach (string id in match) chk.Add(id);
            Fill();
            int missing = ids.Count - match.Count;
            info.Text = "Pasted: " + match.Count + " voice" + (match.Count == 1 ? "" : "s") + " checked" + (missing > 0 ? "; " + missing + " of the clipboard " + (missing == 1 ? "is" : "are") + " not available here (another language or a voice that is not installed)." : ".");
        }
    }

    // --pool-test <report>: the window on a made-up list, driven the way a user drives it (sort, used for, copy and paste through the clipboard)
    internal static class PoolSelfTest
    {
        static VoiceInfo V(string id, string name, string sex, string quality, int rank, string engine)
        {
            VoiceInfo v = new VoiceInfo();
            v.Id = id; v.Family = "en"; v.Engine = engine; v.Sex = sex; v.QualityText = quality; v.Rank = rank;
            v.Short = name + " (" + (engine == "windows" ? "Windows" : "Piper") + ", en)";
            v.Display = v.Short; v.Keys = new string[] { name };
            return v;
        }

        // --pool-shot <png>: a picture of the window on a made-up list, to look at the layout
        public static int Shot(string pngPath)
        {
            List<VoiceInfo> fam = new List<VoiceInfo>();
            fam.Add(V("piper:amy", "Amy", "female", "medium", 3, "piper"));
            fam.Add(V("piper:ryan", "Ryan", "male", "high", 4, "piper"));
            fam.Add(V("piper:alba", "Alba", "female", "medium", 3, "piper"));
            fam.Add(V("win:zira", "Microsoft Zira", "female", "Windows", 5, "windows"));
            fam.Add(V("win:mark", "Microsoft Mark", "male", "Windows", 5, "windows"));
            fam.Add(V("piper:vctk:p225", "vctk p225", "", "medium", 3, "piper"));
            Dictionary<string, string> used = new Dictionary<string, string>();
            used["piper:ryan"] = "used for Pilot, Copilot";
            used["piper:amy"] = "used for Doctor";
            used["win:zira"] = "used for Ground unit";
            using (PoolForm f = new PoolForm("Voices for Dispatcher (English)", "EN", fam, new List<string>(new string[] { "piper:amy", "win:mark", "piper:alba" }), used, null))
            {
                f.SortBy(1);
                f.Show();
                Application.DoEvents();
                using (Bitmap bmp = new Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            return 0;
        }
        public static int Run(string reportPath)
        {
            StringBuilder rep = new StringBuilder();
            int failed = 0;
            Action<string, bool> check = delegate (string what, bool ok) { rep.AppendLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failed++; };
            List<VoiceInfo> fam = new List<VoiceInfo>();
            fam.Add(V("piper:amy", "Amy", "female", "medium", 3, "piper"));
            fam.Add(V("piper:ryan", "Ryan", "male", "high", 4, "piper"));
            fam.Add(V("win:zira", "Zira", "female", "Windows", 5, "windows"));
            fam.Add(V("win:mark", "Mark", "male", "Windows", 5, "windows"));
            fam.Add(V("piper:zed", "Zed", "", "low", 2, "piper"));
            // Ryan: Pilot, Copilot and the actors 4 to 7 and 10; Amy: Doctor and the checklist (actor 11 has no choice yet and shows the voice at its position); Zira: the ground unit;
            // the dispatcher and the heli rescuer are drawn (they use no voice); Mark and Zed are free
            Ctx.Settings.Get("EN", 1).Voice = "piper:ryan";
            Ctx.Settings.Get("EN", 2).Voice = "piper:ryan";
            Ctx.Settings.Get("EN", 3).Voice = "piper:amy";
            foreach (int k in new int[] { 4, 5, 6, 7, 10 }) Ctx.Settings.Get("EN", k).Voice = "piper:ryan";
            Ctx.Settings.Get("EN", 11).Voice = null;        // no voice chosen yet: the list position decides (actor 11 is the 11th: Amy, as the Actors tab shows it)
            Ctx.Settings.Get("EN", 8).Random = true;
            Ctx.Settings.Get("EN", 12).Random = true;
            Ctx.Settings.Get("EN", 9).Random = false;
            Ctx.Settings.Get("EN", 9).Voice = "win:zira";
            Dictionary<string, string> used = VoicePool.UsedMap("EN", fam);
            check("Ryan is used for the Pilot and the Copilot", used.ContainsKey("piper:ryan") && used["piper:ryan"].StartsWith("used for Pilot, Copilot", StringComparison.Ordinal));
            check("Amy is used for the Doctor", used.ContainsKey("piper:amy") && used["piper:amy"].IndexOf("Doctor", StringComparison.Ordinal) >= 0);
            check("Zira (chosen for the ground unit) is used for Ground unit", used.ContainsKey("win:zira") && used["win:zira"].IndexOf("Ground unit", StringComparison.Ordinal) >= 0);
            check("the actors that are drawn use no voice (Dispatcher, Heli rescuer not listed)", !string.Join("|", new List<string>(used.Values).ToArray()).Contains("Dispatcher") && !string.Join("|", new List<string>(used.Values).ToArray()).Contains("Heli rescuer"));
            rep.AppendLine("used: " + string.Join("; ", new List<string>(used.Keys).ConvertAll(delegate (string k) { return k + " = " + used[k]; }).ToArray()));

            List<string> tested = new List<string>();
            PoolForm f = new PoolForm("test", "EN", fam, new List<string>(new string[] { "piper:amy", "win:mark" }), used, delegate (VoiceInfo v) { tested.Add(v.Id); });
            check("the checked voices are the pool", string.Join(",", f.CheckedIds().ToArray()) == "piper:amy,win:mark");
            check("sorted by name at the start", string.Join("|", f.Order().ToArray()) == "Amy (Piper, en)|Mark (Windows, en)|Ryan (Piper, en)|Zed (Piper, en)|Zira (Windows, en)");
            f.SortBy(0);
            check("name again: descending", string.Join("|", f.Order().ToArray()) == "Zira (Windows, en)|Zed (Piper, en)|Ryan (Piper, en)|Mark (Windows, en)|Amy (Piper, en)");
            f.SortBy(1);
            check("sex: female, male, unknown (names inside)", string.Join("|", f.Order().ToArray()) == "Amy (Piper, en)|Zira (Windows, en)|Mark (Windows, en)|Ryan (Piper, en)|Zed (Piper, en)");
            f.SortBy(2);
            check("quality: low first", f.Order()[0] == "Zed (Piper, en)" && f.Order()[1] == "Amy (Piper, en)" && f.Order()[2] == "Ryan (Piper, en)");
            f.SortBy(2);
            check("quality again: best first (Windows voices, then high)", f.Order()[0] == "Mark (Windows, en)" && f.Order()[1] == "Zira (Windows, en)" && f.Order()[2] == "Ryan (Piper, en)");
            f.SortBy(3);
            check("used: the used ones first", f.Order()[0] == "Amy (Piper, en)" && f.Order()[1] == "Ryan (Piper, en)" && f.Order()[2] == "Zira (Windows, en)");
            f.SortBy(3);
            check("used again: the free ones first", f.Order()[0] == "Mark (Windows, en)" && f.Order()[1] == "Zed (Piper, en)");
            check("sorting keeps the checks", string.Join(",", f.CheckedIds().ToArray()) == "piper:amy,win:mark");

            // copy, clear, paste: the choices travel through the clipboard
            string clipError = null;
            try
            {
                f.CopyToClipboard();
                string text = Clipboard.GetText();
                rep.AppendLine("clipboard: " + text.Replace("\r\n", " / ").Replace("\t", " > "));
                List<string> back = VoicePool.ParseClip(text);
                check("the clipboard holds a header, the language and the two checked voices", back != null && string.Join(",", back.ToArray()) == "piper:amy,win:mark" && text.StartsWith(VoicePool.ClipHeader, StringComparison.Ordinal) && text.IndexOf("lang=EN", StringComparison.Ordinal) > 0);
                f.CheckNone();
                check("None clears the checks", f.CheckedIds().Count == 0);
                f.PasteFromClipboard();
                check("Paste checks the copied voices again", string.Join(",", f.CheckedIds().ToArray()) == "piper:amy,win:mark");
                rep.AppendLine("info: " + f.InfoText);
                // another list (another language, or a voice that is not installed): only what exists is checked
                List<VoiceInfo> small = new List<VoiceInfo>();
                small.Add(fam[0]); small.Add(fam[4]);
                PoolForm g = new PoolForm("test", "EN", small, null, new Dictionary<string, string>(), null);
                g.CheckNone();
                g.PasteFromClipboard();
                check("Paste in a list that has only one of the two voices checks that one and says so", string.Join(",", g.CheckedIds().ToArray()) == "piper:amy" && g.InfoText.IndexOf("1 of the clipboard is not available", StringComparison.Ordinal) >= 0);
                // nothing matches: unchanged
                Clipboard.SetText(VoicePool.ClipText("FR", new List<VoiceInfo>(new VoiceInfo[] { V("piper:fr-x", "Xavier", "male", "high", 4, "piper") })));
                g.SetChecked("piper:zed", true);
                string before = string.Join(",", g.CheckedIds().ToArray());
                g.PasteFromClipboard();
                check("Paste of voices that are not here changes nothing", string.Join(",", g.CheckedIds().ToArray()) == before && g.InfoText.IndexOf("nothing changed", StringComparison.Ordinal) >= 0);
                Clipboard.SetText("just some text");
                g.PasteFromClipboard();
                check("Paste of any other text changes nothing", string.Join(",", g.CheckedIds().ToArray()) == before && g.InfoText.IndexOf("does not hold a copied random list", StringComparison.Ordinal) >= 0);
                // nothing checked: Copy means every voice
                g.CheckNone();
                g.CopyToClipboard();
                List<string> all = VoicePool.ParseClip(Clipboard.GetText());
                check("Copy with nothing checked copies every voice", all != null && all.Count == 2);
            }
            catch (Exception ex) { clipError = ex.ToString(); }
            if (clipError != null) { rep.AppendLine("clipboard error: " + clipError); failed++; }
            check("Parse of a text without the header is null", VoicePool.ParseClip("piper:amy") == null && VoicePool.ParseClip("") == null);
            rep.AppendLine(failed == 0 ? "ALL PASS" : failed + " FAILED");
            File.WriteAllText(reportPath, rep.ToString());
            return failed == 0 ? 0 : 1;
        }
    }}
