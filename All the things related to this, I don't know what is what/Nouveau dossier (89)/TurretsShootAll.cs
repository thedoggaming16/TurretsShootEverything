using BepInEx;
using BepInEx.Configuration;
using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace TurretsShootAll
{
    [BepInPlugin("com.yourname.turretsshootall", "Turrets Shoot All", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ConfigEntry<bool> TargetEnemies;
        internal static ConfigEntry<bool> TargetPlayers;
        internal static ConfigEntry<float> Range;
        internal static ConfigEntry<float> FireInterval;
        internal static ConfigEntry<int> EnemyDamage;
        internal static ConfigEntry<int> PlayerDamage;

        private void Awake()
        {
            TargetEnemies = Config.Bind("General", "TargetEnemies", true, "Turrets shoot monsters.");
            TargetPlayers = Config.Bind("General", "TargetPlayers", true, "Turrets shoot players (360 degrees, ignores the vanilla detection cone).");
            Range = Config.Bind("General", "Range", 30f, "Max shooting distance in meters.");
            FireInterval = Config.Bind("General", "FireInterval", 0.21f, "Seconds between shots (vanilla is ~0.21).");
            EnemyDamage = Config.Bind("Damage", "EnemyDamage", 2, "Hit force applied to a monster per shot.");
            PlayerDamage = Config.Bind("Damage", "PlayerDamage", 50, "Damage applied to a player per shot.");

            new Harmony("com.yourname.turretsshootall").PatchAll();
            Logger.LogInfo("Turrets Shoot All loaded.");
        }
    }

    // Attach our extra targeting logic to every turret when it spawns.
    [HarmonyPatch(typeof(Turret), "Start")]
    internal static class TurretStartPatch
    {
        private static void Postfix(Turret __instance)
        {
            if (__instance.GetComponent<TurretShootAll>() == null)
            {
                var comp = __instance.gameObject.AddComponent<TurretShootAll>();
                comp.turret = __instance;
            }
        }
    }

    public class TurretShootAll : MonoBehaviour
    {
        public Turret turret;

        private float shotTimer;
        private float aimTimer;
        private Vector3 aimPosition;

        private static bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            return !Physics.Linecast(from, to, StartOfRound.Instance.collidersAndRoomMask, QueryTriggerInteraction.Ignore);
        }

        private void Update()
        {
            if (turret == null || !turret.turretActive || StartOfRound.Instance == null)
                return;

            shotTimer -= Time.deltaTime;
            aimTimer -= Time.deltaTime;
            if (shotTimer > 0f)
                return;

            Vector3 origin = turret.aimPoint.position;
            float bestDistance = Plugin.Range.Value;
            EnemyAI bestEnemy = null;
            bool targetLocalPlayer = false;
            Vector3 bestPosition = Vector3.zero;

            // Monsters: only the host/server applies damage to them.
            if (Plugin.TargetEnemies.Value
                && NetworkManager.Singleton != null
                && NetworkManager.Singleton.IsServer
                && RoundManager.Instance != null)
            {
                foreach (EnemyAI enemy in RoundManager.Instance.SpawnedEnemies)
                {
                    if (enemy == null || enemy.isEnemyDead)
                        continue;

                    Vector3 pos = enemy.transform.position + Vector3.up * 1f;
                    float dist = Vector3.Distance(origin, pos);
                    if (dist < bestDistance && HasLineOfSight(origin, pos))
                    {
                        bestDistance = dist;
                        bestEnemy = enemy;
                        bestPosition = pos;
                        targetLocalPlayer = false;
                    }
                }
            }

            // Players: each client checks and damages its own local player.
            if (Plugin.TargetPlayers.Value)
            {
                PlayerControllerB local = GameNetworkManager.Instance != null
                    ? GameNetworkManager.Instance.localPlayerController
                    : null;

                if (local != null && local.isPlayerControlled && !local.isPlayerDead)
                {
                    Vector3 pos = local.transform.position + Vector3.up * 1.2f;
                    float dist = Vector3.Distance(origin, pos);
                    if (dist < bestDistance && HasLineOfSight(origin, pos))
                    {
                        bestDistance = dist;
                        bestEnemy = null;
                        bestPosition = pos;
                        targetLocalPlayer = true;
                    }
                }
            }

            if (bestEnemy == null && !targetLocalPlayer)
                return;

            // Fire.
            shotTimer = Plugin.FireInterval.Value;
            aimTimer = 0.3f;
            aimPosition = bestPosition;

            if (turret.bulletParticles != null)
                turret.bulletParticles.Play();

            if (targetLocalPlayer)
            {
                GameNetworkManager.Instance.localPlayerController.DamagePlayer(
                    Plugin.PlayerDamage.Value, true, true, CauseOfDeath.Gunshots, 0, false, default(Vector3));
            }
            else
            {
                bestEnemy.HitEnemy(Plugin.EnemyDamage.Value, null, true, -1);
            }
        }

        // Runs after the vanilla Update so our aim overrides the sweeping rotation while shooting.
        private void LateUpdate()
        {
            if (turret == null || turret.turretRod == null || aimTimer <= 0f)
                return;

            Vector3 dir = aimPosition - turret.turretRod.position;
            if (dir.sqrMagnitude < 0.01f)
                return;

            Quaternion look = Quaternion.LookRotation(dir);
            turret.turretRod.rotation = Quaternion.Slerp(turret.turretRod.rotation, look, Time.deltaTime * 15f);
        }
    }
}
