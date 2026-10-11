using System;
using System.Drawing;
using System.Windows.Forms;

namespace HPGMissionCompanion
{
    // A window with release notes. Ask() adds the install question (Yes = download and install, the default answer is No); View() only shows the notes.
    internal sealed class NotesForm : Form
    {
        NotesForm(string title, string header, string notes, bool question)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(780, 580);
            MinimumSize = new Size(520, 360);

            TextBox box = new TextBox();
            box.Multiline = true;
            box.ReadOnly = true;
            box.ScrollBars = ScrollBars.Vertical;
            box.WordWrap = true;
            box.Dock = DockStyle.Fill;
            box.BackColor = SystemColors.Window;
            box.Text = (notes ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");

            Label head = new Label();
            head.Text = header ?? "";
            head.Dock = DockStyle.Top;
            head.Padding = new Padding(10, 10, 10, 6);
            int lines = 1;
            foreach (char c in head.Text) if (c == '\n') lines++;
            head.Height = Math.Min(260, 20 + lines * 18);

            FlowLayoutPanel bottom = new FlowLayoutPanel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 48;
            bottom.FlowDirection = FlowDirection.RightToLeft;
            bottom.Padding = new Padding(10, 9, 10, 0);
            if (question)
            {
                // right to left: the first button added is the rightmost one
                Button no = new Button();
                no.Text = "Not now";
                no.DialogResult = DialogResult.No;
                no.Size = new Size(100, 30);
                Button yes = new Button();
                yes.Text = "Download and install";
                yes.DialogResult = DialogResult.Yes;
                yes.Size = new Size(170, 30);
                bottom.Controls.Add(no);
                bottom.Controls.Add(yes);
                AcceptButton = no;
                CancelButton = no;
            }
            else
            {
                Button close = new Button();
                close.Text = "Close";
                close.DialogResult = DialogResult.OK;
                close.Size = new Size(100, 30);
                bottom.Controls.Add(close);
                AcceptButton = close;
                CancelButton = close;
            }
            // the docking order: the last control added is laid out first
            Controls.Add(box);
            Controls.Add(head);
            Controls.Add(bottom);
            Shown += delegate { box.Select(0, 0); };
        }

        public static DialogResult Ask(IWin32Window owner, string title, string header, string notes)
        {
            using (NotesForm f = new NotesForm(title, header, notes, true)) return f.ShowDialog(owner);
        }

        public static void View(IWin32Window owner, string title, string header, string notes)
        {
            using (NotesForm f = new NotesForm(title, header, notes, false)) f.ShowDialog(owner);
        }
    }

    // A small message in the corner of the screen, over the other windows and without taking the focus: the Windows balloon of the notification area is not always shown
    // (notification settings, focus assist, a tray icon the shell has not drawn yet), so an update found at the start is announced here too. Click it, or the button, to open the Updates.
    internal sealed class UpdatePopup : Form
    {
        readonly Timer closer = new Timer();

        UpdatePopup(string title, string text, string button, Action onOpen)
        {
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            ControlBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Font = SystemFonts.MessageBoxFont;
            Text = title;
            BackColor = Color.FromArgb(255, 238, 190);
            ClientSize = new Size(380, 112);
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);

            Label head = new Label();
            head.Text = title;
            head.Font = new Font(Font, FontStyle.Bold);
            head.ForeColor = Color.FromArgb(150, 55, 0);
            head.Location = new Point(12, 10);
            head.AutoSize = true;
            Label body = new Label();
            body.Text = text;
            body.Location = new Point(12, 34);
            body.Size = new Size(356, 36);
            Button open = new Button();
            open.Text = button;
            open.Location = new Point(12, 74);
            open.Size = new Size(150, 28);
            open.Click += delegate { Close(); if (onOpen != null) onOpen(); };
            Button later = new Button();
            later.Text = "Later";
            later.Location = new Point(172, 74);
            later.Size = new Size(80, 28);
            later.Click += delegate { Close(); };
            Controls.Add(head);
            Controls.Add(body);
            Controls.Add(open);
            Controls.Add(later);

            closer.Interval = 30000;
            closer.Tick += delegate { closer.Stop(); Close(); };
            Shown += delegate { closer.Start(); };
            FormClosed += delegate { closer.Stop(); closer.Dispose(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public static void Announce(string title, string text, string button, Action onOpen)
        {
            UpdatePopup p = new UpdatePopup(title, text, button, onOpen);
            p.Show();
        }
    }
}
