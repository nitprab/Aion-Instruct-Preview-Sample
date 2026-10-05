using Microsoft.Windows.AI.MachineLearning;

namespace AionInstructPreview.Tools.AcquireQnnEp;

internal static class Program
{
    private enum ExitCode
    {
        Success = 0,
        InvalidArguments = 1,
        CatalogFailure = 2,
        QnnNotFound = 3,
        EnsureReadyFailure = 4,
        // 5 was RegistrationFailure, retired: in-process registration is not something
        // this tool's callers depend on. See the TryRegister note in AcquireQnnAsync.
    }

    public static async Task<int> Main(string[] args)
    {
        if (!TryParseArguments(args, out bool showHelp))
        {
            PrintUsage(Console.Error);
            return (int)ExitCode.InvalidArguments;
        }

        if (showHelp)
        {
            PrintUsage(Console.Out);
            return (int)ExitCode.Success;
        }

        try
        {
            string packageFullName = WindowsAppRuntimeDependency.EnsureLoaded();
            Console.WriteLine($"Loaded Windows App Runtime dependency: {packageFullName}");
            return (int)await AcquireQnnAsync();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(
                $"Windows ML catalog operation failed: HRESULT=0x{error.HResult:X8}, " +
                $"Message={error.Message}");
            return (int)ExitCode.CatalogFailure;
        }
    }

    private static async Task<ExitCode> AcquireQnnAsync()
    {
        ExecutionProviderCatalog catalog = ExecutionProviderCatalog.GetDefault();
        IReadOnlyList<ExecutionProvider> providers = catalog.FindAllProviders();

        Console.WriteLine($"Providers found: {providers.Count}");
        if (providers.Count == 0)
        {
            Console.Error.WriteLine(
                "No execution providers were returned. Verify the Windows ML runtime " +
                "and the process privileges.");
            return ExitCode.QnnNotFound;
        }

        // Prefer the highest-versioned QNN provider when a machine surfaces more than one.
        // Snapdragon boxes can carry several packages publishing a QNN provider (for example
        // ...QNN.EP.1.8 alongside ...QNN.EP.2), and the Aion Instruct model's shared-context
        // groups only compile on the newer one: EP 2.2451.48 fails all six cacheable models
        // where 2.2480.49 passes all six. ExecutionProvider exposes no version, so ordering
        // falls back to the library path; when that is empty the first match wins, which
        // matches the previous behaviour.
        ExecutionProvider? qnnProvider = null;
        foreach (ExecutionProvider provider in providers)
        {
            PrintProvider(provider);
            if (!provider.Name.Contains("QNN", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (qnnProvider is null ||
                string.CompareOrdinal(provider.LibraryPath ?? string.Empty,
                                      qnnProvider.LibraryPath ?? string.Empty) > 0)
            {
                qnnProvider = provider;
            }
        }

        if (qnnProvider is null)
        {
            Console.Error.WriteLine("QNN execution provider was not found.");
            return ExitCode.QnnNotFound;
        }

        Console.WriteLine($"Selected QNN provider: {qnnProvider.Name}");
        Console.WriteLine(
            "Ensuring QNN provider is ready. This may download or install components...");

        ExecutionProviderReadyResult ensureResult = await qnnProvider.EnsureReadyAsync();
        if (ensureResult.Status != ExecutionProviderReadyResultState.Success)
        {
            Console.Error.WriteLine(
                $"EnsureReadyAsync failed. Status={(int)ensureResult.Status}, " +
                $"DiagnosticText={ensureResult.DiagnosticText}");
            return ExitCode.EnsureReadyFailure;
        }

        Console.WriteLine("QNN provider is ready.");

        // EnsureReadyAsync above is the part that matters: it stages the provider package
        // machine-wide, which is what this tool exists to do. TryRegister only registers the
        // provider into THIS process's inference environment, and this process is about to
        // exit -- and the Aion Instruct SDK performs its own provider registration against
        // its own bundled ONNX Runtime when it loads. So a TryRegister failure here says
        // nothing about whether the machine is provisioned; report it and still succeed.
        bool registered;
        using (new NativeStderrFilter())
        {
            registered = qnnProvider.TryRegister();
        }

        Console.WriteLine(registered
            ? "QNN provider registered successfully for this process."
            : "QNN provider staged, but in-process registration was declined. This does not " +
              "affect the SDK, which registers the provider itself.");
        return ExitCode.Success;
    }

    private static bool TryParseArguments(string[] args, out bool showHelp)
    {
        showHelp = false;
        foreach (string argument in args)
        {
            if (argument is "--help" or "-h" or "/?")
            {
                showHelp = true;
                continue;
            }

            Console.Error.WriteLine($"Unknown command-line argument: {argument}");
            return false;
        }

        return true;
    }

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("AcquireQnnEp [--help]");
        writer.WriteLine();
        writer.WriteLine(
            "Downloads or prepares the QNN execution provider when needed, then registers it " +
            "for this process.");
    }

    private static void PrintProvider(ExecutionProvider provider)
    {
        Console.WriteLine(
            $"Provider: {provider.Name} | " +
            $"Certification: {provider.Certification} ({(int)provider.Certification}) | " +
            $"ReadyState: {provider.ReadyState} ({(int)provider.ReadyState})");
    }
}
