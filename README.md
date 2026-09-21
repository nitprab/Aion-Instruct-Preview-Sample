# Aion Instruct Preview Chat

A WinUI 3 desktop chat app that runs against the **AionInstructPreview** on-device language model on
Copilot+ PCs. Text chunks stream into the bubble as they're generated; app-observed first-update
latency and updates/sec are shown under each reply. A progress update is not an exact tokenizer token.

Two tabs, two APIs: **Chat** drives `AionInstructPreview.Text` (multi-turn conversation, optional system
prompt), and **Describe image** drives `AionInstructPreview.Imaging`. The preview follows the
corresponding Windows App SDK public contracts for its supported members; it does not expose the
entire in-box API.

> **Preview notes**
>
> - **Platform support.** This preview ships **ARM64** (Snapdragon, QNN NPU) and **x64** (Intel, OpenVINO NPU) builds. The ARM64/QNN path is validated on hardware; the **x64/OpenVINO path has not yet been validated on an Intel NPU** — please file an issue with your results if you try it. AMD (VitisAI) is not yet supported.
> - **Performance.** The first-update latency and updates/sec shown in the app are **preliminary,
>   app-observed streaming measurements — not tokenizer throughput or final model performance.**
>   Runtime and model optimizations are underway.
> - **Moderation follow-up.** The moderation behavior described below requires the matching
>   follow-up SDK metadata and runtime, not just the previously published packages. Release
>   packaging is coordinated separately; these changes do not establish RAI qualification.

## Quickstart

