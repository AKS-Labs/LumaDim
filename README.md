# LumaDim

Windows 11 WinUI 3 starter for a tray-based dimming utility.

## Build automatically on GitHub

1. Create a GitHub repository and push this project to its `main` or `master` branch.
2. Open the repository's **Actions** tab. Enable Actions if GitHub asks.
3. Each push to `main` or `master` runs `.github/workflows/build.yml`; you can also start it using **Run workflow**.
4. Open the completed workflow run and download the **LumaDim-windows-x64** artifact.

The artifact is a self-contained Windows x64 publish output. It includes the EXE and any companion files emitted by .NET; download and extract the artifact ZIP before running. GitHub Actions artifacts are retained for 30 days by this workflow.

## Local build (optional)

Install Visual Studio 2022 with WinUI 3 / Windows App SDK support and .NET 8 SDK, then run:

```powershell
dotnet publish .\LumaDim\LumaDim.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:WindowsPackageType=None -o .\publish
```

## Prototype limitations

- Overlay geometry is currently a fixed 1920×1080 primary-display prototype, not production-ready multi-monitor support.
- Hardware brightness relies on WMI; driver and display support varies.
- Startup writes the executable path to the current user's Run registry key. Publish to a stable folder before enabling startup.
- This is unsigned source code, not a tested installer. Validate display behavior, startup, brightness restoration, and tray behavior before daily use.
- WinUI 3 is more substantial than a minimal native Win32 utility.
