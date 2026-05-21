namespace PropCreator
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.MenuStrip menuStrip;
        private System.Windows.Forms.ToolStripMenuItem fileMenu;
        private System.Windows.Forms.ToolStripMenuItem importMenuItem;
        private System.Windows.Forms.ToolStripMenuItem exitMenuItem;
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel statusLabel;
        private System.Windows.Forms.ToolStripProgressBar progressBar;
        private System.Windows.Forms.Panel viewportPanel;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            menuStrip = new System.Windows.Forms.MenuStrip();
            fileMenu = new System.Windows.Forms.ToolStripMenuItem();
            importMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            exitMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            statusStrip = new System.Windows.Forms.StatusStrip();
            statusLabel = new System.Windows.Forms.ToolStripStatusLabel();
            progressBar = new System.Windows.Forms.ToolStripProgressBar();
            viewportPanel = new System.Windows.Forms.Panel();

            menuStrip.SuspendLayout();
            statusStrip.SuspendLayout();
            SuspendLayout();

            // menuStrip
            menuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { fileMenu });
            menuStrip.Location = new System.Drawing.Point(0, 0);
            menuStrip.Name = "menuStrip";
            menuStrip.Size = new System.Drawing.Size(1024, 24);
            menuStrip.TabIndex = 0;
            menuStrip.Text = "menuStrip";

            // fileMenu
            fileMenu.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { importMenuItem, exitMenuItem });
            fileMenu.Name = "fileMenu";
            fileMenu.Text = "File";

            // importMenuItem
            importMenuItem.Name = "importMenuItem";
            importMenuItem.ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.I;
            importMenuItem.Text = "Import";
            importMenuItem.Click += new System.EventHandler(ImportMenuItem_Click);

            // exitMenuItem
            exitMenuItem.Name = "exitMenuItem";
            exitMenuItem.ShortcutKeys = System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.F4;
            exitMenuItem.Text = "Exit";
            exitMenuItem.Click += new System.EventHandler(ExitMenuItem_Click);

            // statusStrip
            statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { statusLabel, progressBar });
            statusStrip.Location = new System.Drawing.Point(0, 539);
            statusStrip.Name = "statusStrip";
            statusStrip.Size = new System.Drawing.Size(1024, 22);
            statusStrip.TabIndex = 1;
            statusStrip.Text = "statusStrip";

            // statusLabel
            statusLabel.Name = "statusLabel";
            statusLabel.Text = "Ready";
            statusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // progressBar
            progressBar.Name = "progressBar";
            progressBar.Size = new System.Drawing.Size(150, 16);
            progressBar.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
            progressBar.Visible = false;

            // viewportPanel
            viewportPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            viewportPanel.Location = new System.Drawing.Point(0, 24);
            viewportPanel.Name = "viewportPanel";
            viewportPanel.Size = new System.Drawing.Size(1024, 515);
            viewportPanel.TabIndex = 2;

            // MainForm
            AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(1024, 561);
            Controls.Add(viewportPanel);
            Controls.Add(statusStrip);
            Controls.Add(menuStrip);
            MainMenuStrip = menuStrip;
            Name = "MainForm";
            Text = "PropCreator";
            menuStrip.ResumeLayout(false);
            menuStrip.PerformLayout();
            statusStrip.ResumeLayout(false);
            statusStrip.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }
    }
}
