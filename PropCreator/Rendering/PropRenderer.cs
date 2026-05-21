using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;

namespace PropCreator
{
    public class PropRenderer : IDisposable
    {
        private Control targetControl;
        private Device device;
        private SwapChain swapChain;
        private Texture2D backBuffer;
        private RenderTargetView renderTargetView;
        private Texture2D depthBuffer;
        private DepthStencilView depthStencilView;

        private VertexShader vertexShader;
        private PixelShader pixelShader;
        private InputLayout inputLayout;
        private Buffer constantBuffer;

        private Buffer vertexBuffer;
        private Buffer indexBuffer;
        private int indexCount;

        private volatile bool running;
        private volatile bool resizing;
        private Thread renderThread;

        private Matrix viewMatrix;
        private Matrix projMatrix;
        private Matrix worldMatrix;
        private float rotationX;
        private float rotationY;
        private float zoomDistance = 5.0f;

        private Vector3 modelCenter;
        private float modelRadius = 1.0f;

        private System.Drawing.Point lastMousePos;
        private bool mouseDown;
        private bool mouseRightDown;

        private int panelWidth;
        private int panelHeight;

        private const string VertexShaderCode = @"
cbuffer ConstantBuffer : register(b0)
{
    float4x4 WorldViewProj;
    float4x4 World;
    float4 LightDir;
    float4 CameraPos;
};

struct VS_INPUT
{
    float3 Position : POSITION;
    float3 Normal : NORMAL;
};

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float3 WorldNormal : NORMAL;
    float3 WorldPos : TEXCOORD0;
};

PS_INPUT VS(VS_INPUT input)
{
    PS_INPUT output;
    float4 worldPos = mul(float4(input.Position, 1.0f), World);
    output.Position = mul(worldPos, WorldViewProj);
    output.WorldNormal = mul(input.Normal, (float3x3)World);
    output.WorldPos = worldPos.xyz;
    return output;
}
";

        private const string PixelShaderCode = @"
cbuffer ConstantBuffer : register(b0)
{
    float4x4 WorldViewProj;
    float4x4 World;
    float4 LightDir;
    float4 CameraPos;
};

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float3 WorldNormal : NORMAL;
    float3 WorldPos : TEXCOORD0;
};

