// SimConnect link: keeps the local variable L:AUDIOINHIBIT at 1 while the simulator is connected.
// The native SimConnect.dll is not part of this repository: it comes from the MSFS SDK (see THIRD_PARTY.md);
// tools\build.cmd copies it next to the executable and the program also looks for it in the SDK folders.
using System;
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

        const uint DefineId = 1;
        const uint RequestId = 1;
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
        static volatile string status = "SimConnect: off";
        static Func<bool> enabled;
        static string dllPath;

        public static string Status { get { return status; } }

        public static void Start(Func<bool> isEnabled)
        {
            if (worker != null) return;
            enabled = isEnabled;
            stop = false;
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
                if (!enabled())
                {
                    SetStatus("SimConnect: off (option not ticked)");
                    Sleep(500);
                    continue;
                }
                if (dllPath == null)
                {
                    dllPath = FindDll();
                    if (dllPath == null)
                    {
                        SetStatus("SimConnect: SimConnect.dll not found (MSFS SDK)");
                        if (!dllLogged) { dllLogged = true; Ctx.Log("[WARN] SimConnect.dll not found: copy it next to the program or install the MSFS SDK. L:AUDIOINHIBIT will not be set."); }
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

        // one connection: ping every PingSeconds, read the value, write it when it is not the wanted one
        static void Session(IntPtr h)
        {
            if (SimConnect_AddToDataDefinition(h, DefineId, Variable, "number", DatatypeFloat64, 0f, uint.MaxValue) != 0)
            {
                Ctx.Log("[ERROR] SimConnect: cannot define " + Variable);
                return;
            }
            Ctx.Log("[INFO] SimConnect: connected to the simulator");
            SetStatus("SimConnect: connected, " + Variable + " not read yet");
            DateTime nextPing = DateTime.UtcNow;
            DateTime lastAnswer = DateTime.UtcNow;
            bool announced = false;
            while (!stop && enabled())
            {
                DateTime now = DateTime.UtcNow;
                if (now >= nextPing)
                {
                    nextPing = now.AddSeconds(PingSeconds);
                    if (SimConnect_RequestDataOnSimObject(h, RequestId, DefineId, ObjectUser, PeriodOnce, 0, 0, 0, 0) != 0)
                    {
                        Ctx.Log("[INFO] SimConnect: connection lost");
                        SetStatus("SimConnect: connection lost");
                        return;
                    }
                }
                if ((now - lastAnswer).TotalSeconds > 30)
                {
                    Ctx.Log("[WARN] SimConnect: no answer for 30 s, reconnecting");
                    SetStatus("SimConnect: no answer, reconnecting");
                    return;
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
                        Ctx.Log("[WARN] SimConnect exception " + Marshal.ReadInt32(data, 12) + " (send id " + Marshal.ReadInt32(data, 16) + ")");
                    }
                    else if (id == RecvSimObjectData && Marshal.ReadInt32(data, 12) == (int)RequestId)
                    {
                        lastAnswer = DateTime.UtcNow;
                        double value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(data, DataOffset));
                        if (Math.Abs(value - Wanted) > 0.001)
                        {
                            Write(h, Wanted);
                            Ctx.Log("[INFO] SimConnect: " + Variable + " was " + value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ", set to 1");
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

        static void Write(IntPtr h, double value)
        {
            IntPtr mem = Marshal.AllocHGlobal(8);
            try
            {
                Marshal.WriteInt64(mem, BitConverter.DoubleToInt64Bits(value));
                int rc = SimConnect_SetDataOnSimObject(h, DefineId, ObjectUser, 0, 1, 8, mem);
                if (rc != 0) Ctx.Log("[ERROR] SimConnect: writing " + Variable + " failed (" + rc + ")");
            }
            finally { Marshal.FreeHGlobal(mem); }
        }
    }
}
