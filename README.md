# PropCreator

A lightweight Windows desktop application for importing and viewing FBX 3D models with real-time DirectX 11 rendering.

## Features

- **FBX Import** — Open binary FBX files via `File > Import` (Ctrl+I). Supports FBX versions 6xxx through 8xxx.
- **Real-time 3D Rendering** — DirectX 11 renderer with Phong lighting (ambient + diffuse + specular) and a movable directional light.
- **Camera Controls**
  - **Left-click + drag** — Orbit around the model
  - **Right-click + drag** — Pan
  - **Mouse wheel** — Zoom in/out
- **Multi-threaded** — FBX parsing runs on a background thread; rendering runs on its own dedicated loop at ~60 FPS.
- **Model Processing** — Automatic polygon triangulation (including concave), vertex deduplication, normal generation (from cross-product when missing), and bounding sphere computation.

## Requirements

- Windows OS (DirectX 11, Windows Forms)
- .NET Framework 4.8
- Visual Studio 2022 (or compatible MSBuild toolchain)

## Build & Run

```bash
# Restore NuGet packages and build
msbuild PropCreator.sln /p:Configuration=Debug

# Run
bin/Debug/PropCreator.exe
```

## Dependencies

| Package | Version | Purpose |
|---|---|---|
| SharpDX | 4.2.0 | DirectX 11 interop |
| SharpDX.D3DCompiler | 4.2.0 | HLSL shader compilation |
| SharpDX.Direct3D11 | 4.2.0 | Direct3D 11 API |
| SharpDX.DXGI | 4.2.0 | Swap chain & display |
| SharpDX.Mathematics | 4.2.0 | Vector, matrix, math types |

## Project Structure

```
PropCreator/
├── PropCreator.sln          # Visual Studio 2022 solution
└── PropCreator/
    ├── PropCreator.csproj   # .NET Framework 4.8 project
    ├── Program.cs           # Application entry point (STAThread)
    ├── MainForm.cs          # Main window, UI events, MeshData model
    ├── MainForm.Designer.cs # WinForms designer layout
    ├── FbxParser.cs         # Binary FBX parser
    └── Rendering/
        └── PropRenderer.cs  # DirectX 11 renderer
```

## Architecture

1. **Program.cs** launches the Windows Forms main form.
2. **MainForm** hosts a menu strip, status bar, and a viewport panel.
3. On "Import", **FbxParser** reads the binary FBX tree on a background thread and extracts mesh geometry (vertices, normals, UVs, indices).
4. Extracted **MeshData** is passed to **PropRenderer**, which builds vertex/index buffers and renders them with HLSL shaders.
5. An independent render loop drives the viewport at ~60 FPS, handling camera input from the mouse.

## License

[MIT](LICENSE)