float4 PS(PS_INPUT input) : SV_Target
{
    float3 N = normalize(input.WorldNormal);
    float3 L = normalize(-LightDir.xyz);
    float3 V = normalize(CameraPos.xyz - input.WorldPos);
    float3 H = normalize(L + V);

    float ambient = 0.3f;
    float diffuse = max(dot(N, L), 0.0f);
    float specular = pow(max(dot(N, H), 0.0f), 32.0f);

    float3 color = float3(0.7f, 0.7f, 0.7f) * (ambient + diffuse * 0.7f) + float3(1.0f, 1.0f, 1.0f) * specular * 0.3f;
    return float4(color, 1.0f);
}
";

        private struct ConstantBufferData
        {
            public Matrix WorldViewProj;
            public Matrix World;
            public Vector4 LightDir;
            public Vector4 CameraPos;

            public ConstantBufferData(Matrix wvp, Matrix w, Vector3 lightDir, Vector3 cameraPos)
            {
                WorldViewProj = Matrix.Transpose(wvp);
                World = Matrix.Transpose(w);
                LightDir = new Vector4(lightDir, 0);
                CameraPos = new Vector4(cameraPos, 1);
            }
        }

        public PropRenderer(Control control)
        {
            targetControl = control;
            panelWidth = control.ClientSize.Width;
            panelHeight = control.ClientSize.Height;
            control.Resize += Control_Resize;
            control.MouseDown += Control_MouseDown;
            control.MouseUp += Control_MouseUp;
            control.MouseMove += Control_MouseMove;
            control.MouseWheel += Control_MouseWheel;

            InitDevice();
            InitShaders();
            StartRenderLoop();
        }

        private SampleDescription sampleDesc;

        private void InitDevice()
        {
            var desc = new SwapChainDescription
            {
                BufferCount = 2,
                ModeDescription = new ModeDescription(panelWidth, panelHeight,
                    new Rational(60, 1), Format.R8G8B8A8_UNorm),
                IsWindowed = true,
                OutputHandle = targetControl.Handle,
                SampleDescription = new SampleDescription(4, 0),
                SwapEffect = SwapEffect.Discard,
                Usage = Usage.RenderTargetOutput
            };

            try
            {
                Device.CreateWithSwapChain(DriverType.Hardware, DeviceCreationFlags.None,
                    new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1 },
                    desc, out device, out swapChain);
            }
            catch
            {
                desc.SampleDescription = new SampleDescription(1, 0);
                Device.CreateWithSwapChain(DriverType.Hardware, DeviceCreationFlags.None,
                    new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1 },
                    desc, out device, out swapChain);
            }

            sampleDesc = desc.SampleDescription;
            CreateRenderTargets();
        }

        private void CreateRenderTargets()
        {
            backBuffer?.Dispose();
            renderTargetView?.Dispose();
            depthBuffer?.Dispose();
            depthStencilView?.Dispose();

            backBuffer = Texture2D.FromSwapChain<Texture2D>(swapChain, 0);
            renderTargetView = new RenderTargetView(device, backBuffer);

            depthBuffer = new Texture2D(device, new Texture2DDescription
            {
                Format = Format.D32_Float,
                ArraySize = 1,
                MipLevels = 1,
                Width = Math.Max(1, panelWidth),
                Height = Math.Max(1, panelHeight),
                SampleDescription = sampleDesc,
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.DepthStencil,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.None
            });

            depthStencilView = new DepthStencilView(device, depthBuffer);
        }

        private void InitShaders()
        {
            var vsByteCode = ShaderBytecode.Compile(VertexShaderCode, "VS", "vs_4_0");
            vertexShader = new VertexShader(device, vsByteCode);

            var psByteCode = ShaderBytecode.Compile(PixelShaderCode, "PS", "ps_4_0");
            pixelShader = new PixelShader(device, psByteCode);

            inputLayout = new InputLayout(device, vsByteCode, new[]
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElement("NORMAL", 0, Format.R32G32B32_Float, 12, 0)
            });

            vsByteCode.Dispose();
            psByteCode.Dispose();

            constantBuffer = new Buffer(device, Utilities.SizeOf<ConstantBufferData>(),
                ResourceUsage.Default, BindFlags.ConstantBuffer,
                CpuAccessFlags.None, ResourceOptionFlags.None, 0);
        }

        public void LoadMeshes(List<MeshData> meshes)
        {
            if (meshes == null || meshes.Count == 0) return;

            // Compute combined bounding
            Vector3 center = Vector3.Zero;
            float radius = 0;

            foreach (var mesh in meshes)
            {
                center += mesh.BoundingCenter;
                if (mesh.BoundingRadius > radius) radius = mesh.BoundingRadius;
            }
            center /= meshes.Count;
            modelCenter = center;
            modelRadius = Math.Max(radius, 0.01f);

            zoomDistance = modelRadius * 3.0f;

            // Flatten all meshes into one buffer
            var vertList = new List<VertexPosNormal>();
            var idxList = new List<int>();
            int baseIndex = 0;

            foreach (var mesh in meshes)
            {
                for (int i = 0; i < mesh.Vertices.Length; i++)
                {
                    vertList.Add(new VertexPosNormal
                    {
                        Position = mesh.Vertices[i],
                        Normal = (mesh.Normals != null && i < mesh.Normals.Length) ? mesh.Normals[i] : Vector3.UnitY
                    });
                }
                for (int i = 0; i < mesh.Indices.Length; i++)
                {
                    idxList.Add(mesh.Indices[i] + baseIndex);
                }
                baseIndex += mesh.Vertices.Length;
            }

            var vertices = vertList.ToArray();
            var indices = idxList.ToArray();
            indexCount = indices.Length;

            vertexBuffer?.Dispose();
            indexBuffer?.Dispose();

            vertexBuffer = Buffer.Create(device, BindFlags.VertexBuffer, vertices);
            indexBuffer = Buffer.Create(device, BindFlags.IndexBuffer, indices);
        }

        private void StartRenderLoop()
        {
            running = true;
            renderThread = new Thread(RenderLoop);
            renderThread.Start();
        }

        private void RenderLoop()
        {
            var rasterState = new RasterizerState(device, new RasterizerStateDescription
            {
                FillMode = FillMode.Solid,
                CullMode = CullMode.Back,
                IsFrontCounterClockwise = false,
                IsDepthClipEnabled = true
            });

            var depthState = new DepthStencilState(device, new DepthStencilStateDescription
            {
                IsDepthEnabled = true,
                DepthWriteMask = DepthWriteMask.All,
                DepthComparison = Comparison.Less
            });

            while (running)
            {
                if (targetControl.IsDisposed)
                    break;

                var form = targetControl.FindForm();
                if (form == null || form.IsDisposed)
                    break;

                if (form.WindowState == FormWindowState.Minimized)
                {
                    Thread.Sleep(10);
                    continue;
                }

                if (resizing)
                {
                    swapChain.Present(1, PresentFlags.None);
                    Thread.Sleep(1);
                    continue;
                }

                try
                {
                    UpdateCamera();
                    Render(rasterState, depthState);
                    swapChain.Present(1, PresentFlags.None);
                }
                catch
                {
                    Thread.Sleep(10);
                }
            }

            rasterState?.Dispose();
            depthState?.Dispose();
        }

        private void UpdateCamera()
        {
            float aspect = (panelHeight > 0) ? (float)panelWidth / panelHeight : 1f;
            projMatrix = Matrix.PerspectiveFovLH(0.785f, aspect, 0.01f, 1000f);

            var camPos = new Vector3(0, 0, -zoomDistance);
            var rotX = Matrix.RotationX(rotationX);
            var rotY = Matrix.RotationY(rotationY);
            var camWorld = rotX * rotY;
            camPos = Vector3.TransformCoordinate(camPos, camWorld);
            camPos += modelCenter;

            var lookAt = modelCenter;
            var up = Vector3.UnitY;
            viewMatrix = Matrix.LookAtLH(camPos, lookAt, up);

            cameraPosition = camPos;
        }

        private Vector3 cameraPosition;

        private void Render(RasterizerState rasterState, DepthStencilState depthState)
        {
            var context = device.ImmediateContext;

            context.OutputMerger.SetRenderTargets(depthStencilView, renderTargetView);
            context.Rasterizer.SetViewport(new Viewport(0, 0, panelWidth, panelHeight));
            context.Rasterizer.State = rasterState;
            context.OutputMerger.SetDepthStencilState(depthState);

            context.ClearRenderTargetView(renderTargetView, new Color4(0.12f, 0.12f, 0.16f, 1.0f));
            context.ClearDepthStencilView(depthStencilView, DepthStencilClearFlags.Depth, 1.0f, 0);

            if (vertexBuffer == null || indexCount == 0)
                return;

            worldMatrix = Matrix.Identity;

            var wvp = worldMatrix * viewMatrix * projMatrix;
            var cbData = new ConstantBufferData(wvp, worldMatrix, new Vector3(0.5f, -1.0f, 0.3f), cameraPosition);
            context.UpdateSubresource(ref cbData, constantBuffer);

            context.VertexShader.Set(vertexShader);
            context.VertexShader.SetConstantBuffer(0, constantBuffer);
            context.PixelShader.Set(pixelShader);
            context.PixelShader.SetConstantBuffer(0, constantBuffer);

            context.InputAssembler.InputLayout = inputLayout;
            context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
            context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(vertexBuffer, Utilities.SizeOf<VertexPosNormal>(), 0));
            context.InputAssembler.SetIndexBuffer(indexBuffer, Format.R32_UInt, 0);

            context.DrawIndexed(indexCount, 0, 0);
        }

        public void Resize(int width, int height)
        {
            if (width <= 0 || height <= 0) return;

            resizing = true;
            renderTargetView?.Dispose();
            backBuffer?.Dispose();
            depthStencilView?.Dispose();
            depthBuffer?.Dispose();

            swapChain.ResizeBuffers(2, width, height, Format.Unknown, SwapChainFlags.None);

            panelWidth = width;
            panelHeight = height;
            CreateRenderTargets();
            resizing = false;
        }

        private void Control_Resize(object sender, EventArgs e)
        {
            Resize(targetControl.ClientSize.Width, targetControl.ClientSize.Height);
        }

        private void Control_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                mouseDown = true;
                lastMousePos = e.Location;
            }
            else if (e.Button == MouseButtons.Right)
            {
                mouseRightDown = true;
                lastMousePos = e.Location;
            }
        }

        private void Control_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) mouseDown = false;
            if (e.Button == MouseButtons.Right) mouseRightDown = false;
        }

        private void Control_MouseMove(object sender, MouseEventArgs e)
        {
            if (mouseDown)
            {
                int dx = e.X - lastMousePos.X;
                int dy = e.Y - lastMousePos.Y;
                rotationY += dx * 0.005f;
                rotationX += dy * 0.005f;
                rotationX = Math.Max(-1.5f, Math.Min(1.5f, rotationX));
            }
            if (mouseRightDown)
            {
                int dx = e.X - lastMousePos.X;
                int dy = e.Y - lastMousePos.Y;
                var offset = new Vector3(-dx * 0.01f * modelRadius, dy * 0.01f * modelRadius, 0);
                modelCenter += offset;
            }
            lastMousePos = e.Location;
        }

        private void Control_MouseWheel(object sender, MouseEventArgs e)
        {
            zoomDistance -= e.Delta * 0.001f * modelRadius;
            zoomDistance = Math.Max(modelRadius * 0.5f, Math.Min(modelRadius * 20.0f, zoomDistance));
        }

        public void Dispose()
        {
            running = false;
            renderThread?.Join(500);

            vertexBuffer?.Dispose();
            indexBuffer?.Dispose();
            constantBuffer?.Dispose();
            inputLayout?.Dispose();
            vertexShader?.Dispose();
            pixelShader?.Dispose();
            renderTargetView?.Dispose();
            backBuffer?.Dispose();
            depthStencilView?.Dispose();
            depthBuffer?.Dispose();
            swapChain?.Dispose();
            device?.Dispose();
        }

        private struct VertexPosNormal
        {
            public Vector3 Position;
            public Vector3 Normal;
        }
    }
}