You need a **[Copilot+ PC](https://learn.microsoft.com/windows/ai/npu-devices/)** running **Windows 11**, plus a few tools:

- **.NET 9 SDK** — `winget install --id Microsoft.DotNet.SDK.9`
- **Git** — `winget install --id Git.Git` (or download the repo ZIP from the green **Code** button). `Bootstrap.ps1` downloads the signed release straight from this repo's **public** GitHub releases over HTTPS.
- **Developer Mode** on — Settings → Privacy & security → For developers → Developer Mode → On (`Bootstrap.ps1` enables it for you if it isn't already)

Then clone and run the bootstrap script:

```powershell
git clone https://github.com/microsoft/Aion-Instruct-Preview-Sample
cd Aion-Instruct-Preview-Sample
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\Bootstrap.ps1
```

`Bootstrap.ps1` downloads and installs the Aion Instruct Preview model framework, prepares the QNN execution provider on ARM64, then builds and launches the chat app. First launch takes ~3-5 minutes to compile the model for your NPU; every launch after is near-instant.

> `Bootstrap.ps1` downloads the signed release from this repo's public GitHub releases over plain HTTPS. For the manual steps, Visual Studio, or if you hit a snag, see [Prerequisites](#prerequisites) and [Quickstart details](#quickstart-details).
>
> Want to use Aion Instruct Preview in **your own** app instead? See [Use Aion Instruct Preview in your own app](#use-aion-instruct-preview-in-your-own-app).

---

## Prerequisites

- **Windows 11** on an **ARM64 Copilot+ PC** (Snapdragon NPU) — runs on QNN.

  The SDK picks the EP automatically via WinML's `ExecutionProviderCatalog` — whichever NPU EP the OS reports as `Certified + Ready` wins. A certified NPU EP is required — there is no CPU fallback.

  > **x64 is built but not yet hardware-validated.** ARM64/Snapdragon (QNN) is the validated path. x64 produces an Intel/OpenVINO build that has not been run on an Intel NPU yet; AMD (VitisAI) is not supported.

**Build-time vs run-time — these are different things.** A common source of confusion (see [Troubleshooting](#troubleshooting)) is assuming a build error means a *runtime* component is missing. It usually doesn't. Keep the two lists straight:

#### To BUILD the sample

- **.NET 9 SDK** (`winget install --id Microsoft.DotNet.SDK.9`).
- **Developer Mode** enabled (Settings → Privacy & security → For developers → Developer Mode → On). `dotnet run` registers the build output as a loose-layout development package, which requires Developer Mode. `Bootstrap.ps1` enables it for you (one UAC prompt) if it isn't already.

That's it — no Visual Studio and no registry-installed Windows SDK are required. All other build-time dependencies (Windows App SDK, SDK build tools, CsWinRT, the Windows metadata/ref pack, and the MSIX loose-layout tooling) come from NuGet and are restored automatically.

**Prefer Visual Studio?** Use **Visual Studio 2026 (18.x)** — open `AionInstructPreview.Chat.sln`, pick **ARM64** and the **AionInstructPreview.Chat (MSIX)** launch profile in the Run dropdown, then press F5 to build, deploy, and debug the packaged app. (The other profile, **AionInstructPreview.Chat**, is the `commandName: Project` profile that `dotnet run` uses from a terminal — don't pick it for F5; it launches the bare exe without package identity and crashes with `REGDB_E_CLASSNOTREG`.) Your VS 2026 instance needs the components listed in [Visual Studio 2026 setup](#visual-studio-2026-setup) below. Visual Studio **2022 (17.x) is not supported** for F5 of this app — its MSIX single-project launcher doesn't bind the Windows App SDK 2.0 debug page and reports *"the project doesn't know how to run the profile … command 'MsixPackage'"*. If you'd rather not deal with VS at all, `dotnet run` from a terminal needs only the .NET 9 SDK + Developer Mode.

#### To RUN the sample

- **Windows App SDK 2.0 runtime** (WindowsAppRuntime 2) installed:

  ```powershell
  winget install --id Microsoft.WindowsAppRuntime.2.0
  ```

  (or grab the installer from <https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads>.) This runtime is needed only to **run**, not to build.
- **Inference runtime:** WinML and ORT are bundled in the Aion framework. The sample launchers do
  not require Windows App Runtime 1.8. The separate legacy QNN acquisition helper still uses
  WAR 1.8; that is not a prerequisite for launching an already-provisioned sample.
- **.NET 9 Desktop Runtime** (`winget install --id Microsoft.DotNet.DesktopRuntime.9`, or the `windowsdesktop-runtime-9.0.x-win-<arch>.exe` installer). The .NET 9 SDK above already includes this, so you only need it separately on a run-only machine that has no SDK.
- The Aion Instruct Preview **framework MSIX** installed for your arch — `Bootstrap.ps1` handles this (see [Quickstart](#quickstart)).

#### For either path

- **Git** — `winget install --id Git.Git`. Used to clone the repo; you can also download the source ZIP from the green **Code** button instead. `Bootstrap.ps1` fetches the signed release from this repo's **public** GitHub releases over HTTPS.

#### Visual Studio 2026 setup

The F5 (build + deploy + debug) path is supported on **Visual Studio 2026 (18.x)** only. In the Visual Studio Installer, **Modify** your VS 2026 instance and make sure these are installed, then relaunch VS:

- **.NET desktop development** workload (`Microsoft.VisualStudio.Workload.ManagedDesktop`) — provides the .NET SDK so the project resolves `Microsoft.NET.Sdk`. Without it VS reports *"The SDK 'Microsoft.NET.Sdk' specified could not be found."*
- **Windows App SDK C# support** individual component (`Microsoft.VisualStudio.Component.WindowsAppSdkSupport.CSharp`).
- **MSIX Packaging Tools** component group (`Microsoft.VisualStudio.ComponentGroup.MSIX.Packaging`) — installs the single-project MSIX launcher that F5-deploys the packaged app. Without it the **(MSIX)** profile errors with *"the project doesn't know how to run the profile … command 'MsixPackage'."*

From an elevated PowerShell you can add all three in one shot (adjust `--installPath` to your VS 2026 instance):

```powershell
$setup = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\setup.exe"
& $setup modify --installPath "C:\Program Files\Microsoft Visual Studio\18\Enterprise" `
  --add Microsoft.VisualStudio.Workload.ManagedDesktop `
  --add Microsoft.VisualStudio.Component.WindowsAppSdkSupport.CSharp `
  --add Microsoft.VisualStudio.ComponentGroup.MSIX.Packaging `
  --includeRecommended --passive --norestart
```

Developer Mode must also be on (same as the terminal path). Then open `AionInstructPreview.Chat.sln`, make sure the **Solution Platform is ARM64** in the toolbar, pick the **AionInstructPreview.Chat (MSIX)** profile, and press F5.

> **"The project needs to be deployed. Please enable Deploy in the Configuration Manager."** This almost always means your selected **Solution Platform isn't ARM64**, not that Deploy is actually unchecked. The `.sln` enables Deploy for ARM64. Switch the toolbar Solution Platform to ARM64 and F5 again.

---

## Quickstart details

A few notes on the steps in the [Quickstart](#quickstart) above:

- **Where to clone.** Put the repo under your user profile (e.g. `C:\repos` or `%USERPROFILE%`) — **not** under `C:\Windows\System32`. See the [Troubleshooting](#troubleshooting) note on System32 for why an elevated prompt's default directory breaks the build.
- **`Set-ExecutionPolicy`.** Required on a machine with the default `Restricted` policy — otherwise `.\Bootstrap.ps1` fails with *"Bootstrap.ps1 cannot be loaded because running scripts is disabled on this system"*, even after a clean `git clone`. It's scoped to the current process, needs no admin, and reverts when you close the window.
- **What `Bootstrap.ps1` does.** Detects your arch, enables Developer Mode if needed, pulls the latest signed Aion Instruct Preview release from this repo's public GitHub releases over HTTPS, installs the framework MSIX, drops the SDK NuGet, installs the QNN execution provider on ARM64 when its AppX package is missing, then builds and launches AionInstructPreview.Chat via `dotnet run`. Re-runs are idempotent.
- **First launch.** Sits on **"Loading Aion Instruct Preview model…"** for ~3-5 minutes while the runtime compiles its QDQ ONNX models for the picked execution provider (QNN on Snapdragon NPU). This is a one-shot per device — every prompt after that is sub-second to first token on NPU.

---

## Manual install (what Bootstrap.ps1 does)

Skip this section unless you want to see the moving parts. Bashers using `Bootstrap.ps1` can ignore.

### 1. Install the Aion Instruct Preview release

Grab the latest signed release from <https://github.com/microsoft/Aion-Instruct-Preview-Sample/releases>. Two assets are attached:

| Asset | Who needs it |
|---|---|
| `AionInstructPreview.LanguageModel.Framework_<ver>_ARM64.msix` | ARM64 (Snapdragon) dev boxes |
| `AionInstructPreview.Text.Framework.<ver>.nupkg` | The SDK NuGet (arch-neutral) |

Both MSIXes are ESRP-signed (`CN=Microsoft Corporation`, Microsoft Windows EKU), so `Add-AppxPackage` accepts them directly — no dev-cert dance, no `TrustedPeople` import.

```powershell
# Install the framework MSIX (one time per machine).
Add-AppxPackage .\AionInstructPreview.LanguageModel.Framework_<ver>_ARM64.msix

# Verify it installed.
Get-AppxPackage Microsoft.AionInstructPreview.Framework.1.0
# Expected: IsFramework=True, Publisher=CN=Microsoft Corporation, ...

# Drop the SDK NuGet into this repo's local feed.
copy .\AionInstructPreview.Text.Framework.<ver>.nupkg .\nuget-local\
```

### 2. Build and run the sample

```powershell
# Build for ARM64 (Snapdragon). dotnet run builds the app, registers the loose
# layout as a development package (Developer Mode required), and launches it.
# No dev cert, no separate Add-AppxPackage step.
dotnet run --project AionInstructPreview.Chat.csproj --launch-profile "AionInstructPreview.Chat" -c Release -p:Platform=ARM64
```

Prefer Visual Studio? Open the solution in **Visual Studio 2026** (see [Visual Studio 2026 setup](#visual-studio-2026-setup) for the required components), pick the **(MSIX)** profile, and press F5 — same build-and-launch, with the debugger attached.

---

## Use Aion Instruct Preview in your own app

Aion Instruct Preview works from two kinds of consumer app. The API projection is identical for both — the same two `PackageReference`s (the SDK plus CsWinRT, see [Required PackageReferences](#required-packagereferences) below) — so the only real difference is **how your app acquires the runtime dependency on the framework package**:

- **[Packaged apps](#packaged-apps)** — WinUI 3 / WinAppSDK, shipped as an MSIX. The SDK's build hooks inject the framework `<PackageDependency>` into your manifest. This is what `AionInstructPreview.Chat` (this repo's root project) demonstrates.
- **[Unpackaged apps](#unpackaged-apps)** — plain desktop apps (e.g. WPF or a console app), no MSIX identity. Your app takes the framework dependency at runtime. See the [`unpackaged-wpf/`](unpackaged-wpf/) and [`unpackaged-console/`](unpackaged-console/) samples.

Both paths first need the SDK NuGet (next).

### Getting the SDK NuGet

`AionInstructPreview.Text.Framework` isn't on nuget.org yet — consume it from this repo's signed GitHub releases. Download `AionInstructPreview.Text.Framework.<ver>.nupkg` from <https://github.com/microsoft/Aion-Instruct-Preview-Sample/releases>, drop it into a local feed folder next to your csproj (this sample uses `./nuget-local/`), and point NuGet at that folder in a `nuget.config` alongside the csproj:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="aion-instruct-preview-local" value="./nuget-local" />
  </packageSources>
</configuration>
```

See [`nuget.config`](nuget.config) in this repo for the working version. Once the SDK NuGet lands on a public feed, swap the `aion-instruct-preview-local` entry for that feed's URL — the rest of your build pipeline doesn't change.

### Required PackageReferences

An external project needs **both** of these `PackageReference`s in its csproj:

```xml
<PackageReference Include="AionInstructPreview.Text.Framework" Version="1.0.1" />
<PackageReference Include="Microsoft.Windows.CsWinRT" Version="2.1.5" />
```

Pin the SDK version exactly rather than floating with `1.0.*`. The local feed is a folder you
populate by hand, so a wildcard silently resolves to whichever `.nupkg` happens to be sitting
there — an older one is missing `AionInstructPreview.Imaging.winmd` and fails with `CS0246
'ImageDescriptionGenerator' not found` rather than a version error. This sample requires
**1.0.1** for the updated text API contract, as well as the imaging metadata.

The CsWinRT reference is **not** optional and **not** implicit. CsWinRT's build targets — the ones that run the source generator turning `AionInstructPreview.Text.winmd` into C# — only import when `Microsoft.Windows.CsWinRT` is referenced **directly** by the project. Picking it up transitively (e.g. via the Windows App SDK) does **not** import those build targets, so the generator never runs and you get a wall of `CS0246 'Aion Instruct Preview' / 'LanguageModel' not found`. This sample's csproj already has both references, which is why copying our csproj "just works" — but a project you wire up from scratch must add the CsWinRT reference itself.

(This repo pins CsWinRT `2.1.5`; match the version your toolchain expects.)

### Packaged apps

With the feed and both [required PackageReferences](#required-packagereferences) in place, the SDK reference brings in:

| Surface | What it does |
|---|---|
| `AionInstructPreview.Text.Framework.props` (auto-imported) | Adds `AionInstructPreview.Text.winmd` to `$(CsWinRTInputs)`. CsWinRT projects the runtimeclasses into C# at build time. |
| `AionInstructPreview.Text.Framework.targets` (auto-imported) | Before pack, injects the framework dependency with `MinVersion="1.0.0.1"` or raises an older minimum. Publisher defaults to the Microsoft Corporation subject used by the signed framework. Override `<AionInstructPreviewFrameworkPublisher>` only for a differently signed framework. |

At runtime, Windows AppX resolves the framework dependency, loads `AionInstructPreview.Text.dll` out of the framework's deploy folder, and cross-package WinRT activation hands you the runtimeclasses.

**No fusion manifest. No sibling-DLL deployment. No manual `<PackageDependency>` declaration. No HKLM registration.**

The targets de-duplicate dependencies by name and raise older minimum versions. They also ensure
the Windows App Runtime 2 dependency required by the SDK's imaging and content-safety types.

#### Minimum integration checklist

1. Your app must be **packaged WinUI 3 / WinAppSDK** (i.e. ships as an MSIX with a `Package.appxmanifest`).
2. Add **both** [required PackageReferences](#required-packagereferences) (the SDK *and* `Microsoft.Windows.CsWinRT`) and the `nuget.config` from [Getting the SDK NuGet](#getting-the-sdk-nuget) above.
3. The consumer needs package identity. The simplest path is `dotnet run`, which registers the build output as a loose-layout development package (Developer Mode required) — no signing cert needed. To install a **signed** MSIX instead, sign it with a cert whose chain is trusted in `LocalMachine\TrustedPeople` (Visual Studio's auto-generated dev cert works).
4. Ensure the framework MSIX is installed on the target machine before launch.

### Unpackaged apps

Plain desktop apps with **no MSIX identity** can use Aion Instruct Preview too. The working sample is an unpackaged **WPF** app in [`unpackaged-wpf/`](unpackaged-wpf/):

```powershell
cd unpackaged-wpf
.\Run.ps1
```

WPF rather than WinUI 3 on purpose: WinUI 3 isn't viable unpackaged on this stack, and the packaged sample above already shows WinUI 3. WPF is the mainstream unpackaged Windows desktop UI framework and consumes the SDK through the same two [required PackageReferences](#required-packagereferences) (the SDK plus `Microsoft.Windows.CsWinRT`).

For a UI-free example, [`unpackaged-console/`](unpackaged-console/) is the same pattern in a plain console app — run it the same way (`cd unpackaged-console; .\Run.ps1`).

Those references do the API projection identically to the packaged app. Because there's no manifest, only the framework-dependency wiring differs:

| Concern | Packaged | Unpackaged |
|---|---|---|
| API projection (`using AionInstructPreview.Text;`) | `AionInstructPreview.Text.Framework.props` feeds CsWinRT | **identical** — same `PackageReference` |
| Framework dependency | injected into your `Package.appxmanifest` by `AionInstructPreview.Text.Framework.targets` at build | **no manifest, so the targets no-op** — taken at runtime via `TryCreatePackageDependency` / `AddPackageDependency` ([`FrameworkDependency.cs`](unpackaged-wpf/FrameworkDependency.cs)) |
| WinRT activation | cross-package activation via the package graph | **registration-free** — with the framework dir on the DLL search path, CsWinRT activates the runtimeclasses through the framework's `AionInstructPreview.Text.dll`. No SxS fusion manifest, no HKLM registration. |
| Execution provider | auto-selected by the SDK (the certified NPU EP — QNN) | **identical** — the SDK picks the EP internally |

So the only consumer code an unpackaged app adds over a packaged one is a single runtime call in [`FrameworkDependency.cs`](unpackaged-wpf/FrameworkDependency.cs), made once at startup (`App.OnStartup`) before the first activation:

```csharp
// App.xaml.cs -- the ONLY consumer code an unpackaged app adds over a packaged
// one. (A packaged app declares this as a manifest <PackageDependency> instead.)
protected override void OnStartup(StartupEventArgs e)
{
    FrameworkDependency.EnsureLoaded();   // must run before the first WinRT activation
    base.OnStartup(e);
}

// FrameworkDependency.EnsureLoaded() -- the Win32 dynamic-dependency APIs,
// callable from an unpackaged process on Windows 11. They add the framework
// package to this process's package graph + DLL search path, so WinRT activation
// resolves AionInstructPreview.Text through the framework's AionInstructPreview.Text.dll.
// The framework MSIX just has to match the process arch (ARM64 in this preview).
int arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
    ? 0x10    // Arm64
    : 0x4;    // X64  (see PackageDependencyProcessorArchitectures in appmodel.h)
TryCreatePackageDependency(
    /* user            */ IntPtr.Zero,
    /* packageFamily   */ "Microsoft.AionInstructPreview.Framework.1.0_8wekyb3d8bbwe",
    /* minVersion      */ (1UL << 48) | 1UL, // 1.0.0.1
    /* architectures   */ arch,
    /* lifetimeKind    */ 0,       // Process
    /* lifetimeArtifact*/ null,
    /* options         */ 0,
    out IntPtr depId);
AddPackageDependency(depId, /* rank */ 0, /* options */ 0, out _, out _);
```

From there, `LanguageModel.CreateAsync()` and the rest of the API are called exactly as in the packaged sample — see [`MainWindow.xaml.cs`](unpackaged-wpf/MainWindow.xaml.cs), which streams responses into the UI by marshaling the `Progress` callback onto the dispatcher thread.

Prerequisites are the same as the packaged path: the matching framework MSIX, SDK NuGet,
Windows App Runtime 2, and .NET SDK 9+. `Run.ps1` checks the framework minimum version and WAR 2
before building.

### Calling the API

```csharp
using AionInstructPreview.Text;

// Loads the model. Long-running on first launch (NPU compile), instant after.
using var model = await LanguageModel.CreateAsync();

using var context = model.CreateContext(); // reuse for multi-turn history

// Final-only display: a streamed result may still be blocked by a later check.
// Explicit options preserve this sample's previous sampling settings; null is rejected.
var options = new LanguageModelOptions { Temperature = 0.5f, TopP = 0.9f, TopK = 40 };
var op = model.GenerateResponseAsync(
    context, "Why are Aion Instruct Preview responses better than scones?", options);
var result = await op;

if (result.Status == LanguageModelResponseStatus.Complete)
{
    Console.WriteLine(result.Text);
}
else
{
    Console.WriteLine($"No final response is available ({result.Status}).");
}
```

**Streaming semantics.** The `Progress` callback's signature is `(asyncInfo, delta)`. The second
argument is the **latest text chunk** — the newly generated text since the last callback, not the
running total and not necessarily one tokenizer token. The first argument is the async operation
(usually ignored, `_`). The **full, accumulated response** comes from awaiting the operation —
`LanguageModelResponseResult.Text`. Append chunks as they arrive for a live typing effect, and read
`result.Text` only when the terminal status is `Complete`. The example above deliberately waits
for completion instead of displaying partial text.

This sample's [`AionInstructClient.cs`](AionInstructClient.cs) holds the session context and uses a
local `Stopwatch` to measure first-update latency, elapsed time to completion, and progress
updates/sec. The rate is `(updateCount - 1)` divided by the time from the first update to operation
completion, including completion overhead; it is not pure model decode throughput. No first-update
latency is reported when there are no updates, and no rate is reported with fewer than two updates.
[`Models/GenerationMetrics.cs`](Models/GenerationMetrics.cs) shares these conventions with image
description. [`ViewModels/ChatViewModel.cs`](ViewModels/ChatViewModel.cs) shows how to marshal the
background-thread Progress callback back to the UI thread via `DispatcherQueue` AND how to surface
`PromptLargerThanContext` as a "New conversation" affordance instead of a generic error. For the
unpackaged equivalent, [`unpackaged-wpf/MainWindow.xaml.cs`](unpackaged-wpf/MainWindow.xaml.cs) shows
the identical API driving a plain WPF window.

---

## API surface used

The `AionInstructPreview.Text` namespace follows the supported public contracts of
[`Microsoft.Windows.AI.Text`](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.ai.text?view=windows-app-sdk-2.0)
from Windows App SDK 2.0, including `IAsyncOperationWithProgress` streaming semantics. Use the
preview namespace for both `LanguageModel` and `LanguageModelOptions`; the preview implements a
subset, not every in-box member.

**Migrating to SDK 1.0.1:** replace `GenerateResponseAsync(context, prompt)` with
`GenerateResponseAsync(context, prompt, options)`. Options cannot be `null`. To preserve this
sample's previous behavior, use
`new LanguageModelOptions { Temperature = 0.5f, TopP = 0.9f, TopK = 40 }`.
Preview-only diagnostics have been removed: results no longer expose `TokenCount`,
`TimeToFirstToken`, or `DecodeDuration`, and the model no longer exposes `GetTokenCount`,
`MaxPromptTokenCount`, or `ContextLength`. Measure app-observed timing locally instead; callback
counts are progress updates, not exact token counts. Rebuild consumers with NuGet **1.0.1** and run
against framework **1.0.0.1**. This is a breaking preview contract change; compatibility with
previously built consumer binaries is not promised.

Cross-link the WinAppSDK reference for full member docs:

### [`LanguageModel`](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.ai.text.languagemodel?view=windows-app-sdk-2.0)

| Member | Sample usage |
|---|---|
| [`static CreateAsync()`](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.ai.text.languagemodel.createasync?view=windows-app-sdk-2.0) | `AionInstructClient.CreateAsync` calls this once at startup; long-running on first launch (NPU compile). |
| [`CreateContext()`](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.ai.text.languagemodel.createcontext?view=windows-app-sdk-2.0) | Opens a fresh conversation context. Called once on startup and again on "New conversation". |
| `CreateContext(String)` | Opens a context with a **system prompt** that steers every turn in that conversation. The sample's "System prompt" expander maps to this overload. A context is immutable once created, so a changed prompt only takes effect on the next `CreateContext` — which is why the UI applies it via "New conversation". |
| [`GenerateResponseAsync(LanguageModelContext, String, LanguageModelOptions)`](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.ai.text.languagemodel.generateresponseasync?view=windows-app-sdk-2.0) | Streaming generation rooted in the session context — every chat send hits this overload with explicit options preserving prior sampling settings. Progress delivers text chunks; awaiting the operation returns the final `LanguageModelResponseResult`. |
| [`GenerateResponseAsync(String)`](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.ai.text.languagemodel.generateresponseasync?view=windows-app-sdk-2.0) | Context-less single-shot variant; supported by Aion Instruct Preview, not used by this sample. |
| `GenerateResponseAsync(String, LanguageModelOptions)` | Context-less single-shot variant with explicit, non-null options. |
| [`Close()`](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.ai.text.languagemodel.close?view=windows-app-sdk-2.0) / `Dispose()` | Releases the model on window close. `IClosable.Close()` projects to `IDisposable.Dispose()` in C#. |

### [`LanguageModelContext`](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.ai.text.languagemodelcontext?view=windows-app-sdk-2.0)

Returned by `LanguageModel.CreateContext()`. Carries in-process conversation history across
multiple `GenerateResponseAsync` calls. The sample retains the same context after blocked turns,
following inbox behavior; it does not automatically start a new conversation or promise that a
failed turn left the context unchanged. `AionInstructClient.StartNewConversation` creates the new
context before disposing the old one, so a rejected system prompt preserves the existing context.

### [`LanguageModelResponseResult`](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.ai.text.languagemodelresponseresult?view=windows-app-sdk-2.0)

| Member | Sample usage |
|---|---|
| `Text` | Final accumulated response text. Display only when `Status` is `Complete`. |
| `Status` | `LanguageModelResponseStatus` — terminal state for the call. `ChatViewModel.SendAsync` branches on this. |

### `LanguageModelOptions`

`AionInstructPreview.Text.LanguageModelOptions` exposes only `Temperature`, `TopP`, `TopK`,
and `ContentFilterOptions`. It does not expose LoRA configuration. New options default to
`Temperature = 0.9f`, `TopP = 0.9f`, and `TopK = 40`. The sample explicitly sets `Temperature = 0.5f`
and retains `TopP = 0.9f` and `TopK = 40` to preserve its previous chat behavior. The prompt-only
overload also retains those previous settings. Passing `null` options is rejected.

`ContentFilterOptions` is constructed by default and enables moderation. The sample keeps the
default filters; it does not expose filter settings in the UI. See [Content moderation](#content-moderation).

### [`LanguageModelResponseStatus`](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.ai.text.languagemodelresponsestatus?view=windows-app-sdk-2.0)

The preview exposes these values (not every value in the inbox enum):

| Value | Number | What the sample does |
|---|---|---|
| `Complete` | 0 | Display the final response and metrics. |
| `InProgress` | 1 | Progress callbacks append text; this is not a successful terminal result. |
| `PromptLargerThanContext` | 3 | Clear partial output; WinUI offers "New conversation". |
| `PromptBlockedByContentModeration` | 4 | Clear partial output and explain that the prompt was blocked. |
| `ResponseBlockedByContentModeration` | 5 | Clear partial output and explain that the response was blocked. |
| `Error` | 6 | Clear partial output and report an operational failure, not a moderation decision. |

### Streaming semantics

Progress callbacks deliver **text chunks**, not the accumulated text or guaranteed individual
tokens. `ChatViewModel.SendAsync` appends each delta to the current Aion Instruct Preview message via
`DispatcherQueue.TryEnqueue`, which is what gives the typewriter-style streaming feel. Image
description uses the same delta contract.

### Content moderation

The moderation follow-up uses the text content moderation model (TCM), image content moderation
model (ICM), and inbox blocklists. Default options enable filtering for prompts, generated text,
images, and text extracted from images. Filters are not a guarantee of safe or accurate output.
`PromptMaxAllowedSeverityLevel` and `ResponseMaxAllowedSeverityLevel` configure text categories
separately; `ImageMaxAllowedSeverityLevel` configures image categories. The default maximum
allowed severity is `Low` per category: a higher classified severity is blocked. These settings
are policy thresholds, not a model-quality score; the sample does not relax them.
This sample keeps the default `Low` policy and has no filter-settings editor. Supported custom
severity settings differ between the text and image-description APIs; do not assume they are
interchangeable. Missing or null filter options,
including null nested severity objects, use `Low` defaults rather than disabling moderation.
Moderation and inbox blocklists are mandatory; there is no off flag or unfiltered fallback.
Text-only requests ignore image severity options. The SDK
snapshots policy before asynchronous work, so later mutations do not change an in-flight request.
Missing or failed moderation models or blocklist payloads fail the operation rather than allowing
unfiltered output. These checks run locally; they do not require cloud moderation or installation
of the inbox moderation MSIX packages.

WinUI and WPF keep already accepted partial text visible while generation is in progress. A later
check may still block the final result: on any terminal block or error, they clear that response's
partial text and ignore queued dispatcher updates. Earlier conversation entries remain. A typed
prompt or user-provided thumbnail may remain visible; its presence is not a moderation approval.
The console cannot retract text already printed to a terminal or redirected file. It labels a
blocked or failed result as having **no final response**, warns to disregard earlier streamed text,
and exits unsuccessfully. Use final-only display, as above, if that limitation is unacceptable.
Even accepted updates are not a guarantee about later checks or conversation-context changes.

Moderation blocks are distinct from runtime, model-loading, and filtering failures. Exception UI
uses a fixed message and HRESULT, never exception text that might contain rejected content.
`CreateContext(systemPrompt)` can itself reject a system prompt with `0x8A1F0202`; WinUI catches
this on both first send and "New conversation", so the user can revise it without losing the
previous context. A blocked turn does not trigger an automatic conversation reset.

These are integration and UX behaviors, not evidence of model-quality or RAI qualification.
Runtime validation requires the matching moderation models and framework on supported hardware.

---

## Image description

The **Describe image** tab uses `AionInstructPreview.Imaging`, which mirrors
[`Microsoft.Windows.AI.Imaging`](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.ai.imaging?view=windows-app-sdk-2.0).
As with the text API, porting to the inbox stack is a `using` change.

```csharp
using AionInstructPreview.Imaging;
using Microsoft.Graphics.Imaging;
using Microsoft.Windows.AI.ContentSafety;

using var generator = await ImageDescriptionGenerator.CreateAsync();

var op = generator.DescribeAsync(
    imageBuffer,                               // Microsoft.Graphics.Imaging.ImageBuffer
    ImageDescriptionKind.DetailedDescription,
    new ContentFilterOptions());
var result = await op;
if (result.Status == ImageDescriptionResultStatus.Complete)
{
    Console.WriteLine(result.Description);
}
else
{
    Console.WriteLine($"No final description is available ({result.Status}).");
}
```

| Member | Notes |
|---|---|
| `static ImageDescriptionGenerator.CreateAsync()` | Loads SigLIP2 and the vision projector. Neither is NPU-cached, so **expect ~20 s on every construction**, warm or cold. Create it once and keep it. |
| `DescribeAsync(ImageBuffer, ImageDescriptionKind, ContentFilterOptions)` | Streams text chunks via `Progress`; the full text is `ImageDescriptionResult.Description`. |
| `ImageDescriptionKind` | `BriefDescription`, `DetailedDescription`, `DiagramDescription`, `AccessibleDescription`. |
| `ImageDescriptionResult.Status` | `Complete` on success. WinUI distinguishes `ImageBlockedByContentModeration`, `TextInImageBlockedByContentModeration`, and `DescriptionTextBlockedByContentModeration` from operational failures. |

**Pixel formats.** `ImageBuffer` must be `Rgb8`, `Argb8`, `Bgra8`, or `Gray8`. `Bgr8` and `Rgba8`
are rejected — they have no byte-order-equivalent in the underlying stack, and silently mapping
them onto a near-miss would feed the encoder swapped channels. Convert before you call.
[`AionImageDescriptionClient.cs`](AionImageDescriptionClient.cs) shows the `BitmapDecoder` →
`Bgra8` → `ImageBuffer.CreateForBuffer` path.

### Preview limitations

> - **Moderation is enabled by default.** `DescribeAsync` honors `ContentFilterOptions`.
>   Image, extracted-text, and generated-description blocks have separate result statuses.
>   See [Content moderation](#content-moderation) for streaming limits and failure handling.
> - **OCR differs from the inbox stack.** Text in the image is extracted with the inbox
>   `Windows.Media.Ocr` engine rather than the OneOCR model the shipping stack uses, because
>   OneOCR's model key can't be redistributed in a sideloadable package. Expect lower text
>   fidelity on text-dense images. OCR is best-effort: a failure yields an empty string and
>   description continues.
> - **Description accuracy is under investigation.** Descriptions are fluent but can be
>   incorrectly grounded relative to the shipping in-box implementation. This is being tracked
>   as a vision-model/projector version-pairing issue. Treat image description in this preview as
>   an **API-compatibility** surface, not a quality baseline.

---

## Troubleshooting

Start here if `Bootstrap.ps1`, the build, or the app fails.

### Build and setup

- **`Bootstrap.ps1 cannot be loaded because running scripts is disabled on this system`** → PowerShell's default `Restricted` execution policy blocks the script. This is the most common failure on a fresh machine. Run this once in the window, before `.\Bootstrap.ps1`:

  ```powershell
  Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
  ```

  It's session-scoped (no admin, reverts when the window closes). A plain `git clone` does **not** sidestep this — the policy applies regardless of how the files arrived.

- **Scripts blocked even after `Set-ExecutionPolicy` (you downloaded the source ZIP)** → if you grabbed **"Source code (zip)"** from the releases page instead of using `git clone`, every file is tagged with the Mark-of-the-Web ("this file came from another computer"), and PowerShell refuses to run scripts that are unsigned / from the internet. Strip the mark in the repo root:

  ```powershell
  Get-ChildItem -Recurse | Unblock-File
  ```

  Better still, **prefer `git clone` over the ZIP** — cloned files carry no Mark-of-the-Web.

- **`cswinrt.exe exited with code 1`, or a XAML compiler `WMC9999` NullReferenceException** → you're almost certainly building under **`C:\Windows\System32`** (the default directory of an *elevated* PowerShell prompt). UAC file virtualization silently redirects writes under `obj\…\Generated Files\`, so `cswinrt.exe` and the XAML compiler write to and read from different paths and the codegen falls apart. **Do not clone or build under `C:\Windows\System32`.** Clone under your user profile (e.g. `%USERPROFILE%` or `C:\repos`) and build from a normal (non-elevated) prompt — building does not require admin. (The one UAC prompt `Bootstrap.ps1` raises is just to enable Developer Mode; it does not mean you should be running from System32.)

- **A build error usually does *not* mean a runtime component is missing.** Windows App Runtime 2
  is a runtime dependency. Building requires the .NET 9 SDK and package restore; Developer Mode
  is also needed for `dotnet run` to register the app.

### Runtime and deployment

- **`0x80073D19` "package family does not have any matching framework packages installed"** when deploying the consumer MSIX → the Aion Instruct Preview framework MSIX isn't installed. `Get-AppxPackage Microsoft.AionInstructPreview.Framework.1.0` should return a row.
- **`NuGet restore` fails with package not found** → put `AionInstructPreview.Text.Framework.1.0.1.nupkg` in `./nuget-local/`. This sample uses an exact version pin.
- **App crashes immediately with class-not-registered** → cross-package WinRT activation can't find the runtimeclasses. The framework MSIX must be installed (its cert is imported when you first install it), and the consumer must have package identity — `dotnet run` provides that via its development registration.
- **Stale NuGet cache after a release bump** → `Remove-Item -Recurse -Force "$env:USERPROFILE\.nuget\packages\aioninstructpreview.text.framework"`, then rebuild.
- **`Bootstrap.ps1` fails to download the release** → confirm a release exists at <https://github.com/microsoft/Aion-Instruct-Preview-Sample/releases>. If you're behind a corporate proxy, set `HTTPS_PROXY` and re-run. You can also download the three assets manually from that page (see [Manual install](#manual-install-what-bootstrapps1-does)).
- **You edited source code but the app didn't change** → `dotnet run` rebuilds and re-registers every time, so a normal re-run picks up your edits. If a stale signed MSIX of the same package is installed, remove it first: `Get-AppxPackage AionInstructPreviewChat | Remove-AppxPackage`.

---

## Filing a bug

File at **<https://github.com/microsoft/Aion-Instruct-Preview-Sample/issues/new/choose>** — pick the **Bug report** template. It pre-fills the structure we need to triage (install path, first-vs-warm launch, repro steps).

Before you file, capture the diagnostic. It checks every prereq (hardware, AppX packages, EP packages, model files, per-user cache), launches AionInstructPreview.Chat, and captures the SDK's own `Aion Instruct Preview: selected EP=…` log line via `OutputDebugString`:

```powershell
.\scripts\Diagnose-AionInstructPreview.ps1 | Tee-Object diag.txt
Get-Content diag.txt | Set-Clipboard
# paste into the "Diagnose-AionInstructPreview.ps1 output" field on the form
```

The most common failure mode is the SDK failing to initialize because no certified NPU EP is registered with WinML — the diagnostic prints which EP got picked and why, so include its output.

---

## Project layout

```
Aion-Instruct-Preview-Sample/
├── App.xaml / App.xaml.cs           # WinAppSDK app entry
├── MainWindow.xaml / .cs            # Chat + Describe image tabs, Mica, custom title bar
├── Controls/
│   └── TypingIndicator.xaml(.cs)    # Three-dot pulsing "Aion Instruct Preview is thinking" indicator
├── Models/
│   ├── Message.cs                   # One transcript entry; mutable Text for streaming
│   ├── ModelState.cs                # enum: Loading | Ready | Generating | Error
│   └── GenerationMetrics.cs         # App timing: first update, updates/s, update count
├── ViewModels/
│   └── ChatViewModel.cs             # Conversation, state machine, Send + Describe commands
├── AionInstructClient.cs                  # Async wrapper over AionInstructPreview.Text.LanguageModel
├── AionImageDescriptionClient.cs          # Async wrapper over AionInstructPreview.Imaging.ImageDescriptionGenerator
├── Package.appxmanifest             # MSIX identity. PackageDependency injected at build.
├── AionInstructPreview.Chat.csproj               # .NET 9 WinUI 3 packaged csproj
├── nuget.config                     # Feeds: nuget.org + ./nuget-local/
├── nuget-local/                     # Drop the SDK pipeline's .nupkg here
├── Bootstrap.ps1                    # One-shot quickstart (download framework, build, run)
├── scripts/Diagnose-AionInstructPreview.ps1      # First-launch hang diagnostic
└── Assets/StoreLogo.png             # Placeholder app icon
```
