using System.Diagnostics;
using System.Text;

namespace GameRagKit.Providers;

/// <summary>
/// Text-to-speech via the Piper CLI (`pip install piper-tts`), run as a subprocess.
/// GameRagKit does not bundle or manage the Piper binary itself -- install it separately.
///
/// Known environment gotcha (found while integrating): some Piper distributions resolve
/// their bundled espeak-ng phoneme data files (phontab, phonindex, etc.) relative to an
/// incorrect base path and fail with "No such file or directory" for files that do exist
/// one directory level away. If transcription fails with that error, symlink the contents
/// of the installed `piper/espeak-ng-data/` directory into its parent `site-packages/`
/// directory as a workaround -- this is an upstream packaging issue, not a GameRagKit bug.
/// </summary>
public sealed class PiperTtsClient : ITextToSpeech
{
    private readonly PiperTtsOptions _options;

    public PiperTtsClient(PiperTtsOptions options)
    {
        _options = options;
    }

    public async Task<byte[]> SynthesizeAsync(string text, CancellationToken cancellationToken)
    {
        var workDir = Path.Combine(Path.GetTempPath(), "gamerag-piper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var outputPath = Path.Combine(workDir, "output.wav");

        try
        {
            await RunProcessAsync(_options.ExecutablePath, new[] { "-m", _options.VoiceModelPath, "-f", outputPath }, text, _options.Timeout, cancellationToken)
                .ConfigureAwait(false);

            if (!File.Exists(outputPath))
            {
                throw new InvalidOperationException("piper did not produce an output WAV file.");
            }

            return await File.ReadAllBytesAsync(outputPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    private static async Task RunProcessAsync(string executablePath, IReadOnlyList<string> arguments, string stdinText, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        var stderrBuilder = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stderrBuilder.AppendLine(e.Data);
            }
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start process: {executablePath}");
            }

            process.BeginErrorReadLine();
            await process.StandardInput.WriteAsync(stdinText).ConfigureAwait(false);
            process.StandardInput.Close();

            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(executablePath)} exited with code {process.ExitCode}. Stderr: {stderrBuilder}");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"{Path.GetFileName(executablePath)} timed out after {timeout}.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup; the process may have already exited.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup; leftover temp files are not fatal.
        }
    }
}
