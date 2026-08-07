# AcquireQnnEp

`AcquireQnnEp` is an unpackaged ARM64 C# console utility that uses the Windows
App SDK 1.8 Windows ML catalog to locate the Qualcomm QNN execution provider,
download or prepare it when needed, and register it for the current process.

## Prerequisites

- Windows 11 on an ARM64 Snapdragon device
- .NET 9 SDK
- Windows App Runtime 1.8 version `8000.836.2153.0` or newer
- Network access when QNN components need to be downloaded or updated

The project uses the public `Microsoft.WindowsAppSDK.ML` 1.8.2197 package,
which contains the managed Windows ML projection and runtime metadata, together
with its matching `Microsoft.WindowsAppSDK.Runtime` 1.8.260508005 component
set. The utility explicitly loads the installed Windows App Runtime 1.8 package
before activating the Windows ML catalog; the Windows App SDK automatic
bootstrap initializer is disabled.

Runtime activation uses the `8000.836.2153.0` GA floor also required by
`Microsoft.AionInstructPreview.Framework.1.0`. Although the build-time Runtime
NuGet identifies its own AppX build as `8000.859.21.0`, the Aion Instruct SDK
pins the GA floor because the ORT/QNN generation remains compatible within the
Windows App Runtime 1.8 line.

The project includes the NuGet.org v2 endpoint as a project-local fallback
because these exact 1.8 packages are downloadable but are not currently visible
through NuGet.org's v3 registration index.

## Build

From the repository root:

```powershell
dotnet build .\tools\AcquireQnnEp\AcquireQnnEp.csproj -c Release -p:Platform=ARM64
```

## Run

```powershell
dotnet run --project .\tools\AcquireQnnEp\AcquireQnnEp.csproj -c Release -p:Platform=ARM64
```

Acquisition is the default behavior. `EnsureReadyAsync()` is safe to call when
QNN is already ready, so repeated runs do not require a separate readiness
switch.

During `TryRegister()`, the Windows ML stack can emit known cpuinfo and ONNX
Runtime warnings directly to native stderr when an older cpuinfo build does not
recognize a Snapdragon model string. The utility suppresses native stderr only
for that registration call and restores it immediately afterward; acquisition
and registration failures are still reported normally.

Use `--help`, `-h`, or `/?` to print usage without loading the Windows ML
catalog.

## Exit codes

| Code | Meaning |
| ---: | --- |
| 0 | QNN is ready and registered for the process |
| 1 | Invalid command-line arguments |
| 2 | Windows App Runtime or catalog operation failed |
| 3 | QNN was not found |
| 4 | `EnsureReadyAsync()` failed |
| 5 | `TryRegister()` failed |
