// HPG Mission Companion - audio effects on the speech wav: volume and the radio effect (band filter, beep, hiss, squelch tail).
using System;
using System.Collections.Generic;
using System.IO;

namespace HPGMissionCompanion
{
    internal static class AudioFx
    {
        public static readonly string[] BeepTypes = new string[] { "None", "Click", "Beep", "Double beep", "Chirp" };
        // a beep type from a mission tag ("double", "Double beep", "3", "chirp", "off") -> the name in BeepTypes, or null when it is not one
        public static string NormalizeBeep(string name)
        {
            if (name == null) return null;
            string n = name.Trim().ToLowerInvariant().Replace(" ", "");
            switch (n)
            {
                case "0": case "none": case "off": case "no": return BeepTypes[0];
                case "1": case "click": return BeepTypes[1];
                case "2": case "beep": case "single": return BeepTypes[2];
                case "3": case "double": case "doublebeep": return BeepTypes[3];
                case "4": case "chirp": return BeepTypes[4];
            }
            return null;
        }

        public static readonly string[] Qualities = new string[] { "Clean", "Aviation", "Rough" };

        sealed class Pcm
        {
            public float[] Data;
            public int Rate;
        }

        sealed class Biquad
        {
            double b0, b1, b2, a1, a2, x1, x2, y1, y2;

            public static Biquad LowPass(double fs, double f, double q) { return Make(fs, f, q, true); }
            public static Biquad HighPass(double fs, double f, double q) { return Make(fs, f, q, false); }

            static Biquad Make(double fs, double f, double q, bool low)
            {
                f = Math.Min(f, fs * 0.45);
                double w0 = 2 * Math.PI * f / fs;
                double cs = Math.Cos(w0), alpha = Math.Sin(w0) / (2 * q);
                double a0 = 1 + alpha;
                Biquad b = new Biquad();
                if (low) { b.b0 = (1 - cs) / 2; b.b1 = 1 - cs; b.b2 = (1 - cs) / 2; }
                else { b.b0 = (1 + cs) / 2; b.b1 = -(1 + cs); b.b2 = (1 + cs) / 2; }
                b.a1 = -2 * cs; b.a2 = 1 - alpha;
                b.b0 /= a0; b.b1 /= a0; b.b2 /= a0; b.a1 /= a0; b.a2 /= a0;
                return b;
            }

            public double Process(double x)
            {
                double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                return y;
            }
        }

        // ---- wav in and out -------------------------------------------------------------------
        static Pcm Decode(byte[] wav)
        {
            if (wav == null || wav.Length < 44) return null;
            if (wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F') return null;
            int pos = 12, channels = 0, rate = 0, bits = 0, format = 0;
            int dataPos = -1, dataLen = 0;
            while (pos + 8 <= wav.Length)
            {
                string id = "" + (char)wav[pos] + (char)wav[pos + 1] + (char)wav[pos + 2] + (char)wav[pos + 3];
                long size = BitConverter.ToUInt32(wav, pos + 4);
                int body = pos + 8;
                if (id == "fmt " && body + 16 <= wav.Length)
                {
                    format = BitConverter.ToUInt16(wav, body);
                    channels = BitConverter.ToUInt16(wav, body + 2);
                    rate = BitConverter.ToInt32(wav, body + 4);
                    bits = BitConverter.ToUInt16(wav, body + 14);
                }
                else if (id == "data")
                {
                    dataPos = body;
                    dataLen = (int)Math.Min(size, (long)(wav.Length - body));
                    break;
                }
                long next = body + size + (size & 1);
                if (next > wav.Length) break;
                pos = (int)next;
            }
            if (dataPos < 0 || bits != 16 || channels < 1 || rate < 8000) return null;
            if (format != 1 && format != 0xFFFE) return null;
            int frames = dataLen / (2 * channels);
            float[] data = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                double sum = 0;
                for (int c = 0; c < channels; c++) sum += BitConverter.ToInt16(wav, dataPos + (i * channels + c) * 2);
                data[i] = (float)(sum / channels / 32768.0);
            }
            Pcm p = new Pcm();
            p.Data = data; p.Rate = rate;
            return p;
        }

