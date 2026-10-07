using System;
using UnityEngine;

namespace GameRagKit.Unity
{
    /// <summary>
    /// 16-bit PCM WAV encoding/decoding. The server's speech-to-text (whisper.cpp) wants
    /// 16 kHz mono WAV, and its text-to-speech returns 16-bit PCM WAV at the voice's own
    /// sample rate, so this is the only audio format the voice API needs.
    /// </summary>
    public static class WavUtility
    {
        public const int SpeechSampleRate = 16000;

        /// <summary>
        /// Encodes interleaved float samples as a 16 kHz mono WAV, downmixing and
        /// resampling (linear interpolation, plenty for speech) as needed.
        /// </summary>
        public static byte[] EncodeSpeechWav(float[] samples, int channels, int sampleRate, int frameCount = -1)
        {
            if (samples == null || channels <= 0 || sampleRate <= 0)
            {
                throw new ArgumentException("samples, channels and sampleRate are required.");
            }

            var frames = frameCount >= 0 ? Math.Min(frameCount, samples.Length / channels) : samples.Length / channels;
            var mono = new float[frames];
            for (var f = 0; f < frames; f++)
            {
                float sum = 0;
                for (var c = 0; c < channels; c++)
                {
                    sum += samples[f * channels + c];
                }

                mono[f] = sum / channels;
            }

            return EncodePcm16(Resample(mono, sampleRate, SpeechSampleRate), SpeechSampleRate, 1);
        }

        /// <summary>16 kHz mono WAV of an AudioClip (e.g. one recorded with Microphone.Start).</summary>
        public static byte[] EncodeSpeechWav(AudioClip clip, int frameCount = -1)
        {
            var frames = frameCount >= 0 ? Math.Min(frameCount, clip.samples) : clip.samples;
            var data = new float[frames * clip.channels];
            clip.GetData(data, 0);
            return EncodeSpeechWav(data, clip.channels, clip.frequency, frames);
        }

        public static byte[] EncodePcm16(float[] interleaved, int sampleRate, int channels)
        {
            var dataBytes = interleaved.Length * 2;
            var wav = new byte[44 + dataBytes];
            WriteAscii(wav, 0, "RIFF");
            WriteInt(wav, 4, 36 + dataBytes);
            WriteAscii(wav, 8, "WAVE");
            WriteAscii(wav, 12, "fmt ");
            WriteInt(wav, 16, 16);
            WriteShort(wav, 20, 1); // PCM
            WriteShort(wav, 22, (short)channels);
            WriteInt(wav, 24, sampleRate);
            WriteInt(wav, 28, sampleRate * channels * 2);
            WriteShort(wav, 32, (short)(channels * 2));
            WriteShort(wav, 34, 16);
            WriteAscii(wav, 36, "data");
            WriteInt(wav, 40, dataBytes);
            for (var i = 0; i < interleaved.Length; i++)
            {
                var s = Mathf.Clamp(interleaved[i], -1f, 1f);
                WriteShort(wav, 44 + i * 2, (short)(s < 0 ? s * 32768f : s * 32767f));
            }

            return wav;
        }

        /// <summary>Decodes a 16-bit PCM WAV into interleaved floats. Walks the chunk list, so extra chunks (LIST, etc.) are fine.</summary>
        public static bool TryDecodePcm16(byte[] wav, out float[] samples, out int channels, out int sampleRate)
        {
            samples = null;
            channels = 0;
            sampleRate = 0;
            if (wav == null || wav.Length < 44 || ReadAscii(wav, 0, 4) != "RIFF" || ReadAscii(wav, 8, 4) != "WAVE")
            {
                return false;
            }

            var bits = 0;
            var offset = 12;
            while (offset + 8 <= wav.Length)
            {
                var id = ReadAscii(wav, offset, 4);
                var size = BitConverter.ToInt32(wav, offset + 4);
                var body = offset + 8;
                if (id == "fmt ")
                {
                    channels = BitConverter.ToInt16(wav, body + 2);
                    sampleRate = BitConverter.ToInt32(wav, body + 4);
                    bits = BitConverter.ToInt16(wav, body + 14);
                }
                else if (id == "data")
                {
                    if (bits != 16 || channels <= 0)
                    {
                        return false;
                    }

                    var length = Math.Min(size, wav.Length - body) / 2;
                    samples = new float[length];
                    for (var i = 0; i < length; i++)
                    {
                        samples[i] = BitConverter.ToInt16(wav, body + i * 2) / 32768f;
                    }

                    return true;
                }

                offset = body + size + (size & 1);
            }

            return false;
        }

        /// <summary>An AudioClip from 16-bit PCM WAV bytes (what the server's text-to-speech returns).</summary>
        public static AudioClip ToAudioClip(byte[] wav, string name = "npc-speech")
        {
            if (!TryDecodePcm16(wav, out var samples, out var channels, out var sampleRate))
            {
                return null;
            }

            var clip = AudioClip.Create(name, samples.Length / channels, channels, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        internal static float[] Resample(float[] mono, int fromRate, int toRate)
        {
            if (fromRate == toRate || mono.Length == 0)
            {
                return mono;
            }

            var outLength = (int)((long)mono.Length * toRate / fromRate);
            var result = new float[outLength];
            var step = (double)fromRate / toRate;
            for (var i = 0; i < outLength; i++)
            {
                var pos = i * step;
                var index = (int)pos;
                var frac = (float)(pos - index);
                var a = mono[Math.Min(index, mono.Length - 1)];
                var b = mono[Math.Min(index + 1, mono.Length - 1)];
                result[i] = a + (b - a) * frac;
            }

            return result;
        }

        private static void WriteAscii(byte[] buffer, int offset, string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                buffer[offset + i] = (byte)text[i];
            }
        }

        private static void WriteInt(byte[] buffer, int offset, int value) => BitConverter.GetBytes(value).CopyTo(buffer, offset);

        private static void WriteShort(byte[] buffer, int offset, short value) => BitConverter.GetBytes(value).CopyTo(buffer, offset);

        private static string ReadAscii(byte[] buffer, int offset, int count)
        {
            var chars = new char[count];
            for (var i = 0; i < count; i++)
            {
                chars[i] = (char)buffer[offset + i];
            }

            return new string(chars);
        }
    }
}
