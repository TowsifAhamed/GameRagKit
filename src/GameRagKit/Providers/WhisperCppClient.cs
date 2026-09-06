using System.Diagnostics;
using System.Text;

namespace GameRagKit.Providers;

/// <summary>
/// Speech-to-text via the whisper.cpp CLI (the `whisper-cli` binary, e.g. from
/// `brew install whisper-cpp`), run as a subprocess. GameRagKit does not bundle or manage
/// the whisper.cpp binary itself -- install it separately, the same way Ollama is treated
/// as an external local service.
/// </summary>
public sealed class WhisperCppClient : ISpeechToText
{
    private readonly WhisperCppOptions _options;

    public WhisperCppClient(WhisperCppOptions options)
    {
        _options = options;
    }

    public async Task<string> TranscribeAsync(byte[] audioWavBytes, CancellationToken cancellationToken)
    {
        var workDir = Path.Combine(Path.GetTempPath(), "gamerag-whisper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var audioPath = Path.Combine(workDir, "input.wav");
        var outputBasePath = Path.Combine(workDir, "output");

        try
        {
            await File.WriteAllBytesAsync(audioPath, audioWavBytes, cancellationToken).ConfigureAwait(false);

            var arguments = new[]
            {
                "-m", _options.ModelPath,
                "-f", audioPath,
                "-l", _options.Language,
                "-otxt",
                "-of", outputBasePath,
                "-np",
                "-nt"
            };

            await RunProcessAsync(_options.ExecutablePath, arguments, _options.Timeout, cancellationToken).ConfigureAwait(false);

            var transcriptPath = outputBasePath + ".txt";
            if (!File.Exists(transcriptPath))
            {
                throw new InvalidOperationException("whisper-cli did not produce a transcript file.");
            }

            var transcript = await File.ReadAllTextAsync(transcriptPath, cancellationToken).ConfigureAwait(false);
            return transcript.Trim();
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    private static async Task RunProcessAsync(string executablePath, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
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
