using HarmonyLib;

namespace TravellersRestAccess
{
    /// <summary>
    /// Announces tavern statistics whenever the player opens one of the two stats
    /// screens inside the main panel (L key). Both TavernStatsUI methods load their
    /// respective data and update the UI text fields — patching their postfix means
    /// the data is guaranteed to be fresh when we read it.
    /// DGPPDBFJFNF = last-6-sessions window + all-time total.
    /// ELLPIGEHAFH = last-4-sessions window.
    /// </summary>
    public static class TavernStatsPatch
    {
        public static void Apply(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(
                AccessTools.Method(typeof(TavernStatsUI), "DGPPDBFJFNF"),
                postfix: new HarmonyMethod(typeof(TavernStatsPatch), nameof(WeeklyStatsPostfix)));

            harmony.Patch(
                AccessTools.Method(typeof(TavernStatsUI), "ELLPIGEHAFH"),
                postfix: new HarmonyMethod(typeof(TavernStatsPatch), nameof(TotalStatsPostfix)));
        }

        static void WeeklyStatsPostfix()
        {
            var tsm = TavernServiceManager.JFJOKGAOPHA();
            if (tsm == null) return;
            var recent = tsm.DAKEIGNBBBD();
            var total = tsm.FEPCCIHJPEH();
            int level = TavernReputation.GetMilestone();
            string text = $"Nível {level}. " +
                          $"Semana: {recent.customersCount} clientes, {recent.satisfiedCustomers} satisfeitos, {recent.kickedCustomers} expulsos. " +
                          $"Total: {total.customersCount} clientes, {total.satisfiedCustomers} satisfeitos, {total.kickedCustomers} expulsos.";
            ScreenReader.Say(text, interrupt: true);
            if (Main.DebugMode) DebugLogger.LogState($"TavernStats DGPPDBFJFNF: level={level}, recent={recent.customersCount}/{recent.satisfiedCustomers}/{recent.kickedCustomers}, total={total.customersCount}/{total.satisfiedCustomers}/{total.kickedCustomers}");
        }

        static void TotalStatsPostfix()
        {
            var tsm = TavernServiceManager.JFJOKGAOPHA();
            if (tsm == null) return;
            var stats = tsm.GEMIEGAFJMI();
            int level = TavernReputation.GetMilestone();
            string text = $"Nível {level}. " +
                          $"Sessões recentes: {stats.customersCount} clientes, {stats.satisfiedCustomers} satisfeitos, {stats.kickedCustomers} expulsos.";
            ScreenReader.Say(text, interrupt: true);
            if (Main.DebugMode) DebugLogger.LogState($"TavernStats ELLPIGEHAFH: level={level}, stats={stats.customersCount}/{stats.satisfiedCustomers}/{stats.kickedCustomers}");
        }
    }
}
