// SimConnect link with the simulator.
//  - keeps the local variable L:AUDIOINHIBIT at 1 (option "Auto set default audio inhibit", on by default);
//  - publishes the state of the voice pack system in L:HMC_VOICE_STATUS, L:HMC_VOICEPACK_VERSION and L:HMC_APP_VERSION
//    (see docs/architecture/VOICEPACKS.md), so a mission can read it with a wait_for.
// The native SimConnect.dll is not part of this repository: it comes from the MSFS SDK (see THIRD_PARTY.md);
// tools\build.cmd copies it next to the executable and the program also looks for it in the SDK folders.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace HPGMissionCompanion
{
    internal static class SimLink
    {
        public const string Variable = "L:AUDIOINHIBIT";
        public const double Wanted = 1.0;
        public const int PingSeconds = 5;

        public const string VarStatus = "L:HMC_VOICE_STATUS";             // 0 no pack declared, 1 pack ok, 2 pack missing, 3 pack older than the mission needs
        public const string VarPackVersion = "L:HMC_VOICEPACK_VERSION";   // version of the declared pack, 0 when missing
        public const string VarAppVersion = "L:HMC_APP_VERSION";          // version of this program (0.4 -> 0.4)

        // the mission publishes its context for the radio rule (see docs/architecture/VOICEPACKS.md); the program only reads them
        public const string CtxValid = "L:HMC_CTX_VALID";                 // 1 when the mission publishes the two variables below
        public const string CtxHeliOff = "L:HMC_HELI_OFF";                // 1 helicopter completely off
        public const string CtxTechSex = "L:HMC_TECH_SEX";                 // the technician of the scene: 0 male, 1 female (the mission picks one at random in app mode)
        public const string CtxCrew = "L:HMC_CREW_PRESENT";               // bit 0 pilot, 1 copilot, 2 doctor, 3 HEMS1, 4 HEMS2: aboard

        const uint WatchDefineBase = 300;
        const uint WatchRequestBase = 300;
        const int WatchGroup = 90;
        const uint PeriodSimFrame = 3;
        const uint FlagChanged = 1;

        const uint DefineId = 1;
        const uint RequestId = 1;
        const uint OutputDefineBase = 100;
        const uint ObjectUser = 0;
        const uint DatatypeFloat64 = 4;
        const uint PeriodOnce = 1;
        const int RecvQuit = 3;
        const int RecvException = 1;
        const int RecvSimObjectData = 8;
        const int DataOffset = 40;     // SIMCONNECT_RECV_SIMOBJECT_DATA: 12 byte header + 7 dwords, then dwData

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryW(string path);

        [DllImport("SimConnect.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        static extern int SimConnect_Open(out IntPtr handle, string name, IntPtr hwnd, uint userEvent, IntPtr hEvent, uint configIndex);

        [DllImport("SimConnect.dll", CallingConvention = CallingConvention.StdCall)]
        static extern int SimConnect_Close(IntPtr handle);

        [DllImport("SimConnect.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        static extern int SimConnect_AddToDataDefinition(IntPtr handle, uint defineId, string datumName, string unitsName, uint datumType, float epsilon, uint datumId);

        [DllImport("SimConnect.dll", CallingConvention = CallingConvention.StdCall)]
        static extern int SimConnect_RequestDataOnSimObject(IntPtr handle, uint requestId, uint defineId, uint objectId, uint period, uint flags, uint origin, uint interval, uint limit);

        [DllImport("SimConnect.dll", CallingConvention = CallingConvention.StdCall)]
        static extern int SimConnect_SetDataOnSimObject(IntPtr handle, uint defineId, uint objectId, uint flags, uint arrayCount, uint unitSize, IntPtr data);

        [DllImport("SimConnect.dll", CallingConvention = CallingConvention.StdCall)]
        static extern int SimConnect_GetNextDispatch(IntPtr handle, out IntPtr data, out uint size);

        static Thread worker;
        static volatile bool stop;
        static volatile string status = "SimConnect: starting";
        static Func<bool> enabled;
        static string dllPath;

        // values published to the simulator (name -> value); written at every connection, every ping and when they change
        static readonly object outLock = new object();
        static readonly Dictionary<string, double> outputs = new Dictionary<string, double>();
        static volatile bool outDirty;

        // variables the program watches (the LVAR counters of the bridge pack and the context): name, old value, new value, first value after the connection
        static string[] watch = new string[0];
        static volatile int watchVersion;
        public static Action<string, double, double, bool> OnWatch;
        static int exceptionsLogged;

        public static void SetWatch(List<string> names)
        {
            string[] fresh = names.ToArray();
            string[] old = watch;
            bool same = old.Length == fresh.Length;
            for (int i = 0; same && i < fresh.Length; i++) if (old[i] != fresh[i]) same = false;
            if (same) return;
            watch = fresh;
            watchVersion++;      // the open connection is closed and reopened with the new list
        }

        public static string Status { get { return status; } }

        // Sets a variable that the program publishes to the simulator (only the three HMC variables above).
        public static void Publish(string name, double value)
        {
            lock (outLock)
            {
                double old;
                if (!outputs.TryGetValue(name, out old) || old != value) { outputs[name] = value; outDirty = true; }
            }
        }

        // isEnabled tells whether L:AUDIOINHIBIT has to be kept at 1; the connection and the published variables do not depend on it
        public static void Start(Func<bool> isEnabled)
        {
            if (worker != null) return;
            enabled = isEnabled;
            stop = false;
            double app;
            if (!double.TryParse(AppInfo.Version, NumberStyles.Float, CultureInfo.InvariantCulture, out app)) app = 0;
            Publish(VarAppVersion, app);
            lock (outLock) { if (!outputs.ContainsKey(VarStatus)) outputs[VarStatus] = 0; if (!outputs.ContainsKey(VarPackVersion)) outputs[VarPackVersion] = 0; }
            worker = new Thread(Run);
            worker.IsBackground = true;
            worker.Name = "SimLink";
            worker.Start();
        }

        public static void Stop()
        {
            stop = true;
            Thread t = worker;
            if (t != null && t.IsAlive) t.Join(1500);
            worker = null;
        }

        // the first SimConnect.dll found: next to the program, then the MSFS SDK folders
        public static string FindDll()
        {
            string[] candidates = new string[]
            {
                Path.Combine(Ctx.BaseDir, "SimConnect.dll"),
                SdkLib(Environment.GetEnvironmentVariable("MSFS2024_SDK")),
                SdkLib(Environment.GetEnvironmentVariable("MSFS_SDK")),
                SdkLib("C:\\MSFS 2024 SDK"),
                SdkLib("C:\\MSFS SDK")
            };
            foreach (string c in candidates)
            {
                if (string.IsNullOrEmpty(c)) continue;
                try { if (File.Exists(c)) return c; } catch (Exception) { }
            }
            return null;
        }

        static string SdkLib(string root)
        {
            if (string.IsNullOrEmpty(root)) return null;
            return Path.Combine(root, "SimConnect SDK\\lib\\SimConnect.dll");
        }

        static void SetStatus(string text)
        {
            if (status == text) return;
            status = text;
        }

        static void Run()
        {
            bool dllLogged = false;
            string lastWait = null;
            while (!stop)
            {
                if (dllPath == null)
                {
                    dllPath = FindDll();
                    if (dllPath == null)
                    {
                        SetStatus("SimConnect: SimConnect.dll not found (MSFS SDK)");
                        if (!dllLogged) { dllLogged = true; Ctx.Log("[WARN] SimConnect.dll not found: copy it next to the program or install the MSFS SDK. The simulator link is off."); }
                        Sleep(5000);
                        continue;
                    }
                    try { LoadLibraryW(dllPath); } catch (Exception) { }
                    Ctx.Log("[INFO] SimConnect library: " + dllPath);
                }
                IntPtr h = IntPtr.Zero;
                try
                {
                    int rc = SimConnect_Open(out h, AppInfo.Name, IntPtr.Zero, 0, IntPtr.Zero, 0);
                    if (rc != 0 || h == IntPtr.Zero)
                    {
                        string w = "SimConnect: waiting for the simulator";
                        SetStatus(w);
                        if (lastWait != w) { lastWait = w; Ctx.Log("[INFO] " + w + " (checking every " + PingSeconds + " s)"); }
                        Sleep(PingSeconds * 1000);
                        continue;
                    }
                    lastWait = null;
                    Session(h);
                }
                catch (DllNotFoundException)
                {
                    dllPath = null;
                    SetStatus("SimConnect: SimConnect.dll cannot be loaded");
                    Ctx.Log("[ERROR] SimConnect.dll cannot be loaded (wrong bitness or missing).");
                    Sleep(10000);
                }
                catch (Exception ex)
                {
                    SetStatus("SimConnect: error, retrying");
                    Ctx.Log("[ERROR] SimConnect: " + ex.Message);
                    Sleep(PingSeconds * 1000);
                }
                finally
                {
                    if (h != IntPtr.Zero) { try { SimConnect_Close(h); } catch (Exception) { } }
                }
            }
        }

        static void Sleep(int ms)
        {
            int left = ms;
            while (left > 0 && !stop) { Thread.Sleep(Math.Min(100, left)); left -= 100; }
        }

        // one connection: ping every PingSeconds, read L:AUDIOINHIBIT and write 1 when it is not the wanted one; publish the HMC variables
        static void Session(IntPtr h)
        {
            if (SimConnect_AddToDataDefinition(h, DefineId, Variable, "number", DatatypeFloat64, 0f, uint.MaxValue) != 0)
            {
                Ctx.Log("[ERROR] SimConnect: cannot define " + Variable);
                return;
            }
            Dictionary<string, uint> outIds = new Dictionary<string, uint>();
            lock (outLock)
            {
                uint n = OutputDefineBase;
                foreach (string name in new List<string>(outputs.Keys))
                {
                    if (SimConnect_AddToDataDefinition(h, n, name, "number", DatatypeFloat64, 0f, uint.MaxValue) == 0) outIds[name] = n;
                    else Ctx.Log("[ERROR] SimConnect: cannot define " + name);
                    n++;
                }
                outDirty = true;
            }
            List<string> allNames = new List<string>(watch);
            if (!allNames.Contains(Variable)) allNames.Add(Variable);      // L:AUDIOINHIBIT is watched too: the simulator resets it to 0 at every flight or mission load and it must be 1 again at once
            string[] names = allNames.ToArray();
            int version = watchVersion;
            int groups = (names.Length + WatchGroup - 1) / WatchGroup;
            double[][] last = new double[groups][];
            bool[] have = new bool[groups];
            for (int g = 0; g < groups; g++)
            {
                int from = g * WatchGroup, count = Math.Min(WatchGroup, names.Length - from);
                last[g] = new double[count];
                for (int i = 0; i < count; i++)
                    if (SimConnect_AddToDataDefinition(h, WatchDefineBase + (uint)g, names[from + i], names[from + i].StartsWith("L:", StringComparison.Ordinal) ? "number" : "enum", DatatypeFloat64, 0f, uint.MaxValue) != 0) Ctx.Log("[ERROR] SimConnect: cannot define " + names[from + i]);
                if (SimConnect_RequestDataOnSimObject(h, WatchRequestBase + (uint)g, WatchDefineBase + (uint)g, ObjectUser, PeriodSimFrame, FlagChanged, 0, 0, 0) != 0)
                    Ctx.Log("[ERROR] SimConnect: cannot watch the variables of group " + (g + 1));
            }
            if (names.Length > 0) Ctx.Log("[INFO] SimConnect: watching " + names.Length + " variables");
            Ctx.Log("[INFO] SimConnect: connected to the simulator");
            SetStatus("SimConnect: connected, " + Variable + " not read yet");
            DateTime nextPing = DateTime.UtcNow;
            DateTime lastAnswer = DateTime.UtcNow;
            bool announced = false;
            while (!stop)
            {
                DateTime now = DateTime.UtcNow;
                if (watchVersion != version) { Ctx.Log("[INFO] SimConnect: the list of watched variables changed, reconnecting"); return; }
                bool inhibit = enabled();
                if (now >= nextPing)
                {
                    nextPing = now.AddSeconds(PingSeconds);
                    if (inhibit && SimConnect_RequestDataOnSimObject(h, RequestId, DefineId, ObjectUser, PeriodOnce, 0, 0, 0, 0) != 0)
                    {
                        Ctx.Log("[INFO] SimConnect: connection lost");
                        SetStatus("SimConnect: connection lost");
                        return;
                    }
                    outDirty = true;     // the simulator can reset the variables (new flight): write them again at every ping
                    if (!inhibit) { lastAnswer = now; SetStatus("SimConnect: connected (audio inhibit option off)"); }
                }
                if (inhibit && (now - lastAnswer).TotalSeconds > 30)
                {
                    Ctx.Log("[WARN] SimConnect: no answer for 30 s, reconnecting");
                    SetStatus("SimConnect: no answer, reconnecting");
                    return;
                }
                if (outDirty)
                {
                    outDirty = false;
                    Dictionary<string, double> copy;
                    lock (outLock) { copy = new Dictionary<string, double>(outputs); }
                    foreach (KeyValuePair<string, double> kv in copy)
                    {
                        uint id;
                        if (outIds.TryGetValue(kv.Key, out id) && !Write(h, id, kv.Value, kv.Key))
                        {
                            SetStatus("SimConnect: connection lost");
                            return;
                        }
                    }
                }
                IntPtr data; uint size;
                bool got = false;
                while (SimConnect_GetNextDispatch(h, out data, out size) == 0 && data != IntPtr.Zero)
                {
                    got = true;
                    int id = Marshal.ReadInt32(data, 8);
                    if (id == RecvQuit)
                    {
                        Ctx.Log("[INFO] SimConnect: the simulator closed the connection");
                        SetStatus("SimConnect: simulator closed");
                        return;
                    }
                    if (id == RecvException)
                    {
                        if (exceptionsLogged++ < 8) Ctx.Log("[WARN] SimConnect exception " + Marshal.ReadInt32(data, 12) + " (send id " + Marshal.ReadInt32(data, 16) + ", index " + Marshal.ReadInt32(data, 20) + ")");
                    }
                    else if (id == RecvSimObjectData && Marshal.ReadInt32(data, 12) >= (int)WatchRequestBase && Marshal.ReadInt32(data, 12) < (int)WatchRequestBase + groups)
                    {
                        int g = Marshal.ReadInt32(data, 12) - (int)WatchRequestBase;
                        int n = Math.Min(Marshal.ReadInt32(data, 36), last[g].Length);
                        for (int i = 0; i < n; i++)
                        {
                            double v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(data, DataOffset + 8 * i));
                            if (have[g] && v == last[g][i]) continue;
                            double old = last[g][i];
                            last[g][i] = v;
                            if (names[g * WatchGroup + i] == Variable && enabled() && Math.Abs(v - Wanted) > 0.001)
                            {
                                Write(h, DefineId, Wanted, Variable);
                                Ctx.Log("[INFO] SimConnect: " + Variable + " was " + v.ToString("0.###", CultureInfo.InvariantCulture) + ", set to 1 at once");
                                SetStatus("SimConnect: connected, " + Variable + " set to 1");
                            }
                            Action<string, double, double, bool> cb = OnWatch;
                            if (cb != null) { try { cb(names[g * WatchGroup + i], old, v, !have[g]); } catch (Exception ex) { Ctx.Log("[ERROR] watch: " + ex.Message); } }
                        }
                        have[g] = true;
                    }
                    else if (id == RecvSimObjectData && Marshal.ReadInt32(data, 12) == (int)RequestId)
                    {
                        lastAnswer = DateTime.UtcNow;
                        double value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(data, DataOffset));
                        if (Math.Abs(value - Wanted) > 0.001)
                        {
                            Write(h, DefineId, Wanted, Variable);
                            Ctx.Log("[INFO] SimConnect: " + Variable + " was " + value.ToString("0.###", CultureInfo.InvariantCulture) + ", set to 1");
                            SetStatus("SimConnect: connected, " + Variable + " set to 1");
                        }
                        else
                        {
                            if (!announced) { announced = true; Ctx.Log("[INFO] SimConnect: " + Variable + " is already 1"); }
                            SetStatus("SimConnect: connected, " + Variable + " = 1");
                        }
                    }
                }
                if (!got) Thread.Sleep(100);
            }
        }

        // false when the connection is gone
        static bool Write(IntPtr h, uint define, double value, string name)
        {
            IntPtr mem = Marshal.AllocHGlobal(8);
            try
            {
                Marshal.WriteInt64(mem, BitConverter.DoubleToInt64Bits(value));
                int rc = SimConnect_SetDataOnSimObject(h, define, ObjectUser, 0, 1, 8, mem);
                if (rc != 0) Ctx.Log("[ERROR] SimConnect: writing " + name + " failed (" + rc + ")");
                return rc == 0;
            }
            finally { Marshal.FreeHGlobal(mem); }
        }
    }
}
