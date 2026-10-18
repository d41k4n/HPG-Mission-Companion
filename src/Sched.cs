// HPG Mission Companion - speech scheduler: lanes, priorities, replaced messages, age. Only decisions, no audio and no threads of its own: the player (Engines.cs)
// and the self test (--queue-test, below) use the same code, so what the test shows is what the player does. See docs/architecture/VOICEPACKS.md ("Queue").
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace HPGMissionCompanion
{
    // the four lanes: each one speaks one line at a time, the lanes speak together (an actor never in two lanes at once)
    internal static class Lanes
    {
        public const int Radio = 0;        // radio calls and external radio calls (the dispatcher, the hospital, the crew on the radio)
        public const int Crew = 1;         // intercom and voices of the crew and of the people of the scene
        public const int Checklist = 2;    // the voice of the checklists (actor 11): it only waits for itself
        public const int Fx = 3;           // sound files: tablet tones, alarms
        public const int Count = 4;
        public static readonly string[] Names = { "radio", "crew", "checklist", "fx" };
    }

    internal sealed class SchedMeta
    {
        public int Prio = 2;               // 3 time critical, 2 operations (default), 1 conversation, 0 small talk and tips
        public int Age;                    // seconds a line may wait for its turn, 0 = for ever
        public string Topic = "";          // messages of one subject: a later one (higher Rank) replaces the earlier ones that have not been spoken yet
        public int Rank;
    }

    internal sealed class SchedItem
    {
        public int Lane;
        public int Prio = 2;
        public int Actor;
        public string Key = "";
        public string Topic = "";
        public int Rank;
        public double Born;
        public double MaxAge;
        public int Seq;
        public object Tag;
        public string Name { get { return Key.Length > 0 ? Key : "(text)"; } }
    }

    internal sealed class SchedDrop
    {
        public SchedItem Item;
        public string Why;
    }

    internal sealed class SchedCore
    {
        public readonly object Lock = new object();
        public readonly List<SchedItem>[] Pending = new List<SchedItem>[Lanes.Count];
        public readonly SchedItem[] Playing = new SchedItem[Lanes.Count];
        public bool Overlap = true;        // false: one line at a time in the whole program (the order of the old queue, but by priority)
        public int ShedMax = 4;            // lines of priority 0 and 1 that may wait in a lane; the oldest of the lowest priority are dropped beyond that
        int seq;

        public SchedCore()
        {
            for (int i = 0; i < Pending.Length; i++) Pending[i] = new List<SchedItem>();
        }

        static bool Better(SchedItem a, SchedItem b)
        {
            return a.Prio > b.Prio || (a.Prio == b.Prio && a.Seq < b.Seq);
        }

        static SchedItem Best(List<SchedItem> q)
        {
            SchedItem best = null;
            foreach (SchedItem c in q) if (best == null || Better(c, best)) best = c;
            return best;
        }

        // The caller holds Lock. Drops what the new line replaces or pushes out (they are in dropped) and returns the line that is being spoken in the same
        // lane and must be cut for the new one (a time critical line over small talk, a later message of the same subject), or null.
        public SchedItem Add(SchedItem it, List<SchedDrop> dropped)
        {
            it.Seq = ++seq;
            if (it.Key.Length > 0)      // the same message waiting twice (a callout repeated in a burst): the earlier one is out of date
            {
                for (int l = 0; l < Pending.Length; l++)
                    for (int i = Pending[l].Count - 1; i >= 0; i--)
                        if (Pending[l][i].Key == it.Key)
                        {
                            dropped.Add(new SchedDrop { Item = Pending[l][i], Why = "repeated by a later one" });
                            Pending[l].RemoveAt(i);
                        }
            }
            if (it.Topic.Length > 0)
            {
                for (int l = 0; l < Pending.Length; l++)
                    for (int i = Pending[l].Count - 1; i >= 0; i--)
                        if (Pending[l][i].Topic == it.Topic && Pending[l][i].Rank < it.Rank)
                        {
                            dropped.Add(new SchedDrop { Item = Pending[l][i], Why = "replaced by " + it.Name + " (same subject, later)" });
                            Pending[l].RemoveAt(i);
                        }
            }
            List<SchedItem> q = Pending[it.Lane];
            if (it.Prio <= 1)
            {
                while (true)
                {
                    int low = 0; SchedItem victim = null;
                    foreach (SchedItem c in q)
                    {
                        if (c.Prio > 1) continue;
                        low++;
                        if (victim == null || c.Prio < victim.Prio || (c.Prio == victim.Prio && c.Seq < victim.Seq)) victim = c;
                    }
                    if (low + 1 <= ShedMax || victim == null) break;
                    if (it.Prio < victim.Prio) { dropped.Add(new SchedDrop { Item = it, Why = "queue too long (" + Lanes.Names[it.Lane] + " lane)" }); return null; }
                    q.Remove(victim);
                    dropped.Add(new SchedDrop { Item = victim, Why = "queue too long (" + Lanes.Names[it.Lane] + " lane)" });
                }
            }
            q.Add(it);
            SchedItem cur = Playing[it.Lane];
            if (cur != null && cur.Prio <= 1 && (it.Prio == 3 || (it.Topic.Length > 0 && cur.Topic == it.Topic && cur.Rank < it.Rank))) return cur;
            return null;
        }

        void Expire(double now, List<SchedDrop> dropped)
        {
            for (int l = 0; l < Pending.Length; l++)
                for (int i = Pending[l].Count - 1; i >= 0; i--)
                {
                    SchedItem c = Pending[l][i];
                    if (c.MaxAge > 0 && now - c.Born > c.MaxAge)
                    {
                        dropped.Add(new SchedDrop { Item = c, Why = "waited " + (now - c.Born).ToString("0", CultureInfo.InvariantCulture) + " s, longer than " + c.MaxAge.ToString("0", CultureInfo.InvariantCulture) + " s" });
                        Pending[l].RemoveAt(i);
                    }
                }
        }

        bool ActorBusy(int lane, int actor)
        {
            if (actor <= 0) return false;
            for (int l = 0; l < Lanes.Count; l++) if (l != lane && Playing[l] != null && Playing[l].Actor == actor) return true;
            return false;
        }

        // The caller holds Lock. The line the lane speaks next, taken out of the queue and marked as playing, or null (nothing waits, the lane is busy, or the
        // lines that wait are of an actor who is speaking in another lane, or - Overlap off - another lane speaks or has a better line).
        public SchedItem Next(int lane, double now, List<SchedDrop> dropped)
        {
            Expire(now, dropped);
            List<SchedItem> q = Pending[lane];
            if (Playing[lane] != null || q.Count == 0) return null;
            SchedItem pick = null;
            if (!Overlap)
            {
                for (int l = 0; l < Lanes.Count; l++) if (Playing[l] != null) return null;
                pick = Best(q);
                for (int l = 0; l < Lanes.Count; l++)
                    if (l != lane && Pending[l].Count > 0 && Better(Best(Pending[l]), pick)) return null;
            }
            else
            {
                List<SchedItem> order = new List<SchedItem>(q);
                order.Sort(delegate (SchedItem a, SchedItem b) { return Better(a, b) ? -1 : (Better(b, a) ? 1 : 0); });
                foreach (SchedItem c in order) if (!ActorBusy(lane, c.Actor)) { pick = c; break; }
                if (pick == null) return null;
            }
            q.Remove(pick);
            Playing[lane] = pick;
            return pick;
        }

        public void Finished(int lane) { Playing[lane] = null; }

        // The caller holds Lock. lane -1: every lane, otherwise that lane and the lanes the interrupt reaches
        public void Clear(int lane, List<SchedDrop> dropped, string why)
        {
            for (int l = 0; l < Lanes.Count; l++)
            {
                if (lane >= 0 && l != lane) continue;
                foreach (SchedItem c in Pending[l]) dropped.Add(new SchedDrop { Item = c, Why = why });
                Pending[l].Clear();
            }
        }

        public int PendingCount()
        {
            int n = 0;
            foreach (List<SchedItem> q in Pending) n += q.Count;
            return n;
        }
    }

    // What a message is: the priority, how long it may wait, its subject. The attributes of the pack message (prio, age, topic, rank; the base pack EN when the
    // active pack has none) win, then the rules below by key, then the flags of the message (small talk, conversation). Packs of other languages need nothing.
    internal static class SchedRules
    {
        sealed class Rule
        {
            public Regex Rx; public int Prio; public int Age; public string Topic; public int Rank;
        }

        static Rule R(string rx, int prio, int age, string topic, int rank)
        {
            Rule r = new Rule();
            r.Rx = new Regex(rx, RegexOptions.Compiled);
            r.Prio = prio; r.Age = age; r.Topic = topic; r.Rank = rank;
            return r;
        }

        static readonly Rule[] Rules = {
            R("^SYS\\.", 3, 0, "", 0),
            // time critical: a warning or a callout that is useless when it is late
            R("^HW\\.", 3, 8, "", 0),
            R("^CPL\\.(42|43|44)$", 3, 3, "", 0),
            R("^CPL\\.(7|9|27|28|29|30|31|32|35|36|39|40|45|46|51)$", 3, 6, "", 0),
            R("^PLT\\.(3|17|18|19|20)$", 3, 8, "", 0),
            R("^PAX2\\.(5|6)$", 3, 6, "", 0),
            R("^PAX1\\.(16|22|24|27|28|29|30|31|32|33|34|35|36|39|40|41|43|44|45|46|47|48|66|67|68|69|70|71|72|73|74|75|T[0-9]+)$", 3, 4, "", 0),
            // the state of the cabin: preparing, then secured, then ready: the later one replaces the earlier ones that wait
            R("^PAX1\\.(2|23)$", 1, 20, "cabin", 1),
            R("^PAX1\\.(1|3|54)$", 2, 45, "cabin", 2),
            R("^PAX1\\.(59|81)$", 2, 45, "cabin", 3),
            // the narration of the hoist: late it is no use
            R("^PAX1\\.(4|5|6|7|8|9|10|11|12|13|14|15|17|18|19|20|21|26|42|49|50|51|52|53|55|56|57|58)$", 2, 25, "", 0),
            // the end of the flight and the debrief
            R("^PLT\\.(10|2[1-7])$", 1, 40, "", 0),
            // conversations, the decision about a dispatch, small talk, tips
            R("^DSA\\.", 1, 30, "", 0),
            R("^(BRF|APR|HRB)\\.", 1, 25, "", 0),
            R("^BAN\\.", 0, 6, "", 0),
            R("^TIP\\.", 0, 12, "", 0),
            // the checklists have their own lane and never expire
            R("^CHK\\.", 2, 0, "", 0)
        };

        // key "" = a text with no key
        public static SchedMeta Classify(string key, PackMessage msg)
        {
            SchedMeta m = new SchedMeta();
            if (key == null) key = "";
            PackMessage baseMsg = null;
            if (key.Length > 0)
            {
                VoicePack reference = PackCheck.Reference();
                if (reference != null) reference.Messages.TryGetValue(key, out baseMsg);
            }
            bool done = false;
            PackMessage[] sources = new PackMessage[] { msg, baseMsg };
            foreach (PackMessage s in sources)
            {
                if (s == null || done) continue;
                if (s.Prio >= 0 || s.Age >= 0 || s.Topic.Length > 0)
                {
                    if (s.Prio >= 0) m.Prio = s.Prio;
                    if (s.Age >= 0) m.Age = s.Age;
                    m.Topic = s.Topic; m.Rank = s.Rank;
                    done = true;
                }
            }
            if (done) return m;
            foreach (Rule r in Rules)
                if (key.Length > 0 && r.Rx.IsMatch(key)) { m.Prio = r.Prio; m.Age = r.Age; m.Topic = r.Topic; m.Rank = r.Rank; return m; }
            PackMessage flags = msg != null ? msg : baseMsg;
            if (flags != null && flags.Futile) { m.Prio = 0; m.Age = 6; }
            else if (flags != null && flags.Group.Length > 0) { m.Prio = 1; m.Age = 25; }
            return m;
        }

        // the lane of a line from what the tag parser made of it
        public static int LaneOf(Segment s)
        {
            if (s == null) return Lanes.Crew;
            if (s.Actor == 11) return Lanes.Checklist;
            if (s.Radio == 1) return s.Fx == "ics" ? Lanes.Crew : Lanes.Radio;
            return Lanes.Crew;
        }
    }

    // --queue-test <report> [--log <hmc log>] [--pack <EN.xml>]: the decisions of the scheduler on made-up cases, and on the lines of a real log of the program replayed
    // with the old queue (one line at a time, in order) and with the lanes. Virtual time, no audio.
    internal static class QueueSelfTest
    {
        sealed class Line
        {
            public double Arrive, Start, Dur;
            public string Key = "", Mode = "voice", Text = "";
            public int Actor, Lane, Prio, Age;
            public string Topic = ""; public int Rank;
            public bool Counter;      // the line came from a simulator counter (the old queue dropped it after 30 s)
            public bool Matched;
            public PackMessage Msg;
        }

        static int failures;

        static void Check(StringBuilder rep, bool ok, string what)
        {
            if (!ok) failures++;
            rep.AppendLine((ok ? "PASS " : "FAIL ") + what);
        }

        static SchedItem It(int lane, int prio, int actor, double born, double maxAge, string key, string topic, int rank)
        {
            SchedItem i = new SchedItem();
            i.Lane = lane; i.Prio = prio; i.Actor = actor; i.Born = born; i.MaxAge = maxAge; i.Key = key; i.Topic = topic; i.Rank = rank;
            return i;
        }

        public static int Run(string reportPath, string logPath, string packPath, bool smoke)
        {
            StringBuilder rep = new StringBuilder();
            failures = 0;
            Synthetic(rep);
            if (logPath != null) Replay(rep, logPath, packPath);
            if (smoke) PlayerSmoke(rep);
            rep.AppendLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
            File.WriteAllText(reportPath, rep.ToString());
            return failures == 0 ? 0 : 1;
        }

        static void Synthetic(StringBuilder rep)
        {
            rep.AppendLine("== decisions on made-up cases ==");
            List<SchedDrop> drops = new List<SchedDrop>();
            SchedCore c = new SchedCore();
            SchedItem a, b, d;

            // priority order inside a lane, then arrival
            c.Add(It(Lanes.Crew, 1, 2, 0, 0, "BRF.X", "", 0), drops); c.Add(It(Lanes.Crew, 3, 2, 1, 0, "CPL.51", "", 0), drops); c.Add(It(Lanes.Crew, 3, 3, 2, 0, "CPL.43", "", 0), drops);
            a = c.Next(Lanes.Crew, 2.5, drops); c.Finished(Lanes.Crew); b = c.Next(Lanes.Crew, 2.5, drops); c.Finished(Lanes.Crew); d = c.Next(Lanes.Crew, 2.5, drops); c.Finished(Lanes.Crew);
            Check(rep, a.Key == "CPL.51" && b.Key == "CPL.43" && d.Key == "BRF.X", "priority first, then arrival order");

            // a line that waited longer than its age is dropped, the others are kept
            c = new SchedCore(); drops.Clear();
            c.Add(It(Lanes.Crew, 1, 2, 0, 10, "BRF.OLD", "", 0), drops); c.Add(It(Lanes.Crew, 2, 2, 0, 0, "DSC.1.1", "", 0), drops);
            a = c.Next(Lanes.Crew, 15, drops);
            Check(rep, a != null && a.Key == "DSC.1.1" && drops.Count == 1 && drops[0].Item.Key == "BRF.OLD", "an old line of priority 1 is dropped, a line with no age is kept");

            // a later message of the same subject replaces the earlier ones that wait
            c = new SchedCore(); drops.Clear();
            c.Add(It(Lanes.Crew, 1, 4, 0, 20, "PAX1.2", "cabin", 1), drops);
            c.Add(It(Lanes.Crew, 2, 4, 1, 45, "PAX1.59", "cabin", 3), drops);
            Check(rep, c.Pending[Lanes.Crew].Count == 1 && c.Pending[Lanes.Crew][0].Key == "PAX1.59" && drops.Count == 1, "cabin ready replaces cabin preparing that has not been spoken");
            c.Add(It(Lanes.Crew, 1, 4, 2, 20, "PAX1.2", "cabin", 1), drops);
            Check(rep, c.Pending[Lanes.Crew].Count == 2, "an earlier message of the subject does not replace a later one");

            // lanes speak together, an actor never in two lanes at once
            c = new SchedCore(); drops.Clear();
            c.Add(It(Lanes.Radio, 2, 8, 0, 0, "DSC.1.1", "", 0), drops); c.Add(It(Lanes.Crew, 2, 2, 0, 0, "CPL.5", "", 0), drops); c.Add(It(Lanes.Checklist, 2, 11, 0, 0, "CHK.a.01", "", 0), drops);
            a = c.Next(Lanes.Radio, 0, drops); b = c.Next(Lanes.Crew, 0, drops); d = c.Next(Lanes.Checklist, 0, drops);
            Check(rep, a != null && b != null && d != null, "radio, crew and checklist speak together");
            c.Add(It(Lanes.Radio, 2, 2, 0, 0, "HOS.X", "", 0), drops);
            c.Finished(Lanes.Radio);
            Check(rep, c.Next(Lanes.Radio, 0, drops) == null, "the copilot is speaking in the crew lane: his radio line waits");
            c.Finished(Lanes.Crew);
            Check(rep, c.Next(Lanes.Radio, 0, drops) != null, "...and goes when he has finished");

            // the checklist only waits for itself
            c = new SchedCore(); drops.Clear();
            c.Add(It(Lanes.Checklist, 2, 11, 0, 0, "CHK.a.01", "", 0), drops); c.Add(It(Lanes.Checklist, 2, 11, 0, 0, "CHK.a.02", "", 0), drops);
            c.Add(It(Lanes.Crew, 2, 1, 0, 0, "PLT.4", "", 0), drops);
            a = c.Next(Lanes.Checklist, 0, drops); b = c.Next(Lanes.Checklist, 0, drops); d = c.Next(Lanes.Crew, 0, drops);
            Check(rep, a != null && b == null && d != null, "the second checklist line waits for the first, the crew line does not wait for the checklist");

            // a time critical line cuts small talk in the same lane, not an operation line
            c = new SchedCore(); drops.Clear();
            c.Add(It(Lanes.Crew, 0, 2, 0, 6, "BAN.1", "", 0), drops); c.Next(Lanes.Crew, 0, drops);
            SchedItem cut = c.Add(It(Lanes.Crew, 3, 2, 1, 5, "CPL.51", "", 0), drops);
            Check(rep, cut != null && cut.Key == "BAN.1", "a time critical line cuts the small talk that is speaking");
            c = new SchedCore(); drops.Clear();
            c.Add(It(Lanes.Crew, 2, 2, 2, 0, "DSC.9.1", "", 0), drops); c.Next(Lanes.Crew, 2, drops);
            Check(rep, c.Add(It(Lanes.Crew, 3, 2, 3, 5, "CPL.43", "", 0), drops) == null, "...but not an operation line");

            // too many small talk lines waiting: the oldest of the lowest priority are dropped
            c = new SchedCore(); drops.Clear(); c.ShedMax = 3;
            for (int i = 0; i < 5; i++) c.Add(It(Lanes.Crew, i % 2, 2, i, 0, "X." + i, "", 0), drops);
            Check(rep, c.Pending[Lanes.Crew].Count == 3 && drops.Count == 2, "no more than 3 lines of priority 0 and 1 wait in a lane");
            Check(rep, drops[0].Item.Prio == 0, "the lowest priority goes first");

            // Overlap off: one line in the whole program, the best waiting line first
            c = new SchedCore(); drops.Clear(); c.Overlap = false;
            c.Add(It(Lanes.Crew, 1, 2, 0, 0, "BRF.1", "", 0), drops); c.Add(It(Lanes.Radio, 2, 8, 1, 0, "DSC.1.1", "", 0), drops);
            a = c.Next(Lanes.Crew, 1, drops);
            Check(rep, a == null, "overlap off: the radio line (better) goes before the crew line");
            b = c.Next(Lanes.Radio, 1, drops);
            Check(rep, b != null && c.Next(Lanes.Crew, 1, drops) == null, "overlap off: only one lane speaks");

            // rules
            SchedMeta m = SchedRules.Classify("CPL.43", null);
            Check(rep, m.Prio == 3 && m.Age == 3, "CPL.43 (V callout) is priority 3 and 3 s");
            m = SchedRules.Classify("PAX1.2", null);
            Check(rep, m.Prio == 1 && m.Topic == "cabin" && m.Rank == 1, "PAX1.2 (cabin preparing) is subject cabin rank 1");
            m = SchedRules.Classify("PAX1.59", null);
            Check(rep, m.Prio == 2 && m.Topic == "cabin" && m.Rank == 3, "PAX1.59 (cabin ready) is subject cabin rank 3");
            m = SchedRules.Classify("DSC.17.2", null);
            Check(rep, m.Prio == 2 && m.Age == 0, "a dispatch description is priority 2 and never expires");
            m = SchedRules.Classify("CHK.interiorckl.02", null);
            Check(rep, m.Prio == 2 && m.Age == 0, "a checklist row never expires");
            m = SchedRules.Classify("", null);
            Check(rep, m.Prio == 2 && m.Age == 0, "a text with no key is priority 2");
            Segment s = new Segment(); s.Actor = 11;
            Check(rep, SchedRules.LaneOf(s) == Lanes.Checklist, "actor 11 is the checklist lane");
            s = new Segment(); s.Actor = 1; s.Radio = 1; s.Fx = "ics";
            Check(rep, SchedRules.LaneOf(s) == Lanes.Crew, "the intercom is the crew lane");
            s = new Segment(); s.Actor = 8; s.Radio = 1; s.Fx = "ext";
            Check(rep, SchedRules.LaneOf(s) == Lanes.Radio, "the external radio is the radio lane");
            s = new Segment(); s.Actor = 2; s.Radio = 1;
            Check(rep, SchedRules.LaneOf(s) == Lanes.Radio, "a radio call is the radio lane");
        }

        // The real player with real voices and no sound (Ctx.Silent): lines for every lane, with the rendering of the engines, must all be spoken in their lane
        // with no error, and the checklist line must not wait for the others.
        static void PlayerSmoke(StringBuilder rep)
        {
            rep.AppendLine();
            rep.AppendLine("== the player with real voices, no sound ==");
            List<string> log = new List<string>();
            Action<string> oldLog = Ctx.Log;
            bool oldSilent = Ctx.Silent;
            Player oldPlayer = Ctx.Player;
            Ctx.Log = delegate (string s) { lock (log) { log.Add(s); } };
            try
            {
                VoiceLibrary.Refresh();
                Ctx.Silent = true;
                Ctx.Player = new Player();
                Ctx.Player.EnqueueText("<actor8><radio:ext>Dispatcher to Pelikan, a radio line of the dispatcher.", false);
                Ctx.Player.EnqueueText("<actor2><ics>Copilot on the intercom, a crew line.", false);
                Ctx.Player.EnqueueText("<actor11>Checklist voice, the first row of a list.", false);
                Ctx.Player.EnqueueText("<actor11>Checklist voice, the second row of a list.", false);
                Ctx.Player.EnqueueText("<actor1><radio>Pilot on the radio, a call.", false);
                DateTime end = DateTime.UtcNow.AddSeconds(60);
                while (DateTime.UtcNow < end && !Ctx.Player.Idle()) System.Threading.Thread.Sleep(100);
                System.Threading.Thread.Sleep(300);
                Check(rep, Ctx.Player.Idle(), "every lane is idle after the lines (no line stuck)");
            }
            finally
            {
                Ctx.Log = oldLog; Ctx.Silent = oldSilent; Ctx.Player = oldPlayer;
            }
            int voice = 0, error = 0; bool noVoice = false;
            string[] lanesSeen = new string[Lanes.Count];
            lock (log)
                foreach (string s in log)
                {
                    if (s.StartsWith("[VOICE]", StringComparison.Ordinal))
                    {
                        voice++;
                        for (int l = 0; l < Lanes.Count; l++) if (s.Contains("[lane " + Lanes.Names[l] + ",")) lanesSeen[l] = "yes";
                    }
                    if (s.StartsWith("[ERROR]", StringComparison.Ordinal)) { error++; if (s.Contains("no voice available")) noVoice = true; rep.AppendLine("  " + s); }
                }
            if (noVoice) { rep.AppendLine("SKIP no voice is installed on this PC: the player was not exercised"); return; }
            Check(rep, voice == 5, "five lines spoken (" + voice + ")");
            Check(rep, error == 0, "no error in the log");
            Check(rep, lanesSeen[Lanes.Radio] != null && lanesSeen[Lanes.Crew] != null && lanesSeen[Lanes.Checklist] != null, "the radio, crew and checklist lanes each spoke");
        }

        // ---- the replay of a log ----
        static readonly Regex LineRx = new Regex("^(\\d\\d):(\\d\\d):(\\d\\d)\\.(\\d+) \\[(\\w+)\\] (.*)$", RegexOptions.Compiled);
        static readonly Regex VoiceRx = new Regex("^actor (\\d+), ([A-Z]{2}), (.*?)(, intercom|, external radio|, radio)?(?: \\(([^)]*)\\))?: (.*)$", RegexOptions.Compiled);
        static readonly Regex SayRx = new Regex("^(\\S+) -> option", RegexOptions.Compiled);
        static readonly Regex RenderRx = new Regex("^(\\d+) ms, (\\d+) KB", RegexOptions.Compiled);
        static readonly Regex KeyTagRx = new Regex("<(?:say|text):([A-Za-z0-9_.\\-]+)", RegexOptions.Compiled);

        static string Norm(string s)
        {
            return Regex.Replace(Regex.Replace((s ?? "").ToLowerInvariant(), "<[^>]*>", " "), "[^a-z0-9]+", " ").Trim();
        }

        sealed class Arrival { public double T; public string Key; public string Text; public bool Counter; public bool Used; }

        static List<Regex> KeyRegexes(VoicePack pack, string key, Dictionary<string, List<Regex>> cache)
        {
            List<Regex> list;
            if (cache.TryGetValue(key, out list)) return list;
            list = new List<Regex>();
            PackMessage pm;
            if (pack != null && pack.Messages.TryGetValue(key, out pm))
                foreach (string o in pm.Options)
                {
                    string n = Norm(Regex.Replace(o, "\\{[^}]*\\}", " PLACEHOLDER "));
                    if (n.Length == 0) continue;
                    list.Add(new Regex("^" + Regex.Escape(n).Replace("placeholder", ".*") + "$", RegexOptions.Compiled));
                }
            cache[key] = list;
            return list;
        }

        static void Replay(StringBuilder rep, string logPath, string packPath)
        {
            rep.AppendLine();
            rep.AppendLine("== replay of " + Path.GetFileName(logPath) + " ==");
            VoicePack en = null;
            if (packPath != null && File.Exists(packPath)) en = VoicePacks.Load(packPath);
            if (en == null || !en.Usable) en = VoicePacks.Find("EN");
            Dictionary<string, List<Regex>> regexCache = new Dictionary<string, List<Regex>>();

            List<Arrival> arrivals = new List<Arrival>();
            List<KeyValuePair<double, string>> rawSay = new List<KeyValuePair<double, string>>();     // texts of the mission with <say:KEY>: the log also has the [SAY] line of the same message
            List<Line> lines = new List<Line>();
            Line last = null;
            foreach (string raw in File.ReadAllLines(logPath, Encoding.UTF8))
            {
                Match m = LineRx.Match(raw);
                if (!m.Success) continue;
                double t = int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + int.Parse(m.Groups[3].Value) + double.Parse("0." + m.Groups[4].Value, CultureInfo.InvariantCulture);
                string tag = m.Groups[5].Value, body = m.Groups[6].Value;
                if (tag == "SAY")
                {
                    Match sm = SayRx.Match(body);
                    if (!sm.Success) continue;
                    string key = sm.Groups[1].Value;
                    bool fromMission = rawSay.Exists(delegate (KeyValuePair<double, string> k) { return k.Value == key && t - k.Key < 1.5 && t - k.Key >= 0; });
                    Arrival a = new Arrival(); a.T = t; a.Key = key; a.Counter = !fromMission; arrivals.Add(a);
                }
                else if (tag == "RAW")
                {
                    if (!body.Contains("\"Text\"")) continue;
                    Match km = Regex.Match(body, "<say:([A-Za-z0-9_.\\-]+)");
                    if (km.Success) { rawSay.Add(new KeyValuePair<double, string>(t, km.Groups[1].Value)); continue; }
                    Match tm = Regex.Match(body, "<text:([A-Za-z0-9_.\\-]+)");
                    Arrival a = new Arrival(); a.T = t; a.Key = tm.Success ? tm.Groups[1].Value : ""; a.Text = body; arrivals.Add(a);
                }
                else if (tag == "VOICE")
                {
                    Match vm = VoiceRx.Match(body);
                    if (!vm.Success) continue;
                    Line ln = new Line();
                    ln.Start = t; ln.Actor = int.Parse(vm.Groups[1].Value); ln.Text = vm.Groups[6].Value;
                    string mode = vm.Groups[4].Value;
                    ln.Mode = mode == ", intercom" ? "ics" : (mode == ", external radio" ? "ext" : (mode == ", radio" ? "radio" : "voice"));
                    lines.Add(ln); last = ln;
                }
                else if (tag == "RENDER" && last != null)
                {
                    Match rm = RenderRx.Match(body);
                    if (rm.Success) last.Dur = int.Parse(rm.Groups[1].Value) / 1000.0 + int.Parse(rm.Groups[2].Value) * 1024.0 / 44100.0 + 0.3;
                    last = null;
                }
            }
            // the arrival of every spoken line: the first one not used yet, before the start, whose message has this text (or whose own text it is)
            foreach (Line ln in lines)
            {
                string n = Norm(ln.Text);
                Arrival best = null;
                foreach (Arrival a in arrivals)
                {
                    if (a.Used || a.T > ln.Start + 0.5) continue;
                    bool hit = false;
                    if (a.Text == null && a.Key.Length > 0) { foreach (Regex rx in KeyRegexes(en, a.Key, regexCache)) if (rx.IsMatch(n)) { hit = true; break; } }
                    else if (a.Text != null && n.Length > 3 && Norm(a.Text).Contains(n)) hit = true;
                    if (hit) { best = a; break; }
                }
                if (best == null) { ln.Arrive = ln.Start; continue; }
                best.Used = true; ln.Matched = true;
                ln.Arrive = best.T; ln.Key = best.Key; ln.Counter = best.Counter;
                if (ln.Dur <= 0) ln.Dur = 4;
            }
            foreach (Line ln in lines) if (ln.Dur <= 0) ln.Dur = 4;
            // the window of the report: from the first line that waited more than 3 s to the end of the last one
            double first = double.MaxValue, lastT = 0;
            foreach (Line ln in lines) if (ln.Start - ln.Arrive > 3) { first = Math.Min(first, ln.Arrive); lastT = Math.Max(lastT, ln.Start + ln.Dur); }
            if (first == double.MaxValue) { rep.AppendLine("no line waited more than 3 s in this log: nothing to compare"); return; }
            List<Line> win = lines.FindAll(delegate (Line x) { return x.Arrive >= first - 1 && x.Arrive <= lastT; });
            rep.AppendLine(win.Count + " lines arrive between " + Clock(first) + " and " + Clock(lastT) + " (" + lines.Count + " spoken in the log)");
            if (win.Count == 0) return;

            foreach (Line ln in win)
            {
                PackMessage pm = null;
                if (en != null && ln.Key.Length > 0 && ln.Key != "") en.Messages.TryGetValue(ln.Key, out pm);
                ln.Msg = pm;
                SchedMeta meta = SchedRules.Classify(ln.Key, pm);
                ln.Prio = meta.Prio; ln.Age = meta.Age; ln.Topic = meta.Topic; ln.Rank = meta.Rank;
                ln.Lane = ln.Actor == 11 ? Lanes.Checklist : (ln.Mode == "ics" || ln.Mode == "voice" ? Lanes.Crew : Lanes.Radio);
            }
            Result oldR = Simulate(win, false, true);
            Result newR = Simulate(win, true, false);
            rep.AppendLine();
            rep.AppendLine("The old queue is modelled (one line at a time, in order, counters dropped after 30 s): its start times differ from the log by " + oldR.Err.ToString("0.0", CultureInfo.InvariantCulture) + " s on average.");
            rep.AppendLine("Lines that waited 2 s or more with either queue, or were not spoken (durations from the size of the rendered sound, 44100 bytes a second assumed):");
            rep.AppendLine("  time     lane      P  message      actor  length   old queue wait   new queue");
            foreach (Line ln in win)
            {
                double ow = oldR.Wait.ContainsKey(ln) ? oldR.Wait[ln] : -1, nw = newR.Wait.ContainsKey(ln) ? newR.Wait[ln] : -1;
                if (ow < 2 && nw < 2 && nw >= 0) continue;
                rep.AppendLine("  " + Clock(ln.Arrive) + " " + Lanes.Names[ln.Lane].PadRight(9) + " " + ln.Prio + "  " + (ln.Key.Length > 0 ? ln.Key : "(text)").PadRight(12) + " a" + ln.Actor.ToString().PadRight(3) + " " + ln.Dur.ToString("0.0", CultureInfo.InvariantCulture).PadLeft(5) + " s   " + (ow >= 0 ? ow.ToString("0.0", CultureInfo.InvariantCulture).PadLeft(6) + " s" : "   not spoken").PadRight(15) + "  " + (nw >= 0 ? "waits " + nw.ToString("0.0", CultureInfo.InvariantCulture) + " s" : "NOT SPOKEN: " + (newR.Dropped.ContainsKey(ln) ? newR.Dropped[ln] : "?")));
            }
            rep.AppendLine();
            rep.AppendLine("                         old queue   new queue");
            rep.AppendLine("lines spoken             " + oldR.Played.ToString().PadLeft(9) + "   " + newR.Played.ToString().PadLeft(9) + "   of " + win.Count);
            rep.AppendLine("longest wait (s)         " + oldR.MaxWait.ToString("0.0", CultureInfo.InvariantCulture).PadLeft(9) + "   " + newR.MaxWait.ToString("0.0", CultureInfo.InvariantCulture).PadLeft(9));
            rep.AppendLine("lines that waited > 10 s " + oldR.Over10.ToString().PadLeft(9) + "   " + newR.Over10.ToString().PadLeft(9));
            rep.AppendLine("total wait (s)           " + oldR.SumWait.ToString("0", CultureInfo.InvariantCulture).PadLeft(9) + "   " + newR.SumWait.ToString("0", CultureInfo.InvariantCulture).PadLeft(9));
            Check(rep, newR.MaxWait <= oldR.MaxWait && newR.SumWait <= oldR.SumWait, "the new queue waits no longer than the old one on this log");
            Check(rep, oldR.Err < 3.0, "the model of the old queue follows the log (mean difference under 3 s)");
        }

        sealed class Result
        {
            public Dictionary<Line, double> Wait = new Dictionary<Line, double>();
            public Dictionary<Line, string> Dropped = new Dictionary<Line, string>();
            public int Played, Over10;
            public double MaxWait, SumWait, Err;
        }
        static string Clock(double t)
        {
            return ((int)(t / 3600)).ToString("00") + ":" + ((int)(t % 3600 / 60)).ToString("00") + ":" + ((int)(t % 60)).ToString("00");
        }

        // the lines of the window through the scheduler in virtual time (oldQueue: one lane, one line at a time, in order, counters dropped after 30 s)
        static Result Simulate(List<Line> win, bool lanes, bool oldQueue)
        {
            Result res = new Result();
            SchedCore core = new SchedCore();
            core.Overlap = lanes;
            double[] busyUntil = new double[Lanes.Count];
            SchedItem[] playing = new SchedItem[Lanes.Count];
            List<Line> order = new List<Line>(win);
            order.Sort(delegate (Line a, Line b) { return a.Arrive.CompareTo(b.Arrive); });
            Dictionary<SchedItem, Line> map = new Dictionary<SchedItem, Line>();
            double t = order[0].Arrive, sumErr = 0;
            int next = 0, nErr = 0, guard = 0;
            while (guard++ < 200000)
            {
                while (next < order.Count && order[next].Arrive <= t + 1e-9)      // the lines that arrive now
                {
                    Line ln = order[next++];
                    SchedItem it = new SchedItem();
                    if (oldQueue) { it.Lane = Lanes.Crew; it.Prio = 2; it.MaxAge = ln.Counter ? 30 : 0; it.Actor = 0; }
                    else { it.Lane = ln.Lane; it.Prio = ln.Prio; it.MaxAge = ln.Age > 0 ? (ln.Counter ? Math.Min(ln.Age, 30) : ln.Age) : (ln.Counter ? 30 : 0); it.Topic = ln.Topic; it.Rank = ln.Rank; it.Actor = ln.Actor; it.Key = ln.Key; }
                    it.Born = ln.Arrive;
                    map[it] = ln;
                    List<SchedDrop> dr = new List<SchedDrop>();
                    SchedItem cut = core.Add(it, dr);
                    foreach (SchedDrop d in dr) res.Dropped[map[d.Item]] = d.Why;
                    if (cut != null) busyUntil[cut.Lane] = t;
                }
                for (int l = 0; l < Lanes.Count; l++)                              // the lanes that have finished
                    if (playing[l] != null && busyUntil[l] <= t + 1e-9) { core.Finished(l); playing[l] = null; }
                bool again = true;
                while (again)                                                       // the lanes that start a line
                {
                    again = false;
                    for (int l = 0; l < Lanes.Count; l++)
                    {
                        if (playing[l] != null) continue;
                        List<SchedDrop> dr = new List<SchedDrop>();
                        SchedItem it = core.Next(l, t, dr);
                        foreach (SchedDrop d in dr) res.Dropped[map[d.Item]] = d.Why;
                        if (it == null) continue;
                        Line ln = map[it];
                        playing[l] = it; busyUntil[l] = t + ln.Dur; res.Played++;
                        double wait = t - ln.Arrive;
                        res.Wait[ln] = wait; res.MaxWait = Math.Max(res.MaxWait, wait); res.SumWait += wait;
                        if (wait > 10) res.Over10++;
                        if (oldQueue) { sumErr += Math.Abs(t - ln.Start); nErr++; }
                        again = true;
                    }
                }
                double nt = double.MaxValue;                                        // the next moment something happens
                if (next < order.Count) nt = order[next].Arrive;
                for (int l = 0; l < Lanes.Count; l++) if (playing[l] != null && busyUntil[l] > t + 1e-9) nt = Math.Min(nt, busyUntil[l]);
                if (nt == double.MaxValue)
                {
                    if (core.PendingCount() == 0) break;
                    nt = t + 0.5;      // a line waits for a lane or an actor: look again
                }
                t = nt;
            }
            res.Err = nErr > 0 ? sumErr / nErr : 0;
            return res;
        }
    }
}