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
}
