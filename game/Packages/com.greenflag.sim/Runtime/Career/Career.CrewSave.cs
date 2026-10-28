using System;
using System.Collections.Generic;
using System.Linq;

namespace GreenFlag.Sim
{
    // The crew's part of the save file (format version 4, roadmap step 39): the four seats, this race's candidates and a
    // rival's offer for one of your crew. Base's crew levels and perks are still written, but on load they are rebuilt from
    // the seats so the two can't disagree.
    public sealed partial class Career
    {
        static JsonValue MemberJson(CrewMember m) => JsonValue.Obj().Set("role", m.Role.ToString()).Set("name", m.Name).Set("lvl", m.Lvl).Set("xp", m.Xp)
            .Set("trait", m.Trait).Set("wage", m.Wage).Set("perks", Ints(m.Perks)).Set("since", m.Since).Set("kept", m.Kept);

        static CrewMember MemberFrom(JsonValue j)
        {
            var m = new CrewMember
            {
                Role = j["role"].Enum(CrewRole.CrewChief), Name = j["name"].Str("Crew"), Lvl = j["lvl"].Int(3), Xp = Math.Max(0, j["xp"].Int()), Trait = j["trait"].Str(),
                Wage = Math.Max(0, j["wage"].Int()), Since = j["since"].Int(1), Kept = j["kept"].Int(),
            };
            if (m.Lvl < 1 || m.Lvl > CrewRules.Max) throw new FormatException($"Save file: {m.Name} is level {m.Lvl}, outside 1 to {CrewRules.Max}");
            if (m.Trait != null && !CrewHq.Traits.ContainsKey(m.Trait)) throw new FormatException($"Save file: no crew trait {m.Trait}");
            var p = IntsFrom(j["perks"]);
            if (p.Count != 3 || p.Any(x => x < 0 || x > 2)) throw new FormatException($"Save file: {m.Name} needs three perk picks from 0 to 2");
            m.Perks = p.ToArray();
            return m;
        }

        static JsonValue CandJson(StaffCandidate h) => JsonValue.Obj().Set("role", h.Role.ToString()).Set("name", h.Name).Set("lvl", h.Lvl).Set("trait", h.Trait).Set("fee", h.Fee).Set("wage", h.Wage);
        static StaffCandidate CandFrom(JsonValue j)
        {
            var h = new StaffCandidate { Role = j["role"].Enum(CrewRole.CrewChief), Name = j["name"].Str("Crew"), Lvl = j["lvl"].Int(3), Trait = j["trait"].Str(), Fee = j["fee"].Int(), Wage = j["wage"].Int() };
            if (h.Trait != null && !CrewHq.Traits.ContainsKey(h.Trait)) throw new FormatException($"Save file: no crew trait {h.Trait}");
            return h;
        }

        static JsonValue PoachJson(PoachOffer o) => o == null ? JsonValue.Null : JsonValue.Obj().Set("role", o.Role.ToString()).Set("name", o.Name).Set("team", o.Team).Set("teamName", o.TeamName)
            .Set("rating", o.Rating).Set("was", o.Was).Set("wage", o.Wage).Set("matchAt", o.MatchAt).Set("buyout", o.Buyout).Set("season", o.Season).Set("after", o.After)
            .Set("talk", NegJson(o.Talk)).Set("done", o.Done).Set("outcome", o.Outcome);

        static PoachOffer PoachFrom(JsonValue j) => j.IsNull ? null : new PoachOffer
        {
            Role = j["role"].Enum(CrewRole.CrewChief), Name = j["name"].Str(""), Team = j["team"].Int(), TeamName = j["teamName"].Str(""), Rating = j["rating"].Int(),
            Was = j["was"].Int(), Wage = j["wage"].Int(), MatchAt = j["matchAt"].Int(), Buyout = j["buyout"].Int(), Season = j["season"].Int(1), After = j["after"].Int(),
            Talk = NegFrom(j["talk"]), Done = j["done"].Bool(), Outcome = j["outcome"].Str(),
        };

        static JsonValue CrewXpJson(Dictionary<CrewRole, int> x)
        {
            var j = JsonValue.Obj();
            foreach (var r in CrewRules.Roles) if (x.TryGetValue(r, out var n)) j.Set(r.ToString(), n);
            return j;
        }

        static Dictionary<CrewRole, int> CrewXpFrom(JsonValue j)
        {
            var d = new Dictionary<CrewRole, int>();
            if (j.IsNull) return d; // a race from before step 39
            foreach (var kv in j.Fields) d[JsonValue.Of(kv.Key).Enum(CrewRole.CrewChief)] = kv.Value.Int();
            return d;
        }

        void CrewToJson(JsonValue j)
        {
            var st = JsonValue.Arr();
            foreach (var r in CrewRules.Roles) st.Add(MemberJson(Staff[r]));
            var hire = JsonValue.Arr();
            foreach (var h in Hire) hire.Add(CandJson(h));
            j.Set("staff", st).Set("hire", hire).Set("hireFor", hireFor).Set("poach", PoachJson(Poach)).Set("poached", Poached);
        }

        void CrewFromJson(JsonValue j)
        {
            Staff.Clear();
            foreach (var m in j["staff"].Items.Select(MemberFrom))
            {
                if (Staff.ContainsKey(m.Role)) throw new FormatException($"Save file: two people in the {CrewRules.Name(m.Role).ToLowerInvariant()} seat");
                Staff[m.Role] = m;
            }
            foreach (var r in CrewRules.Roles) if (!Staff.ContainsKey(r)) throw new FormatException($"Save file: nobody in the {CrewRules.Name(r).ToLowerInvariant()} seat");
            Hire = j["hire"].Items.Select(CandFrom).ToList();
            hireFor = j["hireFor"].Int(-1);
            Poach = PoachFrom(j["poach"]);
            Poached = j["poached"].Int();
            SyncCrew();
        }

        /// <summary>
        /// Save format 3 to 4 (step 39): a version 3 career had crew levels but no people. Each seat gets a volunteer at the
        /// level the career raced with (the pit crew at the crew chief's when it had none of its own), named from the career's
        /// seed, with you since season 1, with no wage, trait or perks (there was no way to pick perks before). No candidates rolled yet, no rival's
        /// offer, and finished races paid no wages.
        /// </summary>
        public static JsonValue MigrateV3(JsonValue c)
        {
            var b = c["base"];
            uint seed = c["seed"].UInt();
            int o = (int)(seed % (uint)CrewRules.Names.Length);
            int chief = b["crewChief"].Int(3), pit = b["pitCrew"].Int();
            var lv = new[] { chief, b["spotter"].Int(3), b["engineer"].Int(3), pit > 0 ? pit : chief };
            var st = JsonValue.Arr();
            for (int i = 0; i < 4; i++)
                st.Add(MemberJson(new CrewMember { Role = CrewRules.Roles[i], Name = CrewRules.Names[(o + i * 5) % CrewRules.Names.Length], Lvl = Math.Max(1, Math.Min(CrewRules.Max, lv[i])), Since = 1 }));
            c.Set("staff", st).Set("hire", JsonValue.Arr()).Set("hireFor", -1).Set("poach", JsonValue.Null).Set("poached", 0);
            return c;
        }
    }
}
