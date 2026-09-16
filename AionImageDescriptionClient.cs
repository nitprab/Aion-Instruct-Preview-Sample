using System;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using global::AionInstructPreview.Imaging;
using Microsoft.Graphics.Imaging;
using Microsoft.Windows.AI.ContentSafety;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using AionInstructPreview.Chat.Models;

namespace AionInstructPreview.Chat;

// Bundles the image description result with timing metrics collected during the
// streaming call, mirroring AionGenerationResult on the text side.
public sealed record AionDescriptionResult(
    ImageDescriptionResult Response,
    GenerationMetrics Metrics);

// Thin async wrapper around the AionInstructPreview.Imaging.ImageDescriptionGenerator
// runtimeclass, mirroring AionInstructClient.
//
// The API surface is deliberately identical to the inbox
// Microsoft.Windows.AI.Imaging.ImageDescriptionGenerator, so app code written against
// this preview moves to the inbox stack by changing the `using` only.
//
// IMPORTANT: this preview performs NO content moderation. DescribeAsync accepts a
// ContentFilterOptions to match the inbox signature, but it is ignored: nothing is
// filtered and the moderation members of ImageDescriptionResultStatus are never
// returned. Do not use this build to validate content-filtering configuration.
public sealed class AionImageDescriptionClient : IDisposable
{
    private readonly ImageDescriptionGenerator _generator;
    private bool _disposed;

    private AionImageDescriptionClient(ImageDescriptionGenerator generator)
    {
        _generator = generator;
    }

    // Loads SigLIP2 + the Muffin vision projector. Neither vision model is NPU-cached,
    // so they are recompiled on every construction -- expect ~20s even on a warm machine.
    // Keep a loading affordance up until this completes.
    public static async Task<AionImageDescriptionClient> CreateAsync()
    {
        var generator = await ImageDescriptionGenerator.CreateAsync();
        return new AionImageDescriptionClient(generator);
    }

    // Describes a single image file. Description deltas are streamed to onToken, which
    // fires on a background thread -- the caller marshals to the UI thread.
    public async Task<AionDescriptionResult> DescribeAsync(
        StorageFile file,
        ImageDescriptionKind kind,
        Action<string> onToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(onToken);
        ThrowIfDisposed();

        ImageBuffer imageBuffer = await LoadImageBufferAsync(file).ConfigureAwait(false);

        var stopwatch = Stopwatch.StartNew();
        long ttftTicks = 0;
        int tokenCount = 0;

        var op = _generator.DescribeAsync(imageBuffer, kind, new ContentFilterOptions());
        op.Progress = (_, delta) =>
        {
            if (tokenCount == 0)
            {
                ttftTicks = stopwatch.ElapsedTicks;
            }
            tokenCount++;
            onToken(delta);
        };

        var response = await op;
        stopwatch.Stop();

        double totalMs = stopwatch.Elapsed.TotalMilliseconds;
        double ttftMs = ttftTicks == 0 ? totalMs : ttftTicks * 1000.0 / Stopwatch.Frequency;
        double decodeMs = totalMs - ttftMs;
        double? tps = (tokenCount > 1 && decodeMs > 0)
            ? (tokenCount - 1) * 1000.0 / decodeMs
            : (double?)null;

        var metrics = new GenerationMetrics(ttftMs, decodeMs, totalMs, tokenCount, tps);
        return new AionDescriptionResult(response, metrics);
    }

    // Decodes any WIC-supported image file to a Bgra8 ImageBuffer.
    //
    // Bgra8 is used because it is one of the four formats the SDK accepts
    // (Rgb8, Argb8, Bgra8, Gray8) and is what BitmapDecoder produces most cheaply.
    private static async Task<ImageBuffer> LoadImageBufferAsync(StorageFile file)
    {
        using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);

        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        PixelDataProvider pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            new BitmapTransform(),
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);

        byte[] bytes = pixels.DetachPixelData();
        int width = (int)decoder.OrientedPixelWidth;
        int height = (int)decoder.OrientedPixelHeight;

        return ImageBuffer.CreateForBuffer(
            bytes.AsBuffer(),
            ImageBufferPixelFormat.Bgra8,
            width,
            height,
            width * 4);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ((IDisposable)_generator).Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AionImageDescriptionClient));
        }
    }
}
