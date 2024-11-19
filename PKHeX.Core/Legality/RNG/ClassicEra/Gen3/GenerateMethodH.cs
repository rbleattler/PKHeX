using System;

namespace PKHeX.Core;

/// <summary>
/// Generator methods for Generation 3.
/// </summary>
public static class GenerateMethodH
{
    public static void SetRandom<T>(this T enc, PK3 pk, PersonalInfo3 pi, EncounterCriteria criteria, uint seed)
        where T : IEncounterSlot3
    {
        var gr = pi.Gender;
        var ability = criteria.GetAbilityFromNumber(AbilityPermission.Any12);
        var (min, max) = SlotMethodH.GetRange(enc.Type, enc.SlotNumber);
        bool checkProc = MethodH.IsEncounterCheckApplicable(enc.Type);

        // Generate Method H correlated PID and IVs, no lead (keep things simple).
        while (true)
        {
            if (checkProc)
            {
                var check = new LeadSeed(seed, LeadRequired.None);
                if (!MethodH.CheckEncounterActivation(enc, ref check))
                {
                    seed = LCRNG.Next(seed);
                    continue;
                }
            }
            var esv = LCRNG.Next16(ref seed) % 100;
            if (esv < min || esv > max)
                continue;
            var lv = LCRNG.Next16(ref seed);
            var nature = LCRNG.Next16(ref seed) % 25;
            if (criteria.IsSpecifiedNature() && nature != (byte)criteria.Nature)
                continue;

            while (true)
            {
                var a = LCRNG.Next16(ref seed);
                var b = LCRNG.Next16(ref seed);
                var pid = GetPIDRegular(a, b);
                if (pid % 25 != nature)
                    continue;
                if ((pid & 1) != ability)
                    break; // try again
                var gender = EntityGender.GetFromPIDAndRatio(pid, gr);
                if (!criteria.IsGenderSatisfied(gender))
                    break; // try again

                pk.MetLevel = pk.CurrentLevel = (byte)MethodH.GetRandomLevel(enc, lv, LeadRequired.None);
                SetPIDIVSequential(pk, pid, seed);
                return;
            }
        }
    }

    public static void SetRandomUnown<T>(this T enc, PK3 pk, EncounterCriteria criteria)
       where T : INumberedSlot, ISpeciesForm
    {
        //bool checkForm = forms.Contains(criteria.Form); // not a property :(
        var (min, max) = SlotMethodH.GetRangeGrass(enc.SlotNumber);
        var seed = Util.Rand32();
        // Can't game the seed with % 100 increments as Unown's form calculation is based on the PID.

        while (true)
        {
            var esv = LCRNG.Next16(ref seed) % 100;
            if (esv < min || esv > max)
                continue;
            // Skip the level roll, always results in the same level value.
            seed = LCRNG.Next(seed);
            // Nature is not used in the loop.

            while (true)
            {
                var a = LCRNG.Next16(ref seed);
                var b = LCRNG.Next16(ref seed);
                var pid = a << 16 | b;
                var form = EntityPID.GetUnownForm3(pid);
                if (form != enc.Form)
                    continue;

                // Check the nature is what the user requested.
                if (criteria.IsSpecifiedNature() && pid % 25 != (byte)criteria.Nature)
                    break;

                SetPIDIVSequential(pk, pid, seed);
                return;
            }
        }
    }

    public static bool SetFromIVs<T>(this T enc, PK3 pk, EncounterCriteria criteria)
        where T : IEncounterSlot3
    {
        var gr = pk.PersonalInfo.Gender;
        criteria.GetCombinedIVs(out var iv1, out var iv2);
        Span<uint> all = stackalloc uint[LCRNG.MaxCountSeedsIV];
        var count = LCRNGReversal.GetSeedsIVs(all, iv1 << 16, iv2 << 16);
        var seeds = all[..count];
        foreach (ref var seed in seeds)
        {
            seed = LCRNG.Prev2(seed);
            var s = seed;

            var a = LCRNG.Next16(ref s);
            var b = LCRNG.Next16(ref s);
            var pid = GetPIDRegular(a, b);
            if (criteria.IsSpecifiedNature() && (Nature)(pid % 25) != criteria.Nature)
            {
                // Try again as Method 2 (AB-DE)
                var o = seed >> 16;
                pid = GetPIDRegular(o, a);
                if (criteria.IsSpecifiedNature() && (Nature)(pid % 25) != criteria.Nature)
                    continue;
                seed = LCRNG.Prev(seed);
            }

            var gender = EntityGender.GetFromPIDAndRatio(pid, gr);
            if (!criteria.IsGenderSatisfied(gender))
                continue;
            var lead = MethodH.GetSeed(enc, seed, pk.E, gender, criteria.ForceMinLevelRange);
            if (!lead.IsValid()) // Verifies the slot, (min) level, and nature loop; if it passes, apply the details.
                continue;

            // always level rand
            {
                var lv = MethodH.SkipToLevelRand(enc, lead.Seed) >> 16;
                var actual = MethodH.GetRandomLevel(enc, lv, lead.Lead);
                if (pk.MetLevel != actual)
                    pk.MetLevel = pk.CurrentLevel = (byte)actual;
            }

            pk.PID = pid;
            pk.IV32 = ((iv2 & 0x7FFF) << 15) | (iv1 & 0x7FFF);
            pk.RefreshAbility((int)(pid & 1));
            return true;
        }

        // Try again as Method 4 (ABC-E)
        count = LCRNGReversalSkip.GetSeedsIVs(all, iv1 << 16, iv2 << 16);
        seeds = all[..count];
        foreach (ref var seed in seeds)
        {
            seed = LCRNG.Prev2(seed);
            var s = seed;

            var a = LCRNG.Next16(ref s);
            var b = LCRNG.Next16(ref s);
            var pid = GetPIDRegular(a, b);
            if (criteria.IsSpecifiedNature() && (Nature)(pid % 25) != criteria.Nature)
                continue;

            var gender = EntityGender.GetFromPIDAndRatio(pid, gr);
            if (!criteria.IsGenderSatisfied(gender))
                continue;
            var lead = MethodH.GetSeed(enc, seed, pk.E, gender, criteria.ForceMinLevelRange);
            if (!lead.IsValid()) // Verifies the slot and nature loop; if it passes, apply the details.
                continue;

            // always level rand
            {
                var lv = MethodH.SkipToLevelRand(enc, lead.Seed) >> 16;
                var actual = MethodH.GetRandomLevel(enc, lv, lead.Lead);
                if (pk.MetLevel != actual)
                    pk.MetLevel = pk.CurrentLevel = (byte)actual;
            }

            pk.PID = pid;
            pk.IV32 = ((iv2 & 0x7FFF) << 15) | (iv1 & 0x7FFF);
            pk.RefreshAbility((int)(pid & 1));
            return true;
        }

        return false;
    }