        static byte[] Encode(float[] data, int rate)
        {
            byte[] outb = new byte[44 + data.Length * 2];
            WriteAscii(outb, 0, "RIFF");
            BitConverter.GetBytes(36 + data.Length * 2).CopyTo(outb, 4);
            WriteAscii(outb, 8, "WAVEfmt ");
            BitConverter.GetBytes(16).CopyTo(outb, 16);
            BitConverter.GetBytes((short)1).CopyTo(outb, 20);
            BitConverter.GetBytes((short)1).CopyTo(outb, 22);
            BitConverter.GetBytes(rate).CopyTo(outb, 24);
            BitConverter.GetBytes(rate * 2).CopyTo(outb, 28);
            BitConverter.GetBytes((short)2).CopyTo(outb, 32);
            BitConverter.GetBytes((short)16).CopyTo(outb, 34);
            WriteAscii(outb, 36, "data");
            BitConverter.GetBytes(data.Length * 2).CopyTo(outb, 40);
            for (int i = 0; i < data.Length; i++)
            {
                double v = Math.Max(-1.0, Math.Min(1.0, data[i]));
                BitConverter.GetBytes((short)Math.Round(v * 32767.0)).CopyTo(outb, 44 + i * 2);
            }
            return outb;
        }

        static void WriteAscii(byte[] b, int pos, string s)
        {
            for (int i = 0; i < s.Length; i++) b[pos + i] = (byte)s[i];
        }

        // ---- public entry ---------------------------------------------------------------------
        // volume 0-100 (tag), master 0-100 (settings). Returns the wav unchanged when nothing has to be done.
        public static byte[] Apply(byte[] wav, double volume, double master, bool radio, RadioSettings rs)
        {
            double gain = Math.Max(0, Math.Min(100, volume)) / 100.0 * Math.Max(0, Math.Min(100, master)) / 100.0;
            if (!radio && Math.Abs(gain - 1.0) < 0.001) return wav;
            Pcm pcm = Decode(wav);
            if (pcm == null) return wav;
            float[] data = radio ? RadioChain(pcm, rs) : pcm.Data;
            if (!radio) data = (float[])data.Clone();
            double peak = 0;
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (float)(data[i] * gain);
                double a = Math.Abs(data[i]);
                if (a > peak) peak = a;
            }
            if (peak > 0.98)
            {
                float k = (float)(0.98 / peak);
                for (int i = 0; i < data.Length; i++) data[i] *= k;
            }
            return Encode(data, pcm.Rate);
        }

        // ---- radio effect ---------------------------------------------------------------------
        static float[] RadioChain(Pcm pcm, RadioSettings rs)
        {
            int sr = pcm.Rate;
            double hp, lp, drive;
            switch (rs.Quality)
            {
                case "Clean": hp = 250; lp = 4500; drive = 1.0; break;
                case "Rough": hp = 500; lp = 2400; drive = 5.0; break;
                default: hp = 350; lp = 2800; drive = 2.2; break;
            }

            // speech: band filter, soft compression, back to the original peak
            float[] speech = new float[pcm.Data.Length];
            Biquad h1 = Biquad.HighPass(sr, hp, 0.707), h2 = Biquad.HighPass(sr, hp, 0.707);
            Biquad l1 = Biquad.LowPass(sr, lp, 0.707), l2 = Biquad.LowPass(sr, lp, 0.707);
            double inSum = 0, outSum = 0;
            for (int i = 0; i < speech.Length; i++)
            {
                double x = pcm.Data[i];
                inSum += x * x;
                x = l2.Process(l1.Process(h2.Process(h1.Process(x))));
                if (drive > 1.01) x = Math.Tanh(drive * x) / Math.Tanh(drive);
                speech[i] = (float)x;
                outSum += x * x;
            }
            // same loudness as the clean voice (rms), the final limiter takes care of the peaks
            if (outSum > 1e-9 && inSum > 1e-9)
            {
                float k = (float)Math.Sqrt(inSum / outSum);
                for (int i = 0; i < speech.Length; i++) speech[i] *= k;
            }

            // timeline: lead-in hiss, beep, gap, speech, hiss, squelch tail
            float[] beep = MakeBeep(rs.Beep, sr, rs.BeepLevel / 100.0);
            int lead = (int)(0.06 * sr);
            int gap = beep.Length > 0 ? (int)(0.09 * sr) : (int)(0.10 * sr);
            int hissTail = (int)(0.10 * sr);
            int squelch = rs.Tail ? (int)(0.12 * sr) : 0;
            int speechStart = lead + beep.Length + gap;
            int hissEnd = speechStart + speech.Length + hissTail;
            float[] outd = new float[hissEnd + squelch];

            double noiseRms = 0.25 * Math.Pow(rs.NoiseLevel / 100.0, 1.5);
            if (noiseRms > 0)
            {
                float[] noise = MakeNoise(hissEnd, sr, noiseRms);
                int fade = (int)(0.01 * sr);
                for (int i = 0; i < hissEnd; i++)
                {
                    double env = 1.0;
                    if (i < fade) env = i / (double)fade;
                    else if (i > hissEnd - fade) env = (hissEnd - i) / (double)fade;
                    outd[i] += (float)(noise[i] * env);
                }
            }
            for (int i = 0; i < beep.Length; i++) outd[lead + i] += beep[i];
            for (int i = 0; i < speech.Length; i++) outd[speechStart + i] += speech[i];
            if (squelch > 0)
            {
                double tailRms = Math.Max(0.05, noiseRms * 2.5);
                float[] burst = MakeNoise(squelch, sr, tailRms);
                for (int i = 0; i < squelch; i++)
                {
                    double env = Math.Exp(-5.0 * i / squelch);
                    outd[hissEnd + i] += (float)(burst[i] * env);
                }
            }
            return outd;
        }

