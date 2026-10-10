// HPG Mission Companion - downloads (Piper engine, Piper voices), the Piper voice catalog and the start with Microsoft Flight Simulator.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;

namespace HPGMissionCompanion
{
    internal static class Net
    {
        static Net()
        {
            // TLS 1.2 (3072) is not on by default in .NET Framework 4.0 to 4.5
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | (SecurityProtocolType)3072;
        }

        static HttpWebRequest NewRequest(string url)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "HPGMissionCompanion/0.2";
            req.AllowAutoRedirect = true;
            req.Timeout = 30000;
            req.ReadWriteTimeout = 60000;
            return req;
        }

        public static string GetString(string url)
        {
            using (WebResponse resp = NewRequest(url).GetResponse())
            using (StreamReader r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return r.ReadToEnd();
        }

        // Downloads to dest (through dest.part), reports progress, checks the md5 when given.
        public static void Download(string url, string dest, Action<long, long> progress, string md5, long expected)
        {
            string part = dest + ".part";
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            using (WebResponse resp = NewRequest(url).GetResponse())
            {
                long total = resp.ContentLength > 0 ? resp.ContentLength : expected;
                using (Stream src = resp.GetResponseStream())
                using (FileStream dst = new FileStream(part, FileMode.Create, FileAccess.Write))
                using (MD5 hash = MD5.Create())
                {
                    byte[] buf = new byte[81920];
                    long done = 0;
                    int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        dst.Write(buf, 0, n);
                        hash.TransformBlock(buf, 0, n, null, 0);
                        done += n;
                        if (progress != null) progress(done, total);
                    }
                    hash.TransformFinalBlock(new byte[0], 0, 0);
                    if (!string.IsNullOrEmpty(md5))
                    {
                        string got = BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
                        if (got != md5.ToLowerInvariant())
                        {
                            dst.Close();
                            File.Delete(part);
                            throw new IOException("download is corrupted (md5 differs), try again");
                        }
                    }
                }
            }
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(part, dest);
        }
    }

    internal sealed class CatalogFile
    {
        public string Path;
        public long Size;
        public string Md5;
    }

    internal sealed class CatalogVoice
    {
        public string Key;
        public string Name;
        public string LangCode;
        public string Family;
        public string Quality;
        public int Speakers;
        public List<CatalogFile> Files = new List<CatalogFile>();
        public long OnnxSize;
    }

    internal static class PiperInstaller
    {
        public const string EngineUrl = "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip";
        public const long EngineSize = 22477236;
        public const string CatalogUrl = "https://huggingface.co/rhasspy/piper-voices/raw/main/voices.json";
        public const string VoiceBase = "https://huggingface.co/rhasspy/piper-voices/resolve/main/";

        public static void InstallEngine(Action<long, long> progress)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "HPGMissionCompanion_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                string zip = Path.Combine(tmp, "piper.zip");
                Net.Download(EngineUrl, zip, progress, null, EngineSize);
                string ex = Path.Combine(tmp, "x");
                ZipFile.ExtractToDirectory(zip, ex);
                string src = Path.Combine(ex, "piper");
                if (!File.Exists(Path.Combine(src, "piper.exe"))) src = ex;
                if (!File.Exists(Path.Combine(src, "piper.exe"))) throw new IOException("piper.exe not found in the download");
                PiperEngine.StopAll();
                if (Directory.Exists(Ctx.PiperDir)) Directory.Delete(Ctx.PiperDir, true);
                CopyDir(src, Ctx.PiperDir);
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch (Exception) { }
            }
        }

        static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        public static List<CatalogVoice> LoadCatalog(string json)
        {
            List<CatalogVoice> list = new List<CatalogVoice>();
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            Dictionary<string, object> root = js.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null) return list;
            foreach (KeyValuePair<string, object> kv in root)
            {
                Dictionary<string, object> v = kv.Value as Dictionary<string, object>;
                if (v == null) continue;
                try
                {
                    CatalogVoice c = new CatalogVoice();
                    c.Key = Convert.ToString(v["key"]);
                    c.Name = Convert.ToString(v["name"]);
                    Dictionary<string, object> lang = (Dictionary<string, object>)v["language"];
                    c.LangCode = Convert.ToString(lang["code"]);
                    c.Family = Convert.ToString(lang["family"]).ToLowerInvariant();
                    c.Quality = Convert.ToString(v["quality"]);
                    c.Speakers = Convert.ToInt32(v["num_speakers"], CultureInfo.InvariantCulture);
                    foreach (KeyValuePair<string, object> f in (Dictionary<string, object>)v["files"])
                    {
                        Dictionary<string, object> fm = (Dictionary<string, object>)f.Value;
                        CatalogFile cf = new CatalogFile();
                        cf.Path = f.Key;
                        cf.Size = Convert.ToInt64(fm["size_bytes"], CultureInfo.InvariantCulture);
                        cf.Md5 = Convert.ToString(fm["md5_digest"]);
                        c.Files.Add(cf);
                        if (cf.Path.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)) c.OnnxSize = cf.Size;
                    }
                    if (c.Speakers <= 40) list.Add(c);
                }
                catch (Exception) { }
            }
            return list;
        }

        public static bool IsInstalled(CatalogVoice c)
        {
            return File.Exists(Path.Combine(Ctx.PiperVoicesDir, c.Key + ".onnx")) && File.Exists(Path.Combine(Ctx.PiperVoicesDir, c.Key + ".onnx.json"));
        }

        public static void InstallVoice(CatalogVoice c, Action<long, long> progress)
        {
            Directory.CreateDirectory(Ctx.PiperVoicesDir);
            foreach (CatalogFile f in c.Files)
            {
                string dest;
                if (f.Path.EndsWith(".onnx.json", StringComparison.OrdinalIgnoreCase)) dest = Path.Combine(Ctx.PiperVoicesDir, c.Key + ".onnx.json");
                else if (f.Path.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)) dest = Path.Combine(Ctx.PiperVoicesDir, c.Key + ".onnx");
                else if (f.Path.EndsWith("MODEL_CARD", StringComparison.OrdinalIgnoreCase)) dest = Path.Combine(Ctx.PiperVoicesDir, c.Key + ".MODEL_CARD.txt");
                else continue;
                try { Net.Download(VoiceBase + f.Path, dest, progress, f.Md5, f.Size); }
                catch (Exception)
                {
                    if (!dest.EndsWith(".MODEL_CARD.txt")) throw;
                }
            }
        }

        public static void RemoveVoice(CatalogVoice c)
        {
            PiperEngine.StopAll();
            foreach (string suffix in new string[] { ".onnx", ".onnx.json", ".MODEL_CARD.txt" })
            {
                string p = Path.Combine(Ctx.PiperVoicesDir, c.Key + suffix);
                try { if (File.Exists(p)) File.Delete(p); } catch (Exception) { }
            }
        }
    }

    // Microsoft Flight Simulator starts the programs listed in its EXE.xml. This adds or removes our entry
    // in the EXE.xml of every installed simulator (2020 and 2024, Microsoft Store and Steam).
    internal sealed class SimTarget
    {
        public string Name;
        public string Folder;      // folder of EXE.xml
        public string ExeXml { get { return Path.Combine(Folder, "EXE.xml"); } }
    }

    internal static class SimAutostart
    {
        static readonly string EntryName = AppInfo.Name;
        static readonly string[] LegacyNames = new string[] { "HPG Voice Hub" };   // entries written by the first versions

        public static List<SimTarget> Detect(string overrideRoot)
        {
            List<SimTarget> list = new List<SimTarget>();
            if (overrideRoot != null)
            {
                Add(list, "Test", overrideRoot);
                return list;
            }
            string local = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
            string roaming = Environment.GetEnvironmentVariable("APPDATA") ?? "";
            Add(list, "Microsoft Flight Simulator 2020 (Microsoft Store)", Path.Combine(local, "Packages\\Microsoft.FlightSimulator_8wekyb3d8bbwe\\LocalCache"));
            Add(list, "Microsoft Flight Simulator 2020 (Steam)", Path.Combine(roaming, "Microsoft Flight Simulator"));
            Add(list, "Microsoft Flight Simulator 2024 (Microsoft Store)", Path.Combine(local, "Packages\\Microsoft.Limitless_8wekyb3d8bbwe\\LocalCache"));
            Add(list, "Microsoft Flight Simulator 2024 (Steam)", Path.Combine(roaming, "Microsoft Flight Simulator 2024"));
            return list;
        }

        static void Add(List<SimTarget> list, string name, string folder)
        {
            if (!Directory.Exists(folder)) return;
            SimTarget t = new SimTarget();
            t.Name = name; t.Folder = folder;
            list.Add(t);
        }

        public static bool IsEnabled(SimTarget t)
        {
            try
            {
                if (!File.Exists(t.ExeXml)) return false;
                XmlDocument doc = new XmlDocument();
                doc.Load(t.ExeXml);
                return Find(doc) != null;
            }
            catch (Exception) { return false; }
        }

        static XmlNode Find(XmlDocument doc)
        {
            foreach (XmlNode n in doc.SelectNodes("//Launch.Addon"))
            {
                XmlNode name = n.SelectSingleNode("Name");
                if (name == null) continue;
                string v = name.InnerText.Trim();
                if (v == EntryName || Array.IndexOf(LegacyNames, v) >= 0) return n;
            }
            return null;
        }

        public static void Enable(SimTarget t, string exePath, string args)
        {
            XmlDocument doc = new XmlDocument();
            if (File.Exists(t.ExeXml))
            {
                string backup = t.ExeXml + ".hpgvoicehub.bak";
                if (!File.Exists(backup)) File.Copy(t.ExeXml, backup);
                doc.Load(t.ExeXml);
            }
            else
            {
                doc.LoadXml("<?xml version=\"1.0\" encoding=\"Windows-1252\"?><SimBase.Document Type=\"Launch\" version=\"1,0\"><Descr>Launch</Descr><Filename>EXE.xml</Filename><Disabled>False</Disabled><Launch.ManualLoad>False</Launch.ManualLoad></SimBase.Document>");
            }
            XmlNode entry = Find(doc);
            if (entry == null)
            {
                entry = doc.CreateElement("Launch.Addon");
                doc.DocumentElement.AppendChild(entry);
            }
            entry.RemoveAll();
            Child(doc, entry, "Name", EntryName);
            Child(doc, entry, "Disabled", "False");
            Child(doc, entry, "Path", exePath);
            Child(doc, entry, "CommandLine", args);
            Child(doc, entry, "NewConsole", "False");
            doc.Save(t.ExeXml);
        }

        static void Child(XmlDocument doc, XmlNode parent, string name, string value)
        {
            XmlElement e = doc.CreateElement(name);
            e.InnerText = value;
            parent.AppendChild(e);
        }

        public static void Disable(SimTarget t)
        {
            if (!File.Exists(t.ExeXml)) return;
            XmlDocument doc = new XmlDocument();
            doc.Load(t.ExeXml);
            XmlNode entry = Find(doc);
            if (entry == null) return;
            entry.ParentNode.RemoveChild(entry);
            doc.Save(t.ExeXml);
        }
    }
}
