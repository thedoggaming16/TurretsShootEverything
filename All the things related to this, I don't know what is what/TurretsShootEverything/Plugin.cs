using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace TurretsShootEverything
{
    internal static class ModInfo
    {
        public const string GUID = "yourname.TurretsShootEverything"; // change "yourname"
        public const string NAME = "TurretsShootEverything";
        public const string VERSION = "1.0.0";
    }

    [BepInPlugin(ModInfo.GUID, ModInfo.NAME, ModInfo.VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static Settings Cfg;

        private void Awake()
        {
            Log = Logger;
            Cfg = new Settings(Config);
            new Harmony(ModInfo.GUID).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{ModInfo.NAME} {ModInfo.VERSION} loaded.");
        }

        // Runs after every plugin has been loaded, so we can see which companion mods are present.
        private void Start()
        {
            foreach (var info in Chainloader.PluginInfos.Values)
            {
                string name = info.Metadata.Name ?? "";
                if (name.IndexOf("ToilHead", StringComparison.OrdinalIgnoreCase) >= 0)
                    Log.LogInfo($"Detected ToilHead ({info.Metadata.GUID}) - turret-head enemies will be treated like any other enemy.");
                if (name.IndexOf("FairAI", StringComparison.OrdinalIgnoreCase) >= 0)
                    Log.LogInfo($"Detected FairAI ({info.Metadata.GUID}) - no AI methods are patched by this mod, so there is no overlap.");
            }
        }
    }

    internal class Settings
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<float> Range;
        public readonly ConfigEntry<int> Damage;
        public readonly ConfigEntry<float> FireInterval;
        public readonly ConfigEntry<float> ChargeTime;
        public readonly ConfigEntry<bool> SpareEnemiesWithOwnTurret;
        public readonly ConfigEntry<string> IgnoredEnemies;

        private HashSet<string> _ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public Settings(ConfigFile cfg)
        {
            Enabled = cfg.Bind("General", "Enabled", true, "Turrets also shoot enemies/monsters (players are still handled by the vanilla turret logic).");
            Range = cfg.Bind("Targeting", "Range", 30f, new ConfigDescription("Maximum distance at which turrets pick enemy targets.", new AcceptableValueRange<float>(1f, 100f)));
            Damage = cfg.Bind("Targeting", "DamagePerShot", 1, new ConfigDescription("Damage dealt to an enemy per bullet.", new AcceptableValueRange<int>(1, 50)));
            FireInterval = cfg.Bind("Targeting", "FireInterval", 0.21f, new ConfigDescription("Seconds between bullets (vanilla turret = 0.21).", new AcceptableValueRange<float>(0.05f, 2f)));
            ChargeTime = cfg.Bind("Targeting", "ChargeTime", 1.0f, new ConfigDescription("Seconds a turret tracks an enemy before it starts firing.", new AcceptableValueRange<float>(0f, 5f)));
            SpareEnemiesWithOwnTurret = cfg.Bind("Compatibility", "SpareEnemiesWithOwnTurret", false,
                "If true, enemies that carry their own Turret component (e.g. ToilHead's turret-heads) are NOT targeted by other turrets.");
            IgnoredEnemies = cfg.Bind("Compatibility", "IgnoredEnemies", "",
                "Comma-separated enemy names (EnemyType.enemyName) that turrets should never shoot. Example: Docile Locust Bees,Manticoil");
            IgnoredEnemies.SettingChanged += (_, __) => Rebuild();
            Rebuild();
        }

        private void Rebuild()
        {
            _ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in IgnoredEnemies.Value.Split(','))
            {
                var t = s.Trim();
                if (t.Length > 0) _ignored.Add(t);
            }
        }

        public bool IsIgnored(string enemyName) => _ignored.Contains(enemyName ?? "");
    }

    /// <summary>
    /// Only a postfix is used (the original method always runs), so this stays compatible with
    /// other mods that patch Turret. It just makes sure every turret has our targeting component.
    /// </summary>
    [HarmonyPatch(typeof(Turret), "Update")]
    internal static class TurretUpdatePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Turret __instance)
        {
            if (!__instance.TryGetComponent<TurretEnemyTargeting>(out _))
                __instance.gameObject.AddComponent<TurretEnemyTargeting>();
        }
    }

    /// <summary>
    /// Host-authoritative enemy targeting. Runs only while the vanilla turret is idle
    /// (Detection mode), so players always keep priority and vanilla behaviour is untouched.
    /// </summary>
    internal class TurretEnemyTargeting : MonoBehaviour
    {
        private Turret _turret;
        private EnemyAI _target;
        private float _scanTimer, _chargeTimer, _fireTimer, _lostTimer;

        private void Awake() => _turret = GetComponent<Turret>();

        private void LateUpdate()
        {
            if (_turret == null || !Plugin.Cfg.Enabled.Value) return;

            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return; // host decides damage

            // (int)0 == Detection. Compared as int so it works whether the enum is nested or not.
            if (!_turret.turretActive || (int)_turret.turretMode != 0)
            {
                ResetTarget();
                return;
            }

            _scanTimer -= Time.deltaTime;
            if (_target == null || !IsValid(_target))
            {
                ResetTarget();
                if (_scanTimer <= 0f)
                {
                    _scanTimer = 0.25f;
                    _target = FindTarget();
                }
                if (_target == null) return;
            }

            Vector3 point = AimPoint(_target);
            if (!HasLineOfSight(point))
            {
                _lostTimer += Time.deltaTime;
                if (_lostTimer > 0.5f) ResetTarget();
                return;
            }
            _lostTimer = 0f;

            RotateTowards(point);

            _chargeTimer += Time.deltaTime;
            if (_chargeTimer < Plugin.Cfg.ChargeTime.Value) return;

            _fireTimer -= Time.deltaTime;
            if (_fireTimer <= 0f)
            {
                _fireTimer = Plugin.Cfg.FireInterval.Value;
                Shoot(point);
            }
        }

        private void ResetTarget()
        {
            _target = null;
            _chargeTimer = 0f;
            _fireTimer = 0f;
            _lostTimer = 0f;
        }

        private static Vector3 AimPoint(EnemyAI e) => e.transform.position + Vector3.up * 1.0f;

        private Vector3 Origin() =>
            _turret.aimPoint != null ? _turret.aimPoint.position : _turret.transform.position + Vector3.up * 1.5f;

        private bool IsValid(EnemyAI e)
        {
            if (e == null || e.isEnemyDead || !e.gameObject.activeInHierarchy) return false;
            if (e.enemyType == null || !e.enemyType.canDie) return false;
            if (Plugin.Cfg.IsIgnored(e.enemyType.enemyName)) return false;
            // Never shoot the creature this turret is physically attached to (e.g. turret-head enemies).
            if (_turret.GetComponentInParent<EnemyAI>() == e) return false;
            if (Plugin.Cfg.SpareEnemiesWithOwnTurret.Value && e.GetComponentInChildren<Turret>() != null) return false;
            return (e.transform.position - _turret.transform.position).sqrMagnitude
                   <= Plugin.Cfg.Range.Value * Plugin.Cfg.Range.Value;
        }

        private EnemyAI FindTarget()
        {
            var rm = RoundManager.Instance;
            if (rm == null || rm.SpawnedEnemies == null) return null;

            EnemyAI best = null;
            float bestSqr = float.MaxValue;
            foreach (var e in rm.SpawnedEnemies)
            {
                if (!IsValid(e)) continue;
                float sqr = (e.transform.position - _turret.transform.position).sqrMagnitude;
                if (sqr >= bestSqr || !HasLineOfSight(AimPoint(e))) continue;
                best = e;
                bestSqr = sqr;
            }
            return best;
        }

        private bool HasLineOfSight(Vector3 point)
        {
            var origin = Origin();
            int mask = StartOfRound.Instance != null
                ? StartOfRound.Instance.collidersAndRoomMaskAndDefault
                : Physics.DefaultRaycastLayers;
            return !Physics.Linecast(origin, point, mask, QueryTriggerInteraction.Ignore);
        }

        private void RotateTowards(Vector3 point)
        {
            var rod = _turret.turretRod;
            if (rod == null) return;
            Vector3 dir = point - rod.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return;
            rod.rotation = Quaternion.RotateTowards(rod.rotation, Quaternion.LookRotation(dir), 360f * Time.deltaTime);
        }

        private void Shoot(Vector3 point)
        {
            Vector3 dir = (point - Origin()).normalized;

            if (_turret.bulletParticles != null) _turret.bulletParticles.Play();
            if (_turret.mainAudio != null && _turret.firingSFX != null)
                _turret.mainAudio.PlayOneShot(_turret.firingSFX);

            try
            {
                _target.HitEnemyOnLocalClient(Plugin.Cfg.Damage.Value, dir, null, true, -1);
            }
            catch (Exception ex)
            {
                // Some modded enemies may not like a null "playerWhoHit"; never crash the game over it.
                Plugin.Log.LogWarning($"Hit on {_target.enemyType.enemyName} failed: {ex.Message}");
                ResetTarget();
            }
        }
    }
}
