using HarmonyLib;

namespace TravellersRestAccess
{
    /// <summary>
    /// Reads the tavern statistics screen aloud. The old patch targeted DGPPDBFJFNF/ELLPIGEHAFH,
    /// which the live log proved never fire (they are obfuscator DECOY clones - garbage constants,
    /// never called). The REAL display method is TavernStatsUI.UpdateInfo() - it populates every
    /// stat field from TavernServiceManager.GetWeekStats()/GetAllTimeStats(), so we Postfix it and
    /// read the same data on open. The two section labels ("última semana" / "totais") aren't real
    /// buttons, so the navigator reads them on demand via WeekStatsText()/TotalStatsText().
    /// </summary>
    public static class TavernStatsPatch
    {
        public static void Apply(HarmonyLib.Harmony harmony)
        {
            var target = AccessTools.Method(typeof(TavernStatsUI), "UpdateInfo");
            if (target != null)
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(TavernStatsPatch), nameof(UpdateInfoPostfix)));
        }

        public static string WeekStatsText()
        {
            try
            {
                var tsm = TavernServiceManager.GGFJGHHHEJC;
                if (tsm == null) return null;
                var w = tsm.GetWeekStats();
                return $"Semana: {w.customersCount} clientes, {w.satisfiedCustomers} satisfeitos, " +
                       $"{w.kickedCustomers} expulsos, receita {w.totalIncome}, custo {w.staffCost}, lucro {w.profit}.";
            }
            catch { return null; }
        }

        public static string TotalStatsText()
        {
            try
            {
                var tsm = TavernServiceManager.GGFJGHHHEJC;
                if (tsm == null) return null;
                var a = tsm.GetAllTimeStats();
                return $"Total: {a.customersCount} clientes, {a.satisfiedCustomers} satisfeitos, " +
                       $"{a.kickedCustomers} expulsos, receita {a.totalIncome}, custo {a.staffCost}, lucro {a.profit}.";
            }
            catch { return null; }
        }

        static void UpdateInfoPostfix()
        {
            try
            {
                int level = TavernReputation.GetMilestone();
                string week = WeekStatsText();
                string total = TotalStatsText();
                if (week == null && total == null) return;
                ScreenReader.Say($"Reputação nível {level}. {week} {total}", interrupt: true);
                if (Main.DebugMode) DebugLogger.LogState($"TavernStats UpdateInfo read: level={level}");
            }
            catch (System.Exception ex)
            {
                if (Main.DebugMode) DebugLogger.LogState($"TavernStatsPatch threw: {ex.Message}");
            }
        }
    }
}
