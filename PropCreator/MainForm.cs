using SharpDX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PropCreator
{
    public partial class MainForm : Form
    {
        private PropRenderer renderer;
        private bool loaded;

        public MainForm()
        {
            InitializeComponent();
            Text = "PropCreator";
        }

        private async void ImportMenuItem_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "FBX Files (*.fbx)|*.fbx|All Files (*.*)|*.*";
                dialog.Title = "Import FBX Model";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    await LoadModelAsync(dialog.FileName);
                }
            }
        }

        private async Task LoadModelAsync(string filePath)
        {
            importMenuItem.Enabled = false;
            progressBar.Visible = true;
            UpdateStatus("Loading " + Path.GetFileName(filePath) + "...");

            try
            {
                var meshes = await Task.Run(() => FbxParser.Load(filePath));

                if (meshes.Count == 0)
                {
                    MessageBox.Show("No mesh data found in FBX file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    UpdateStatus("Failed: no mesh data");
                    return;
                }

                renderer?.LoadMeshes(meshes);
                loaded = true;
                UpdateStatus("Loaded: " + Path.GetFileName(filePath));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load model: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateStatus("Failed to load model");
            }
            finally
            {
                importMenuItem.Enabled = true;
                progressBar.Visible = false;
            }
        }

        private void ExitMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void UpdateStatus(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => statusLabel.Text = text));
            }
            else
            {
                statusLabel.Text = text;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            renderer = new PropRenderer(viewportPanel);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            renderer?.Dispose();
            base.OnFormClosing(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (renderer != null && WindowState != FormWindowState.Minimized)
            {
                renderer.Resize(viewportPanel.ClientSize.Width, viewportPanel.ClientSize.Height);
            }
        }

    }

    public class MeshData
    {
        public string Name;
        public Vector3[] Vertices;
        public Vector3[] Normals;
        public Vector2[] Texcoords;
        public int[] Indices;
        public Vector3 BoundingCenter;
        public float BoundingRadius;
    }
}
