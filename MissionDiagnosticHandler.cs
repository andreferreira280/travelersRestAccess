using UnityEngine;

namespace TravellersRestAccess
{
    /// <summary>
    /// TEMPORARY investigation aid for the lost-cat quest "Oh Bring Back My Jacky to Me".
    /// Jacky is scene/quest-scripted (not in the CatNPC pet system and not named in decompiled
    /// source), so I can't wire the feature blind. This logs, while in the city, every ACTIVE
    /// mission's id + localized name + stage, plus any nearby object whose name hints at a cat
    /// (cat/jack/barrel/cart/tree). Once the user activates the quest and walks the 3 spots, the
    /// log gives the exact quest id and Jacky's GameObject name/position so the real feature
    /// (list under "Pendentes" while active, proximity announce, "aperte F" at the tree) can be
    /// built on facts. Remove once that's done.
    /// </summary>
    public class MissionDiagnosticHandler
    {
        private float _nextLog = -999f;
        private string _lastSig = "";
        private const float Interval = 2f;

        public void Update()
        {
            if (Time.unscaledTime < _nextLog) return;
            _nextLog = Time.unscaledTime + Interval;

            PlayerController p;
            try { p = PlayerController.GetPlayer(1); } catch { return; }
            if (p == null) return;
            Location loc = p.LEOIMFNKFGA;
            bool cityish = loc == Location.City || loc == Location.CityOutside || loc == Location.CityTavern
                || loc == Location.PetShop || loc == Location.Sawmill || loc == Location.Blacksmith
                || loc == Location.Bathhouse || loc == Location.BathhouseInterior;
            if (!cityish) return;

            var mm = MissionsManager.instance;
            if (mm == null || mm.activeMissions == null) return;

            // Build a signature so we only log when the active-mission set / stage changes.
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < mm.activeMissions.Count; i++)
            {
                var am = mm.activeMissions[i];
                if (am == null || am.mission == null) continue;
                sb.Append(am.mission.id).Append(':').Append(am.currentAmount).Append('|');
            }
            string sig = sb.ToString() + "@" + loc;
            if (sig == _lastSig) return;
            _lastSig = sig;

            MelonLoader.MelonLogger.Msg($"MissionDiag: location={loc} activeMissions={mm.activeMissions.Count}");
            for (int i = 0; i < mm.activeMissions.Count; i++)
            {
                var am = mm.activeMissions[i];
                if (am == null || am.mission == null) continue;
                string name = "?";
                try { name = am.mission.IABAKHPEOAF(); } catch { }
                MelonLoader.MelonLogger.Msg(
                    $"MissionDiag: mission id={am.mission.id} name=\"{name}\" amount={am.currentAmount} " +
                    $"objetivos={(am.completedObjectives != null ? am.completedObjectives.Count : 0)}");
            }

            // Light scan for candidate cat objects near the player (bounded to NPCs so it stays cheap).
            Vector3 pp = PlayerController.GetPlayerPosition(1);
            NPC[] npcs;
            try { npcs = Object.FindObjectsByType<NPC>(FindObjectsSortMode.None); }
            catch { return; }
            foreach (var npc in npcs)
            {
                if (npc == null) continue;
                string n = npc.gameObject.name;
                string ln = n.ToLowerInvariant();
                if (ln.Contains("cat") || ln.Contains("jack") || ln.Contains("jacky"))
                {
                    float d = Vector3.Distance(pp, npc.transform.position);
                    MelonLoader.MelonLogger.Msg(
                        $"MissionDiag: candidato NPC \"{n}\" pos={npc.transform.position} dist={d:F1} tipo={npc.GetType().Name}");
                }
            }
        }
    }
}