        static float[] MakeNoise(int n, int sr, double targetRms)
        {
            Random rng = new Random(12345 + n);
            float[] r = new float[n];
            Biquad h1 = Biquad.HighPass(sr, 280, 0.707), h2 = Biquad.HighPass(sr, 280, 0.707);
            Biquad l1 = Biquad.LowPass(sr, 3600, 0.707), l2 = Biquad.LowPass(sr, 3600, 0.707);
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                double x = l2.Process(l1.Process(h2.Process(h1.Process(rng.NextDouble() * 2 - 1))));
                r[i] = (float)x;
                sum += x * x;
            }
            double rms = n > 0 ? Math.Sqrt(sum / n) : 0;
            if (rms > 1e-9)
            {
                float k = (float)(targetRms / rms);
                for (int i = 0; i < n; i++) r[i] *= k;
            }
            return r;
        }

        static float[] MakeBeep(string type, int sr, double level)
        {
            double amp = 0.35 * level;
            List<float> list = new List<float>();
            if (type == "Beep") Tone(list, sr, 1000, 1000, 0.110, amp);
            else if (type == "Double beep")
            {
                Tone(list, sr, 1000, 1000, 0.070, amp);
                Silence(list, sr, 0.060);
                Tone(list, sr, 1000, 1000, 0.070, amp);
            }
            else if (type == "Chirp") Tone(list, sr, 700, 1500, 0.150, amp);
            else if (type == "Click")
            {
                Random rng = new Random(77);
                int n = (int)(0.030 * sr);
                for (int i = 0; i < n; i++) list.Add((float)((rng.NextDouble() * 2 - 1) * Math.Exp(-6.0 * i / n) * amp * 1.4));
            }
            return list.ToArray();
        }

        static void Tone(List<float> list, int sr, double f0, double f1, double seconds, double amp)
        {
            int n = (int)(seconds * sr);
            int fade = (int)(0.005 * sr);
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                double f = f0 + (f1 - f0) * i / (double)n;
                phase += 2 * Math.PI * f / sr;
                double env = 1.0;
                if (i < fade) env = i / (double)fade;
                else if (i > n - fade) env = (n - i) / (double)fade;
                list.Add((float)(Math.Sin(phase) * amp * env));
            }
        }

        static void Silence(List<float> list, int sr, double seconds)
        {
            int n = (int)(seconds * sr);
            for (int i = 0; i < n; i++) list.Add(0f);
        }

        // for the selftest report
        public static string Describe(byte[] wav)
        {
            Pcm p = Decode(wav);
            if (p == null) return "(not PCM16)";
            double peak = 0, sum = 0;
            foreach (float s in p.Data) { double a = Math.Abs(s); if (a > peak) peak = a; sum += s * s; }
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.00} s at {1} Hz, peak {2:0.000}, rms {3:0.0000}", p.Data.Length / (double)p.Rate, p.Rate, peak, p.Data.Length > 0 ? Math.Sqrt(sum / p.Data.Length) : 0);
        }
    }
}
