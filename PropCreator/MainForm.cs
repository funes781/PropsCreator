using CodeWalker;
using SharpDX;
using SharpDX.Direct3D11;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Device = SharpDX.Direct3D11.Device;

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

        private void ImportMenuItem_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "FBX Files (*.fbx)|*.fbx|All Files (*.*)|*.*";
                dialog.Title = "Import FBX Model";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    LoadModel(dialog.FileName);
                }
            }
        }

        private void ExitMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void LoadModel(string filePath)
        {
            try
            {
                UpdateStatus("Loading " + Path.GetFileName(filePath) + "...");

                var data = File.ReadAllBytes(filePath);
                var fbxDoc = FbxIO.Read(data);
                var sceneNodes = fbxDoc.GetSceneNodes();

                if (sceneNodes == null || sceneNodes.Count == 0)
                {
                    MessageBox.Show("No scene nodes found in FBX file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    UpdateStatus("Failed: no scene nodes");
                    return;
                }

                var meshes = ExtractMeshes(sceneNodes);
                if (meshes.Count == 0)
                {
                    MessageBox.Show("No mesh data found in FBX file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    UpdateStatus("Failed: no mesh data");
                    return;
                }

                renderer?.LoadMeshes(meshes);
                loaded = true;
                UpdateStatus("Loaded: " + Path.GetFileName(filePath) + " (" + meshes.Count + " meshes)");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load model: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateStatus("Failed to load model");
            }
        }

        private List<MeshData> ExtractMeshes(List<FbxNode> nodes)
        {
            var result = new List<MeshData>();
            foreach (var node in nodes)
            {
                ExtractMeshesRecursive(node, result);
            }
            return result;
        }

        private void ExtractMeshesRecursive(FbxNode node, List<MeshData> result)
        {
            if (node == null) return;

            // Check if this node has a Geometry connection (i.e. it's a Model with mesh data)
            FbxNode geometryNode = null;
            foreach (var conn in node.Connections)
            {
                if (conn?.Name == "Geometry")
                {
                    geometryNode = conn;
                    break;
                }
            }

            if (geometryNode != null)
            {
                var mesh = ExtractMeshFromGeometry(geometryNode, node);
                if (mesh != null)
                {
                    result.Add(mesh);
                }
            }

            // Recurse into child connections
            foreach (var conn in node.Connections)
            {
                if (conn?.Name == "Model")
                {
                    ExtractMeshesRecursive(conn, result);
                }
            }
        }

        private MeshData ExtractMeshFromGeometry(FbxNode geometryNode, FbxNode modelNode)
        {
            var fnVerts = geometryNode["Vertices"]?.Value as double[];
            var fnIndices = geometryNode["PolygonVertexIndex"]?.Value as int[];

            if (fnVerts == null || fnIndices == null) return null;

            // Collect layer data
            FbxNode fnNormals = null;
            FbxNode fnTexcoords = null;

            foreach (var child in geometryNode.Nodes)
            {
                if (child == null) continue;
                switch (child.Name)
                {
                    case "LayerElementNormal":
                        if (fnNormals == null) fnNormals = child;
                        break;
                    case "LayerElementUV":
                        if (fnTexcoords == null) fnTexcoords = child;
                        break;
                }
            }

            // Build polygons
            var vertList = new List<Vector3>();
            var normalList = new List<Vector3>();
            var texcoordList = new List<Vector2>();
            var indexList = new List<int>();

            var fPolyVerts = new List<PolyVert>();
            int vertCounter = 0;

            foreach (var fnIndex in fnIndices)
            {
                int absIdx = (fnIndex < 0) ? (-fnIndex - 1) : fnIndex;
                var pos = GetVector3(fnVerts, absIdx);
                var pv = new PolyVert { Position = pos, AbsoluteIndex = absIdx };
                fPolyVerts.Add(pv);

                if (fnIndex < 0)
                {
                    // End of polygon - triangulate
                    if (fPolyVerts.Count >= 3)
                    {
                        var v0 = fPolyVerts[0];
                        for (int vi = 2; vi < fPolyVerts.Count; vi++)
                        {
                            var v1 = fPolyVerts[vi - 1];
                            var v2 = fPolyVerts[vi];
                            AddUniqueVertex(v0, fnNormals, fnTexcoords, vertList, normalList, texcoordList, indexList);
                            AddUniqueVertex(v1, fnNormals, fnTexcoords, vertList, normalList, texcoordList, indexList);
                            AddUniqueVertex(v2, fnNormals, fnTexcoords, vertList, normalList, texcoordList, indexList);
                        }
                    }
                    fPolyVerts.Clear();
                }

                vertCounter++;
            }

            if (vertList.Count == 0 || indexList.Count == 0) return null;

            // Get model name
            var name = "Mesh";
            if (modelNode.Properties.Count > 1 && modelNode.Properties[1] is string s)
            {
                name = s.Replace("Model::", "");
            }

            var mesh = new MeshData
            {
                Name = name,
                Vertices = vertList.ToArray(),
                Normals = normalList.Count > 0 ? normalList.ToArray() : GenerateNormals(vertList, indexList),
                Texcoords = texcoordList.Count > 0 ? texcoordList.ToArray() : null,
                Indices = indexList.ToArray()
            };

            // Compute bounding sphere
            var center = Vector3.Zero;
            foreach (var v in mesh.Vertices) center += v;
            center /= mesh.Vertices.Length;

            float maxRad = 0;
            foreach (var v in mesh.Vertices)
            {
                var d = (v - center).Length();
                if (d > maxRad) maxRad = d;
            }
            mesh.BoundingCenter = center;
            mesh.BoundingRadius = maxRad;

            return mesh;
        }

        private void AddUniqueVertex(PolyVert pv, FbxNode fnNormals, FbxNode fnTexcoords,
            List<Vector3> verts, List<Vector3> normals, List<Vector2> texcoords, List<int> indices)
        {
            var pos = pv.Position;
            var norm = pv.AbsoluteIndex >= 0 ? GetNormal(fnNormals, pv.AbsoluteIndex) : Vector3.UnitY;
            var tc = pv.AbsoluteIndex >= 0 ? GetTexcoord(fnTexcoords, pv.AbsoluteIndex) : Vector2.Zero;

            for (int i = 0; i < verts.Count; i++)
            {
                if (Vector3.DistanceSquared(verts[i], pos) < 0.0001f &&
                    Vector3.DistanceSquared(normals.Count > i ? normals[i] : Vector3.Zero, norm) < 0.0001f)
                {
                    indices.Add(i);
                    return;
                }
            }

            indices.Add(verts.Count);
            verts.Add(pos);
            normals.Add(norm);
            texcoords.Add(tc);
        }

        private Vector3 GetVector3(double[] arr, int index)
        {
            int i = index * 3;
            return new Vector3((float)arr[i], (float)arr[i + 1], (float)arr[i + 2]);
        }

        private Vector3 GetNormal(FbxNode normalNode, int index)
        {
            if (normalNode == null) return Vector3.UnitY;

            var arNorms = normalNode["Normals"]?.Value as double[];
            var aiNorms = normalNode["NormalIndex"]?.Value as int[];
            if (arNorms == null) return Vector3.UnitY;

            bool indexed = aiNorms != null;
            int ai = indexed ? aiNorms[index] : index;
            return GetVector3(arNorms, ai);
        }

        private Vector2 GetTexcoord(FbxNode texcoordNode, int index)
        {
            if (texcoordNode == null) return Vector2.Zero;

            var arTexcs = texcoordNode["UV"]?.Value as double[];
            var aiTexcs = texcoordNode["UVIndex"]?.Value as int[];
            if (arTexcs == null) return Vector2.Zero;

            bool indexed = aiTexcs != null;
            int ai = indexed ? aiTexcs[index] : index;
            int i = ai * 2;
            return new Vector2((float)arTexcs[i], (float)arTexcs[i + 1]);
        }

        private Vector3[] GenerateNormals(Vector3[] vertices, int[] indices)
        {
            var normals = new Vector3[vertices.Length];
            for (int i = 0; i < indices.Length; i += 3)
            {
                int i0 = indices[i];
                int i1 = indices[i + 1];
                int i2 = indices[i + 2];
                var edge1 = vertices[i1] - vertices[i0];
                var edge2 = vertices[i2] - vertices[i0];
                var faceNormal = Vector3.Cross(edge1, edge2);
                if (faceNormal.Length() > 0.0001f)
                    faceNormal = Vector3.Normalize(faceNormal);
                normals[i0] += faceNormal;
                normals[i1] += faceNormal;
                normals[i2] += faceNormal;
            }
            for (int i = 0; i < normals.Length; i++)
            {
                if (normals[i].Length() > 0.0001f)
                    normals[i] = Vector3.Normalize(normals[i]);
                else
                    normals[i] = Vector3.UnitY;
            }
            return normals;
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

        private struct PolyVert
        {
            public Vector3 Position;
            public int AbsoluteIndex;
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
