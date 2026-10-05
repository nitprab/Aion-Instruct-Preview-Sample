# AcquireQnnEp

`AcquireQnnEp` is an optional legacy ARM64 diagnostic utility that uses the Windows
App SDK 1.8 Windows ML catalog. It is not part of `Bootstrap.ps1`: the framework's bundled
WinML catalog acquires the compatible QNN EP 2 when the model first loads.
The older catalog can report QNN ready after staging an EP that does not meet this SDK's
`2.2480.49` floor. Do not use this utility's success as proof that the current SDK is
provisioned; use the SDK model-creation path instead.

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

Runtime activation uses the `8000.836.2153.0` GA floor. The framework itself uses
Windows App Runtime 2 for imaging and content-safety types; the separate 1.8 runtime
is needed only if you run this legacy utility directly.

For default public restores, the project includes the NuGet.org v2 endpoint as a
project-local fallback because these exact 1.8 packages may not be visible through
NuGet.org's v3 registration index. Providing `-p:RestoreConfigFile=<config>` uses
only the sources in that configuration.

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
