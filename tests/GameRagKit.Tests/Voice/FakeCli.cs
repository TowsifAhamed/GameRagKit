using System.Runtime.InteropServices;

namespace GameRagKit.Tests.Voice;

/// <summary>
/// Writes small fake CLI scripts that mimic whisper-cli/piper's process contract (argument
/// shapes, stdin/stdout behavior, exit codes) closely enough to unit-test
/// WhisperCppClient/PiperTtsClient's process-orchestration logic without depending on the
/// real binaries being installed.
/// </summary>
internal static class FakeCli
{
    /// <summary>
    /// Mimics `whisper-cli ... -of <path> -otxt`: writes <paramref name="transcript"/> to
    /// "<of-path>.txt", ignoring all other arguments, and exits 0.
    /// </summary>
    public static string WriteWhisperCliFake(string transcript)
    {
        var scriptPath = CreateScriptPath();
        var script = $"""
            #!/bin/sh
            of=""
            prev=""
            for arg in "$@"; do
              if [ "$prev" = "-of" ]; then
                of="$arg"
              fi
              prev="$arg"
            done
            printf '%s' "{EscapeForShell(transcript)}" > "$of.txt"
            exit 0
            """;
        File.WriteAllText(scriptPath, script);
        MakeExecutable(scriptPath);
        return scriptPath;
    }

    // A minimal valid 16-bit PCM mono 16kHz WAV file (10 silent samples, 64 bytes total),
    // pre-generated and base64-encoded so the fake CLI script below has no dependency on
    // Python or any other WAV-writing tool being present in the test environment.
    private const string TinyWavBase64 =
        "UklGRjgAAABXQVZFZm10IBAAAAABAAEAgD4AAAB9AAACABAAZGF0YRQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==";

    /// <summary>
    /// Mimics `piper -f <path>`: reads and discards stdin, writes a minimal valid WAV file
    /// to the -f path, exits 0.
    /// </summary>
    public static string WritePiperFake()
    {
        var scriptPath = CreateScriptPath();
        var script = $"""
            #!/bin/sh
            cat > /dev/null
            f=""
            prev=""
            for arg in "$@"; do
              if [ "$prev" = "-f" ]; then
                f="$arg"
              fi
              prev="$arg"
            done
            echo "{TinyWavBase64}" | base64 -d > "$f"
            exit 0
            """;
        File.WriteAllText(scriptPath, script);
        MakeExecutable(scriptPath);
        return scriptPath;
    }

    public static string WriteFailingCli(string stderrMessage)
    {
        var scriptPath = CreateScriptPath();
        var script = $"""
            #!/bin/sh
            cat > /dev/null 2>/dev/null || true
            echo "{EscapeForShell(stderrMessage)}" 1>&2
            exit 1
            """;
        File.WriteAllText(scriptPath, script);
        MakeExecutable(scriptPath);
        return scriptPath;
    }

    public static string WriteHangingCli()
    {
        var scriptPath = CreateScriptPath();
        var script = """
            #!/bin/sh
            cat > /dev/null 2>/dev/null || true
            sleep 300
            """;
        File.WriteAllText(scriptPath, script);
        MakeExecutable(scriptPath);
        return scriptPath;
    }

    private static string CreateScriptPath()
    {
        return Path.Combine(Path.GetTempPath(), $"gamerag-fakecli-{Guid.NewGuid():N}.sh");
    }

    private static void MakeExecutable(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var chmod = System.Diagnostics.Process.Start("chmod", $"+x \"{path}\"");
        chmod?.WaitForExit();
    }

    private static string EscapeForShell(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`");
    }
}
