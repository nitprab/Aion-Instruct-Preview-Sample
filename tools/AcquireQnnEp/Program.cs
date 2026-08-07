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
        RegistrationFailure = 5,
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
                "No execution providers were returned. Verify Windows App Runtime 1.8 " +
                "and the process privileges.");
            return ExitCode.QnnNotFound;
        }

        ExecutionProvider? qnnProvider = null;
        foreach (ExecutionProvider provider in providers)
        {
            PrintProvider(provider);
            if (qnnProvider is null &&
                provider.Name.Contains("QNN", StringComparison.OrdinalIgnoreCase))
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
        bool registered;
        using (new NativeStderrFilter())
        {
            registered = qnnProvider.TryRegister();
        }

        if (!registered)
        {
            Console.Error.WriteLine("TryRegister failed for the QNN provider.");
            return ExitCode.RegistrationFailure;
        }

        Console.WriteLine("QNN provider registered successfully for this process.");
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
