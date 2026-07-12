using System.Collections.Generic;
using UnityEngine;

namespace TravellersRestAccess
{
    // Accessibility for hunting wild animals (turkeys / crabs). A blind player can find them fine via
    // the world nav "Caça" list, but couldn't HIT them because they flee. So we FREEZE them in place;
    // then the player walks up and hits them with the mop.
    //
    // Why the earlier freeze failed: turkeys flee by RUNNING, and running uses a DIFFERENT speed field
    // (NPCWalkTo.runningSpeed / currentSpeed) than walking (NPCWalkTo.speed). The old code only zeroed
    // the walking speed + the NPC.runSpeed, so a fleeing (running) turkey kept moving. Also, zeroing
    // only every 0.25s left a gap where it could scoot. Now we cache the turkeys with a cheap staged
    // scan but RE-APPLY the freeze EVERY frame, zeroing ALL of the speed fields (speed, runningSpeed,
    // currentSpeed) plus stopping the walk (isActive=false) and killing rigidbody velocity.
    public static class HuntingHandler
    {
        private const float ScanInterval = 0.25f;

        public static bool Enabled = true;

        private static float _nextScan;

        // Cached from the last scan; the per-frame freeze re-applies to these (cheap field writes).
        private static readonly List<TurkeyNPC> _turkeys = new List<TurkeyNPC>();
        private static readonly List<CrabNPC> _crabs = new List<CrabNPC>();

        private static int _lastLoggedTurkeys = -1;
        private static int _lastLoggedCrabs = -1;

        public static void Update()
        {
            if (!Enabled) return;

            // Refresh the cached list occasionally (FindObjectsByType is expensive; don't do it every
            // frame). This is the SAME detection the "Caça" nav list uses, which the user confirmed works.
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + ScanInterval;
                Rescan();
            }

            // Re-apply the freeze EVERY frame on the cached refs so there's no window where a running
            // turkey slips away between scans. Cheap: just a handful of field writes.
            ApplyFreeze();
        }

        private static void Rescan()
        {
            _turkeys.Clear();
            _crabs.Clear();
            try
            {
                foreach (var t in Object.FindObjectsByType<TurkeyNPC>(FindObjectsSortMode.None))
                    if (t != null) _turkeys.Add(t);
            }
            catch { }
            try
            {
                foreach (var c in Object.FindObjectsByType<CrabNPC>(FindObjectsSortMode.None))
                    if (c != null) _crabs.Add(c);
            }
            catch { }

            if (Main.DebugMode && (_turkeys.Count != _lastLoggedTurkeys || _crabs.Count != _lastLoggedCrabs))
            {
                _lastLoggedTurkeys = _turkeys.Count;
                _lastLoggedCrabs = _crabs.Count;
                DebugLogger.LogState($"Hunting: found {_turkeys.Count} turkey(s), {_crabs.Count} crab(s) — freezing.");
            }
        }

        private static void ApplyFreeze()
        {
            for (int i = 0; i < _turkeys.Count; i++)
            {
                var t = _turkeys[i];
                if (t == null) continue;
                try { t.runSpeed = 0f; } catch { }
                FreezeWalkTo(t.walkTo);
                try { if (t.rb != null) t.rb.velocity = Vector2.zero; } catch { }
            }
            for (int i = 0; i < _crabs.Count; i++)
            {
                var c = _crabs[i];
                if (c == null) continue;
                try { c.runSpeed = 0f; } catch { }
                FreezeWalkTo(c.walkTo);
                try { if (c.rb != null) c.rb.velocity = Vector2.zero; } catch { }
            }
        }

        // Zero EVERY speed the walker uses. currentSpeed is the value actually applied each FixedUpdate
        // (it gets re-derived from speed OR runningSpeed depending on walk/run state), so zero all three;
        // isActive=false also short-circuits the movement step entirely.
        private static void FreezeWalkTo(NPCWalkTo w)
        {
            if (w == null) return;
            try { w.isActive = false; } catch { }
            try { w.speed = 0f; } catch { }
            try { w.runningSpeed = 0f; } catch { }
            try { w.currentSpeed = 0f; } catch { }
        }
    }
}