    public static bool SetFromIVsUnown<T>(this T enc, PK3 pk, EncounterCriteria criteria)
        where T : IEncounterSlot3
    {
        criteria.GetCombinedIVs(out var iv1, out var iv2);
        Span<uint> all = stackalloc uint[LCRNG.MaxCountSeedsIV];
        var count = LCRNGReversal.GetSeedsIVs(all, iv1 << 16, iv2 << 16);
        var seeds = all[..count];
        foreach (ref var seed in seeds)
        {
            seed = LCRNG.Prev2(seed);
            var s = seed;

            var a = LCRNG.Next16(ref s);
            var b = LCRNG.Next16(ref s);
            var pid = GetPIDUnown(a, b);
            if ((criteria.IsSpecifiedNature() && (Nature)(pid % 25) != criteria.Nature) || EntityPID.GetUnownForm3(pid) != enc.Form)
            {
                // Try again as Method 2 (BA-DE)
                var o = seed >> 16;
                pid = GetPIDUnown(o, a);
                if (criteria.IsSpecifiedNature() && (Nature)(pid % 25) != criteria.Nature)
                    continue;
                var form = EntityPID.GetUnownForm3(pid);
                if (form != enc.Form)
                    continue;
                seed = LCRNG.Prev(seed);
            }
            var lead = MethodH.GetSeed(enc, seed, false, 2, criteria.ForceMinLevelRange);
            if (!lead.IsValid()) // Verifies the slot and form loop; if it passes, apply the details.
                continue;

            // Level is always 25, and no need to consider ability (always slot 0, not dual ability).
            pk.PID = pid;
            pk.IV32 = ((iv2 & 0x7FFF) << 15) | (iv1 & 0x7FFF);
            return true;
        }

        // Try again as Method 4 (BAC-E)
        count = LCRNGReversalSkip.GetSeedsIVs(all, iv1 << 16, iv2 << 16);
        seeds = all[..count];
        foreach (ref var seed in seeds)
        {
            seed = LCRNG.Prev2(seed);
            var s = seed;

            var a = LCRNG.Next16(ref s);
            var b = LCRNG.Next16(ref s);
            var pid = GetPIDUnown(a, b);
            if (criteria.IsSpecifiedNature() && (Nature)(pid % 25) != criteria.Nature)
                continue;
            var form = EntityPID.GetUnownForm3(pid);
            if (form != enc.Form)
                continue;
            var lead = MethodH.GetSeed(enc, seed, false, 2, criteria.ForceMinLevelRange);
            if (!lead.IsValid()) // Verifies the slot and form loop; if it passes, apply the details.
                continue;

            // Level is always 25, and no need to consider ability (always slot 0, not dual ability).
            pk.PID = pid;
            pk.IV32 = ((iv2 & 0x7FFF) << 15) | (iv1 & 0x7FFF);
            return true;
        }

        return false;
    }

    public static uint GetPIDUnown(uint a, uint b) => a << 16 | b;
    public static uint GetPIDRegular(uint a, uint b) => b << 16 | a;

    private static void SetPIDIVSequential(PK3 pk, uint pid, uint rand)
    {
        pk.PID = pid;
        var iv1 = LCRNG.Next16(ref rand);
        var iv2 = LCRNG.Next16(ref rand);
        pk.IV32 = ((iv2 & 0x7FFF) << 15) | (iv1 & 0x7FFF);
        pk.RefreshAbility((int)(pid & 1));
    }
}
