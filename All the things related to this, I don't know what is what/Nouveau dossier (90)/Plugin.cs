using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using Unity.Netcode;
using UnityEngine;

namespace TurretsShootEverything
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.example.turretsshooteverything";
        public const string Name = "Turrets Shoot Everything";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<float> Range;
        internal static ConfigEntry<float> ViewAngle;
        internal static ConfigEntry<float> FireInterval;
        internal static ConfigEntry<int> Damage;
        internal static ConfigEntry<string> Blacklist;
        internal static ConfigEntry<bool> TargetEnemyTurrets;

        private float _timer;

        private void Awake()
        {
            Log = Logger;
            Range = Config.Bind("Targeting", "Range", 30f, "Max distance (m) at which turrets engage monsters.");
            ViewAngle = Config.Bind("Targeting", "ViewAngle", 180f, "Total cone angle (degrees) in front of the turret in which monsters are detected. 360 = all around.");
            FireInterval = Config.Bind("Targeting", "FireInterval", 0.21f, "Seconds between shots (vanilla turret = 0.21).");
            Damage = Config.Bind("Targeting", "Damage", 1, "Damage dealt to a monster per shot (monster HP is usually 1-10).");
            Blacklist = Config.Bind("Compatibility", "EnemyBlacklist", "",
                "Comma separated enemy names (enemyType.enemyName) that turrets must NOT shoot. Useful for modded enemies.");
            TargetEnemyTurrets = Config.Bind("Compatibility", "TargetEnemiesWithTurrets", false,
                "If false, monsters that carry their own Turret (e.g. ToilHead) are ignored, so that mod keeps working normally.");

            Log.LogInfo($"{Name} {Version} loaded.");
        }

        private void Start()
        {
            // Soft-detection only: no hard dependency, so the mod loads with or without them.
            foreach (var info in Chainloader.PluginInfos.Values)
            {
                string n = info.Metadata.Name ?? "";
                if (n.IndexOf("ToilHead", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("FairAI", StringComparison.OrdinalIgnoreCase) >= 0)
                    Log.LogInfo($"Detected compatible mod: {n}");
            }
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < 2f) return;
            _timer = 0f;

            foreach (var turret in FindObjectsOfType<Turret>())
            {
                if (turret.GetComponent<TurretEnemyTargeting>() != null) continue;
                // Turrets that belong to an enemy (ToilHead etc.) are left alone.
                if (turret.GetComponentInParent<EnemyAI>() != null) continue;
                turret.gameObject.AddComponent<TurretEnemyTargeting>();
            }
        }
    }

    /// <summary>
    /// Added to every map turret. Runs on all clients (aiming/effects) and only the host applies damage.
    /// It never replaces vanilla behaviour: while the turret is busy with a player it does nothing.
    /// </summary>
    public class TurretEnemyTargeting : MonoBehaviour
    {
        private const int LosMask = 1 | 256 | 2048; // Default + Room + Colliders

        private Turret _turret;
        private EnemyAI _target;
        private float _scanTimer;
        private float _fireTimer;

        private void Awake() => _turret = GetComponent<Turret>();

        private void LateUpdate()
        {
            if (_turret == null || RoundManager.Instance == null) return;

            // (int)turretMode == 0 -> Detection. Any other mode = vanilla is shooting a player, don't interfere.
            if (!_turret.turretActive || (int)_turret.turretMode != 0 || _turret.targetPlayerWithRotation != null)
            {
                _target = null;
                return;
            }

            _scanTimer += Time.deltaTime;
            if (_target == null || !IsValid(_target) || _scanTimer > 0.25f)
            {
                _scanTimer = 0f;
                _target = FindTarget();
            }
            if (_target == null) return;

            Vector3 aim = Center(_target);

            // Rotate the barrel towards the monster
            Transform rod = _turret.turretRod;
            if (rod != null)
            {
                Vector3 dir = aim - rod.position;
                if (dir.sqrMagnitude > 0.01f)
                    rod.rotation = Quaternion.RotateTowards(rod.rotation, Quaternion.LookRotation(dir), 360f * Time.deltaTime);
            }

            _fireTimer += Time.deltaTime;
            if (_fireTimer < Plugin.FireInterval.Value) return;
            _fireTimer = 0f;

            PlayEffects();

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                try { _target.HitEnemy(Plugin.Damage.Value, null, true); }
                catch (Exception e) { Plugin.Log.LogWarning($"HitEnemy failed on {_target.name}: {e.Message}"); }
            }
        }

        private void PlayEffects()
        {
            try
            {
                if (_turret.bulletParticles != null) _turret.bulletParticles.Play();
                if (_turret.mainAudio != null && _turret.firingSFX != null)
                    _turret.mainAudio.PlayOneShot(_turret.firingSFX);
            }
            catch { /* cosmetic only */ }
        }

        private static Vector3 Center(EnemyAI e) => e.transform.position + Vector3.up * 1f;

        private Transform Origin => _turret.aimPoint != null ? _turret.aimPoint : _turret.transform;

        private HashSet<string> BlacklistSet() =>
            new HashSet<string>(Plugin.Blacklist.Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0),
                StringComparer.OrdinalIgnoreCase);

        private bool IsValid(EnemyAI e)
        {
            if (e == null || e.isEnemyDead || !e.gameObject.activeInHierarchy) return false;
            if (e.enemyType != null && !e.enemyType.canDie) return false;
            if (!Plugin.TargetEnemyTurrets.Value && e.GetComponentInChildren<Turret>() != null) return false; // ToilHead
            if (e.enemyType != null && BlacklistSet().Contains(e.enemyType.enemyName)) return false;

            Vector3 from = Origin.position;
            Vector3 to = Center(e);
            Vector3 d = to - from;
            if (d.magnitude > Plugin.Range.Value) return false;

            if (Plugin.ViewAngle.Value < 359f &&
                Vector3.Angle(_turret.transform.forward, d) > Plugin.ViewAngle.Value * 0.5f) return false;

            return !Physics.Linecast(from, to, LosMask, QueryTriggerInteraction.Ignore);
        }

        private EnemyAI FindTarget()
        {
            EnemyAI best = null;
            float bestDist = float.MaxValue;
            foreach (var e in RoundManager.Instance.SpawnedEnemies)
            {
                if (!IsValid(e)) continue;
                float dist = (e.transform.position - Origin.position).sqrMagnitude;
                if (dist < bestDist) { bestDist = dist; best = e; }
            }
            return best;
        }
    }
}
