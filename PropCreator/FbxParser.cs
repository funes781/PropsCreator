using SharpDX;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PropCreator
{
    public static class FbxParser
    {
        public static List<MeshData> Load(string filePath)
        {
            var data = File.ReadAllBytes(filePath);
            var result = Parse(data);
            if (result.Count == 0)
                throw new InvalidDataException("No mesh data found in FBX file. The file may be in an unsupported format.");
            return result;
        }

        public static List<MeshData> Parse(byte[] data)
        {
            var reader = new FbxReader(data);
            return reader.ReadMeshes();
        }

        private class FbxNode
        {
            public string Name;
            public List<object> Properties = new List<object>();
            public List<FbxNode> Children = new List<FbxNode>();
        }

        private class FbxReader
        {
            private byte[] data;
            private int pos;
            private bool is64Bit;

            public FbxReader(byte[] data)
            {
                this.data = data;
            }

            public List<MeshData> ReadMeshes()
            {
                if (data.Length < 27)
                    throw new InvalidDataException("File too small to be a valid FBX file.");

                var magic = Encoding.ASCII.GetString(data, 0, 20);
                if (!magic.StartsWith("Kaydara FBX Binary"))
                {
                    if (data.Length >= 18 && Encoding.ASCII.GetString(data, 0, 18).StartsWith("Kaydara FBX ASCII"))
                        throw new InvalidDataException("ASCII FBX format is not supported. Please export as binary FBX.");
                    throw new InvalidDataException("Not a valid FBX file.");
                }

                // FBX binary header: 21 bytes magic + 0x00 + 0x1A + 0x00 + 4 bytes version = 28 bytes
                // Version at offset 24, data starts at offset 28
                uint version = BitConverter.ToUInt32(data, 24);
                int headerSize = 28;

                // Fallback: some files have slightly different layout
                if (version < 6000 || version > 8000)
                {
                    version = BitConverter.ToUInt32(data, 23);
                    headerSize = 27;
                }
                if (version < 6000 || version > 8000)
                {
                    version = BitConverter.ToUInt32(data, 25);
                    headerSize = 29;
                }
                if (version < 6000 || version > 8000)
                {
                    for (int i = 20; i <= 30 && i + 4 <= data.Length; i++)
                    {
                        uint v = BitConverter.ToUInt32(data, i);
                        if (v >= 6000 && v <= 8000)
                        {
                            version = v;
                            headerSize = i + 4;
                            break;
                        }
                    }
                }

                if (version == 0)
                    throw new InvalidDataException("Could not determine FBX version.");

                is64Bit = version >= 7500;
                pos = headerSize;

                var root = new FbxNode();
                while (pos < data.Length)
                {
                    if (IsNullRecord()) { SkipRecord(); continue; }
                    var node = ReadNode();
                    if (node != null)
                        root.Children.Add(node);
                    else
                        break;
                }

                return ExtractMeshes(root);
            }

            private int HeaderLen => is64Bit ? 21 : 13;

            private bool IsNullRecord()
            {
                if (pos + HeaderLen > data.Length) return false;
                if (is64Bit)
                    return BitConverter.ToUInt64(data, pos) == 0;
                else
                    return BitConverter.ToUInt32(data, pos) == 0;
            }

            private void SkipRecord()
            {
                pos += HeaderLen;
            }

            private FbxNode ReadNode()
            {
                if (pos >= data.Length) return null;

                int headerLen = HeaderLen;

                if (pos + headerLen > data.Length)
                {
                    pos = data.Length;
                    return null;
                }

                long endOffset;
                long numProps;
                uint propListLen;
                int nameLen;

                if (is64Bit)
                {
                    endOffset = (long)BitConverter.ToUInt64(data, pos);
                    if (endOffset == 0) { pos += 21; return null; }
                    numProps = (long)BitConverter.ToUInt64(data, pos + 8);
                    propListLen = BitConverter.ToUInt32(data, pos + 16);
                    nameLen = data[pos + 20];
                }
                else
                {
                    endOffset = BitConverter.ToUInt32(data, pos);
                    if (endOffset == 0) { pos += 13; return null; }
                    numProps = BitConverter.ToUInt32(data, pos + 4);
                    propListLen = BitConverter.ToUInt32(data, pos + 8);
                    nameLen = data[pos + 12];
                }

                if (endOffset > data.Length || endOffset < 0)
                {
                    pos = data.Length;
                    return null;
                }

                if (pos + headerLen + nameLen > data.Length)
                {
                    pos = data.Length;
                    return null;
                }

                string name = nameLen > 0 ? Encoding.ASCII.GetString(data, pos + headerLen, nameLen) : "";
                var node = new FbxNode { Name = name };

                int p = pos + headerLen + nameLen;

                for (int i = 0; i < numProps; i++)
                    node.Properties.Add(ReadProperty(ref p));

                while (p < endOffset)
                {
                    if (IsNullAt(p, endOffset)) { p += HeaderLen; continue; }
                    var child = ReadNodeAt(ref p);
                    if (child != null)
                        node.Children.Add(child);
                    else
                        break;
                }

                pos = (int)endOffset;
                return node;
            }

            private bool IsNullAt(int p, long endOffset)
            {
                if (p + HeaderLen > endOffset || p + HeaderLen > data.Length) return false;
                if (is64Bit)
                    return BitConverter.ToUInt64(data, p) == 0;
                else
                    return BitConverter.ToUInt32(data, p) == 0;
            }

            private FbxNode ReadNodeAt(ref int p)
            {
                if (p >= data.Length) return null;

                int headerLen = is64Bit ? 21 : 13;

                if (p + headerLen > data.Length) return null;

                long endOffset;
                long numProps;
                int nameLen;

                if (is64Bit)
                {
                    endOffset = (long)BitConverter.ToUInt64(data, p);
                    if (endOffset == 0 || endOffset > data.Length) { p += 21; return null; }
                    numProps = (long)BitConverter.ToUInt64(data, p + 8);
                    nameLen = data[p + 20];
                }
                else
                {
                    uint eo = BitConverter.ToUInt32(data, p);
                    if (eo == 0) { p += 13; return null; }
                    if (eo > data.Length) { p = data.Length; return null; }
                    endOffset = eo;
                    numProps = BitConverter.ToUInt32(data, p + 4);
                    nameLen = data[p + 12];
                }

                if (p + headerLen + nameLen > data.Length) return null;

                string name = nameLen > 0 ? Encoding.ASCII.GetString(data, p + headerLen, nameLen) : "";
                var node = new FbxNode { Name = name };

                int pp = p + headerLen + nameLen;

                for (int i = 0; i < numProps; i++)
                    node.Properties.Add(ReadProperty(ref pp));

                while (pp < endOffset)
                {
                    if (IsNullAt(pp, endOffset)) { pp += HeaderLen; continue; }
                    var child = ReadNodeAt(ref pp);
                    if (child != null)
                        node.Children.Add(child);
                }

                p = (int)endOffset;
                return node;
            }

            private object ReadProperty(ref int p)
            {
                if (p >= data.Length) return null;

                char type = (char)data[p];
                p++;

                switch (type)
                {
                    case 'Y':
                        if (!CanRead(p, 2)) return null;
                        short sval = BitConverter.ToInt16(data, p);
                        p += 2;
                        return (long)sval;
                    case 'C':
                        if (!CanRead(p, 1)) return null;
                        bool bval = data[p] != 0;
                        p += 1;
                        return bval;
                    case 'I':
                        if (!CanRead(p, 4)) return null;
                        int ival = BitConverter.ToInt32(data, p);
                        p += 4;
                        return (long)ival;
                    case 'F':
                        if (!CanRead(p, 4)) return null;
                        float fval = BitConverter.ToSingle(data, p);
                        p += 4;
                        return (double)fval;
                    case 'D':
                        if (!CanRead(p, 8)) return null;
                        double dval = BitConverter.ToDouble(data, p);
                        p += 8;
                        return dval;
                    case 'L':
                        if (!CanRead(p, 8)) return null;
                        long lval = BitConverter.ToInt64(data, p);
                        p += 8;
                        return lval;
                    case 'f':
                        return ReadFloatArray(ref p);
                    case 'd':
                        return ReadDoubleArray(ref p);
                    case 'i':
                        return ReadInt32Array(ref p);
                    case 'l':
                        return ReadInt64Array(ref p);
                    case 'b':
                        return ReadBlob(ref p);
                    case 'S':
                        if (!CanRead(p, 4)) return string.Empty;
                        int len = BitConverter.ToInt32(data, p);
                        p += 4;
                        if (len > 0 && CanRead(p, len))
                        {
                            string str = Encoding.ASCII.GetString(data, p, len);
                            p += len;
                            return str;
                        }
                        return string.Empty;
                    case 'R':
                        if (!CanRead(p, 4)) return new byte[0];
                        int rawLen = BitConverter.ToInt32(data, p);
                        p += 4;
                        if (rawLen > 0 && CanRead(p, rawLen))
                        {
                            byte[] raw = new byte[rawLen];
                            Array.Copy(data, p, raw, 0, rawLen);
                            p += rawLen;
                            return raw;
                        }
                        return new byte[0];
                    default:
                        return null;
                }
            }

            private bool CanRead(int p, int bytes) => p >= 0 && p + bytes <= data.Length;

            private byte[] DecompressZlib(byte[] compressed, int expectedBytes)
            {
                if (compressed == null || compressed.Length < 4) return new byte[0];

                // Try raw deflate first (no zlib header)
                try
                {
                    using (var ms = new MemoryStream(compressed))
                    using (var deflate = new DeflateStream(ms, CompressionMode.Decompress))
                    using (var result = new MemoryStream(expectedBytes > 0 ? expectedBytes : 4096))
                    {
                        deflate.CopyTo(result);
                        var data = result.ToArray();
                        if (data.Length >= expectedBytes)
                            return data;
                    }
                }
                catch { }

                // Try stripping zlib header (2 bytes) + adler32 footer (4 bytes)
                if (compressed.Length > 6)
                {
                    try
                    {
                        using (var ms = new MemoryStream(compressed, 2, compressed.Length - 6))
                        using (var deflate = new DeflateStream(ms, CompressionMode.Decompress))
                        using (var result = new MemoryStream(expectedBytes > 0 ? expectedBytes : 4096))
                        {
                            deflate.CopyTo(result);
                            var data = result.ToArray();
                            if (data.Length >= expectedBytes)
                                return data;
                        }
                    }
                    catch { }
                }

                return new byte[0];
            }

            private double[] ReadDoubleArray(ref int p)
            {
                if (!CanRead(p, 12)) return new double[0];
                int count = BitConverter.ToInt32(data, p);
                int encoding = BitConverter.ToInt32(data, p + 4);
                int compressedLen = BitConverter.ToInt32(data, p + 8);
                p += 12;

                var result = new double[count];
                if (encoding == 0 && count > 0 && CanRead(p, count * 8))
                {
                    for (int i = 0; i < count; i++)
                    {
                        result[i] = BitConverter.ToDouble(data, p);
                        p += 8;
                    }
                }
                else if (encoding == 1 && count > 0 && CanRead(p, compressedLen))
                {
                    var compressed = new byte[compressedLen];
                    Array.Copy(data, p, compressed, 0, compressedLen);
                    p += compressedLen;
                    var decompressed = DecompressZlib(compressed, count * 8);
                    if (decompressed.Length >= count * 8)
                    {
                        for (int i = 0; i < count; i++)
                            result[i] = BitConverter.ToDouble(decompressed, i * 8);
                    }
                }
                else if (count > 0)
                {
                    p += compressedLen;
                }
                return result;
            }

            private int[] ReadInt32Array(ref int p)
            {
                if (!CanRead(p, 12)) return new int[0];
                int count = BitConverter.ToInt32(data, p);
                int encoding = BitConverter.ToInt32(data, p + 4);
                int compressedLen = BitConverter.ToInt32(data, p + 8);
                p += 12;

                var result = new int[count];
                if (encoding == 0 && count > 0 && CanRead(p, count * 4))
                {
                    for (int i = 0; i < count; i++)
                    {
                        result[i] = BitConverter.ToInt32(data, p);
                        p += 4;
                    }
                }
                else if (encoding == 1 && count > 0 && CanRead(p, compressedLen))
                {
                    var compressed = new byte[compressedLen];
                    Array.Copy(data, p, compressed, 0, compressedLen);
                    p += compressedLen;
                    var decompressed = DecompressZlib(compressed, count * 4);
                    if (decompressed.Length >= count * 4)
                    {
                        for (int i = 0; i < count; i++)
                            result[i] = BitConverter.ToInt32(decompressed, i * 4);
                    }
                }
                else if (count > 0)
                {
                    p += compressedLen;
                }
                return result;
            }

            private long[] ReadInt64Array(ref int p)
            {
                if (!CanRead(p, 12)) return new long[0];
                int count = BitConverter.ToInt32(data, p);
                int encoding = BitConverter.ToInt32(data, p + 4);
                int compressedLen = BitConverter.ToInt32(data, p + 8);
                p += 12;

                var result = new long[count];
                if (encoding == 0 && count > 0 && CanRead(p, count * 8))
                {
                    for (int i = 0; i < count; i++)
                    {
                        result[i] = BitConverter.ToInt64(data, p);
                        p += 8;
                    }
                }
                else if (encoding == 1 && count > 0 && CanRead(p, compressedLen))
                {
                    var compressed = new byte[compressedLen];
                    Array.Copy(data, p, compressed, 0, compressedLen);
                    p += compressedLen;
                    var decompressed = DecompressZlib(compressed, count * 8);
                    if (decompressed.Length >= count * 8)
                    {
                        for (int i = 0; i < count; i++)
                            result[i] = BitConverter.ToInt64(decompressed, i * 8);
                    }
                }
                else if (count > 0)
                {
                    p += compressedLen;
                }
                return result;
            }

            private float[] ReadFloatArray(ref int p)
            {
                if (!CanRead(p, 12)) return new float[0];
                int count = BitConverter.ToInt32(data, p);
                int encoding = BitConverter.ToInt32(data, p + 4);
                int compressedLen = BitConverter.ToInt32(data, p + 8);
                p += 12;

                var result = new float[count];
                if (encoding == 0 && count > 0 && CanRead(p, count * 4))
                {
                    for (int i = 0; i < count; i++)
                    {
                        result[i] = BitConverter.ToSingle(data, p);
                        p += 4;
                    }
                }
                else if (encoding == 1 && count > 0 && CanRead(p, compressedLen))
                {
                    var compressed = new byte[compressedLen];
                    Array.Copy(data, p, compressed, 0, compressedLen);
                    p += compressedLen;
                    var decompressed = DecompressZlib(compressed, count * 4);
                    if (decompressed.Length >= count * 4)
                    {
                        for (int i = 0; i < count; i++)
                            result[i] = BitConverter.ToSingle(decompressed, i * 4);
                    }
                }
                else if (count > 0)
                {
                    p += compressedLen;
                }
                return result;
            }

            private byte[] ReadBlob(ref int p)
            {
                if (!CanRead(p, 12)) return new byte[0];
                int count = BitConverter.ToInt32(data, p);
                int encoding = BitConverter.ToInt32(data, p + 4);
                int compressedLen = BitConverter.ToInt32(data, p + 8);
                p += 12;

                var result = new byte[count];
                if (encoding == 0 && count > 0 && CanRead(p, count))
                {
                    Array.Copy(data, p, result, 0, count);
                    p += count;
                }
                else if (encoding == 1 && count > 0 && CanRead(p, compressedLen))
                {
                    var compressed = new byte[compressedLen];
                    Array.Copy(data, p, compressed, 0, compressedLen);
                    p += compressedLen;
                    var decompressed = DecompressZlib(compressed, count);
                    if (decompressed.Length >= count)
                        result = decompressed;
                }
                else if (count > 0)
                {
                    p += compressedLen;
                }
                return result;
            }

            private static FbxNode FindChild(FbxNode parent, string name)
            {
                foreach (var child in parent.Children)
                {
                    if (child.Name == name) return child;
                    var found = FindChild(child, name);
                    if (found != null) return found;
                }
                return null;
            }

            private static T[] GetArrayData<T>(object prop)
            {
                if (prop is T[] arr) return arr;
                if (typeof(T) == typeof(double) && prop is float[] farr)
                {
                    var darr = new double[farr.Length];
                    for (int i = 0; i < farr.Length; i++) darr[i] = farr[i];
                    return (T[])(object)darr;
                }
                if (typeof(T) == typeof(int) && prop is long[] larr && larr.Length > 0)
                {
                    var iarr = new int[larr.Length];
                    for (int i = 0; i < larr.Length; i++) iarr[i] = (int)larr[i];
                    return (T[])(object)iarr;
                }
                return null;
            }

            private static string CollectNodeNames(FbxNode n, int depth = 0)
            {
                if (depth > 10) return "";
                var result = n.Name;
                if (n.Properties.Count > 1 && n.Properties[1] is string ns)
                    result += "(" + ns + ")";
                foreach (var c in n.Children)
                {
                    var childInfo = CollectNodeNames(c, depth + 1);
                    if (!string.IsNullOrEmpty(childInfo))
                        result += " [" + childInfo + "]";
                }
                return result;
            }

            private List<MeshData> ExtractMeshes(FbxNode root)
            {
                if (root.Children.Count == 0)
                    throw new InvalidDataException("No top-level nodes found.");

                var objectsNode = FindChild(root, "Objects");
                var connectionsNode = FindChild(root, "Connections");
                if (objectsNode == null)
                {
                    var summary = "";
                    foreach (var c in root.Children)
                        summary += CollectNodeNames(c) + " | ";
                    throw new InvalidDataException("No 'Objects' node found. Nodes: " + summary);
                }

                var geometries = new Dictionary<long, FbxNode>();
                var models = new Dictionary<long, FbxNode>();

                int objChildCount = 0;
                foreach (var node in objectsNode.Children)
                {
                    objChildCount++;
                    if (node.Name == "Geometry" && node.Properties.Count >= 2)
                        geometries[(long)node.Properties[0]] = node;
                    else if (node.Name == "Model" && node.Properties.Count >= 2)
                        models[(long)node.Properties[0]] = node;
                }

                if (geometries.Count == 0)
                {
                    var nodeTypes = new HashSet<string>();
                    foreach (var c in objectsNode.Children)
                    {
                        var label = c.Name;
                        if (c.Properties.Count > 1 && c.Properties[1] is string sn)
                            label += "(" + sn + ")";
                        nodeTypes.Add(label);
                    }
                    throw new InvalidDataException(
                        "Objects node found (" + objChildCount + " children) but no Geometry nodes. Types: " +
                        string.Join(", ", nodeTypes));
                }

                var modelToGeometry = new Dictionary<long, long>();
                if (connectionsNode != null)
                {
                    foreach (var conn in connectionsNode.Children)
                    {
                        if (conn.Properties.Count >= 3)
                        {
                            var type = conn.Properties[0] as string;
                            if (type == "OO")
                            {
                                long childId = (long)conn.Properties[1];
                                long parentId = (long)conn.Properties[2];
                                if (geometries.ContainsKey(childId) && models.ContainsKey(parentId))
                                    modelToGeometry[parentId] = childId;
                            }
                        }
                    }
                }

                var meshes = new List<MeshData>();
                foreach (var kv in modelToGeometry)
                {
                    var mesh = ExtractMesh(geometries[kv.Value], models[kv.Key]);
                    if (mesh != null) meshes.Add(mesh);
                }

                if (meshes.Count == 0 && geometries.Count > 0)
                {
                    foreach (var kv in geometries)
                    {
                        var mesh = ExtractMesh(kv.Value, null);
                        if (mesh != null) meshes.Add(mesh);
                    }
                }

                if (meshes.Count == 0 && geometries.Count > 0)
                {
                    string sampleChildren = "";
                    int geoIdx = 0;
                    foreach (var g in geometries)
                    {
                        geoIdx++;
                        var geo = g.Value;
                        var cnames = new List<string>();
                        foreach (var c in geo.Children)
                        {
                            int pc = c.Properties.Count;
                            string info = "'" + c.Name + "'(" + pc + "p)";
                            if (pc > 0)
                            {
                                var dv = GetArrayData<double>(c.Properties[0]);
                                var iv = GetArrayData<int>(c.Properties[0]);
                                info += dv != null ? "d" + dv.Length : (iv != null ? "i" + iv.Length : "?");
                            }
                            cnames.Add(info);
                        }

                        var vertsNode = FindChild(geo, "Vertices");
                        var idxNode = FindChild(geo, "PolygonVertexIndex");
                        int vertCount = 0, idxCount = 0;
                        if (vertsNode != null && vertsNode.Properties.Count > 0)
                        { var d = GetArrayData<double>(vertsNode.Properties[0]); if (d != null) vertCount = d.Length; }
                        if (idxNode != null && idxNode.Properties.Count > 0)
                        { var d = GetArrayData<int>(idxNode.Properties[0]); if (d != null) idxCount = d.Length; }

                        sampleChildren += string.Format("Geo{0}: verts={1} idx={2} [{3}] | ",
                            geoIdx, vertCount, idxCount, string.Join(", ", cnames));
                        if (sampleChildren.Length > 1000) break;
                    }
                    throw new InvalidDataException(
                        "Found " + geometries.Count + " Geometry nodes. Details: " + sampleChildren);
                }

                return meshes;
            }

            private MeshData ExtractMesh(FbxNode geoNode, FbxNode modelNode)
            {
                string name = "Mesh";
                if (modelNode != null && modelNode.Properties.Count >= 2 && modelNode.Properties[1] is string s)
                    name = s.Replace("Model::", "");

                var vertsNode = FindChild(geoNode, "Vertices");
                var indicesNode = FindChild(geoNode, "PolygonVertexIndex");
                if (vertsNode == null || indicesNode == null) return null;
                if (vertsNode.Properties.Count == 0 || indicesNode.Properties.Count == 0) return null;

                var rawVerts = GetArrayData<double>(vertsNode.Properties[0]);
                var rawIndices = GetArrayData<int>(indicesNode.Properties[0]);
                if (rawVerts == null || rawIndices == null) return null;

                FbxNode fnNormals = null, fnTexcoords = null;
                foreach (var child in geoNode.Children)
                {
                    switch (child.Name)
                    {
                        case "LayerElementNormal": fnNormals = child; break;
                        case "LayerElementUV": fnTexcoords = child; break;
                    }
                }

                double[] rawNormals = null;
                int[] normalIndices = null;
                if (fnNormals != null)
                {
                    var nNode = FindChild(fnNormals, "Normals");
                    var niNode = FindChild(fnNormals, "NormalIndex");
                    if (nNode != null && nNode.Properties.Count > 0)
                        rawNormals = GetArrayData<double>(nNode.Properties[0]);
                    if (niNode != null && niNode.Properties.Count > 0)
                        normalIndices = GetArrayData<int>(niNode.Properties[0]);
                }

                double[] rawUVs = null;
                int[] uvIndices = null;
                if (fnTexcoords != null)
                {
                    var uvNode = FindChild(fnTexcoords, "UV");
                    var uviNode = FindChild(fnTexcoords, "UVIndex");
                    if (uvNode != null && uvNode.Properties.Count > 0)
                        rawUVs = GetArrayData<double>(uvNode.Properties[0]);
                    if (uviNode != null && uviNode.Properties.Count > 0)
                        uvIndices = GetArrayData<int>(uviNode.Properties[0]);
                }

                var vertList = new List<Vector3>();
                var normalList = new List<Vector3>();
                var texcoordList = new List<Vector2>();
                var indexList = new List<int>();
                var polyVerts = new List<PolyVert>();

                foreach (int fnIndex in rawIndices)
                {
                    int absIdx = (fnIndex < 0) ? (-fnIndex - 1) : fnIndex;
                    var vpos = GetVector3(rawVerts, absIdx);
                    polyVerts.Add(new PolyVert { Position = vpos, AbsoluteIndex = absIdx });

                    if (fnIndex < 0)
                    {
                        if (polyVerts.Count >= 3)
                        {
                            var v0 = polyVerts[0];
                            for (int vi = 2; vi < polyVerts.Count; vi++)
                            {
                                var v1 = polyVerts[vi - 1];
                                var v2 = polyVerts[vi];
                                AddVertex(v0, rawNormals, normalIndices, rawUVs, uvIndices, vertList, normalList, texcoordList, indexList);
                                AddVertex(v1, rawNormals, normalIndices, rawUVs, uvIndices, vertList, normalList, texcoordList, indexList);
                                AddVertex(v2, rawNormals, normalIndices, rawUVs, uvIndices, vertList, normalList, texcoordList, indexList);
                            }
                        }
                        polyVerts.Clear();
                    }
                }

                if (vertList.Count == 0) return null;

                var mesh = new MeshData
                {
                    Name = name,
                    Vertices = vertList.ToArray(),
                    Normals = normalList.Count > 0 ? normalList.ToArray() : GenerateNormals(vertList, indexList),
                    Texcoords = texcoordList.Count > 0 ? texcoordList.ToArray() : null,
                    Indices = indexList.ToArray()
                };

                var center = Vector3.Zero;
                foreach (var v in mesh.Vertices) center += v;
                center /= mesh.Vertices.Length;

                float maxRad = 0;
                foreach (var v in mesh.Vertices)
                {
                    float d = (v - center).Length();
                    if (d > maxRad) maxRad = d;
                }
                mesh.BoundingCenter = center;
                mesh.BoundingRadius = maxRad;

                return mesh;
            }

            private static void AddVertex(PolyVert pv,
                double[] rawNormals, int[] normalIndices,
                double[] rawUVs, int[] uvIndices,
                List<Vector3> verts, List<Vector3> normals, List<Vector2> texcoords, List<int> indices)
            {
                var pos = pv.Position;
                var norm = pv.AbsoluteIndex >= 0 ? GetNormal(rawNormals, normalIndices, pv.AbsoluteIndex) : Vector3.UnitY;
                var tc = pv.AbsoluteIndex >= 0 ? GetTexcoord(rawUVs, uvIndices, pv.AbsoluteIndex) : Vector2.Zero;

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

            private static Vector3 GetVector3(double[] arr, int index)
            {
                int i = index * 3;
                if (i < 0 || i + 2 >= arr.Length) return Vector3.Zero;
                return new Vector3((float)arr[i], (float)arr[i + 1], (float)arr[i + 2]);
            }

            private static Vector3 GetNormal(double[] rawNormals, int[] normalIndices, int index)
            {
                if (rawNormals == null) return Vector3.UnitY;
                int ai = index;
                if (normalIndices != null && index >= 0 && index < normalIndices.Length)
                    ai = normalIndices[index];
                return GetVector3(rawNormals, ai);
            }

            private static Vector2 GetTexcoord(double[] rawUVs, int[] uvIndices, int index)
            {
                if (rawUVs == null) return Vector2.Zero;
                int ai = index;
                if (uvIndices != null && index >= 0 && index < uvIndices.Length)
                    ai = uvIndices[index];
                int i = ai * 2;
                if (i < 0 || i + 1 >= rawUVs.Length) return Vector2.Zero;
                return new Vector2((float)rawUVs[i], (float)rawUVs[i + 1]);
            }

            private static Vector3[] GenerateNormals(List<Vector3> vertices, List<int> indices)
            {
                var normals = new Vector3[vertices.Count];
                int triCount = (indices.Count / 3) * 3;
                for (int i = 0; i < triCount; i += 3)
                {
                    int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
                    if (i0 < 0 || i0 >= vertices.Count || i1 < 0 || i1 >= vertices.Count || i2 < 0 || i2 >= vertices.Count)
                        continue;
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
        }

        private struct PolyVert
        {
            public Vector3 Position;
            public int AbsoluteIndex;
        }
    }
}
