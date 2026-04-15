using Mirror;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SurgeSphereGameManager : MinigameBase
{
    public static SurgeSphereGameManager Instance;

    [Header("Spawn Circle")]
    [SerializeField] private GameObject spawnCenterPoint;
    [SerializeField] private float radius = 6f;
    [SerializeField] private float yOffset = 0f;    // raise/lower spawn height
    [SerializeField] private float angleOffsetDeg = 0f; // rotate the whole ring
    public override int PlayerCount => 8;
    public int startingHealth = 5;

    [SerializeField] private GameObject lightningBallPrefab;
    private const float LIGHTNING_BALL_SPAWN_Y_POS = 1.25f;
    private readonly HashSet<int> pendingFatalSlots = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    public override GameObject GetVisualPrefabForCharacter(CharacterData character)
    {
        return character.surgeSpherePrefab;
    }


    [Server]
    public override void Server_SetupMinigame()
    {
        ResetDeathOrder();
        pendingFatalSlots.Clear();
        SpawnAllPlayers();
    }

    [Server]
    private void SpawnAllPlayers()
    {
        var chars = CharacterDatabase.Instance.characters;

        for (int i = 0; i < PlayerCount; i++)
        {
            var ps = PlayerStateManager.Slots[i];
            if (ps == null || ps.selectedCharacter < 0) continue;

            var prefab = chars[ps.selectedCharacter].surgeSpherePrefab;

            Vector3 pos = GetCircleSpawnPos(i, PlayerCount);
            Quaternion rot = GetRotationTowardsCenter(pos, spawnCenterPoint.transform.position);

            GameObject go = Instantiate(prefab, pos, rot);
            go.name = $"Player {i}";

            PropertiesBase properties = go.GetComponent<PropertiesBase>();
            properties.playerMaxHealth = startingHealth;
            properties.playerHealth = startingHealth;

            spawnedPlayers[i] = go;

            var sb = go.GetComponent<SurgeSphereNetwork>();
            sb.slotIndex = i;
            sb.isAI = ps.isAI;
            sb.ServerInitialize(ps);

            NetworkConnectionToClient owner = null;
            if (!ps.isAI)
                owner = FindPlayerConnection(i);

            if (owner != null)
                NetworkServer.Spawn(go, owner);
            else
                NetworkServer.Spawn(go);
        }
    }

    [Server]
    public override void Server_StartGameplay()
    {
        RoundManager.Instance.isRoundActive = true;
        PauseAllPlayers(false);
        SpawnLightningBall(spawnCenterPoint.transform.position);
    }

    [Server]
    public override void Server_RestartRound()
    {
        deathOrder.Clear();
        pendingFatalSlots.Clear();
        TeleportAllPlayersToSpawns();

        // Reset HP and other stats and such
        foreach (var p in spawnedPlayers)
        {
            if (p == null) continue;

            var properties = p.GetComponent<PropertiesBase>();
            var actor = p.GetComponent<SurgeSphereActor>();
            properties.playerHealth = startingHealth;
            properties.isAlive = true;
            properties.ClearInvulnerability();
            properties.ClearStun();
            actor.RpcSetActivePlayer(true);
        }
    }

    [Server]
    public override void Server_OnPlayerTookFatalDamage(int slot)
    {
        if (slot < 0 || slot >= spawnedPlayers.Count) return;
        var player = spawnedPlayers[slot];
        if (player == null) return;

        var properties = player.GetComponent<PropertiesBase>();
        var actor = player.GetComponent<SurgeSphereActor>();
        properties.isPaused = true;
        properties.HasPermanentInvul = true;

        // Guard against duplicate fatal handling while death animation is already running
        if (!properties.isAlive || pendingFatalSlots.Contains(slot))
            return;

        pendingFatalSlots.Add(slot);

        // Players that already took fatal damage are still visually alive during their death animation.
        // Exclude them so the final-duel cleanup reliably happens when 2nd place takes fatal damage.
        int aliveBeforeDeath = Mathf.Max(0, CountAlivePlayers() - (pendingFatalSlots.Count - 1));
        //STOP GAMEPLAY EARLY
        if (aliveBeforeDeath <= 2)
        {
            RoundManager.Instance.isRoundActive = false;
            ClearObjectsNewRound();
            PauseAllPlayers(true);
        }

        // ---------------- DELAYED FINALIZATION ----------------
        StartCoroutine(DeathFinalizeRoutine(slot));
    }

    [Server]
    private IEnumerator DeathFinalizeRoutine(int slot)
    {
        yield return new WaitForSeconds(PropertiesBase.DEATH_ANIM_TIME);
        yield return new WaitForSeconds(0.05f);
        Server_OnPlayerFullyDied(slot);
    }

    [Server]
    public override void Server_OnPlayerFullyDied(int slot)
    {
        pendingFatalSlots.Remove(slot);

        GameObject playerObject = GetPlayerObject(slot);
        if (playerObject == null)
            return;

        var actor = playerObject.GetComponent<SurgeSphereActor>();
        var properties = playerObject.GetComponent<PropertiesBase>();

        // Visual
        actor.RpcPlayDeathFX();

        // Actual death
        properties.isAlive = false;

        actor.RpcSetActivePlayer(false);

        // Track death order for ranking
        RegisterDeathGeneric(slot);

        int alive = CountAlivePlayers();
        int winnerSlot = GetLastAliveSlot();

        // Only finalize when round is actually over
        if (alive <= 1)
        {
            ClearObjectsNewRound();

            GameObject winnerObj = GetPlayerObject(winnerSlot);
            if (winnerObj != null)
            {
                var winnerProps = winnerObj.GetComponent<PropertiesBase>();
                winnerProps.HasPermanentInvul = true;
            }

            // Build final best->worst ranking and let RoundManager apply points/ranks
            List<int> rankingSlots = BuildSurvivalRankingSlots(winnerSlot);

            // If this minigame is multi-heat, pass true; if single-heat, pass false
            bool addIntoMinigameTotal = true; // set based on your design
            RoundManager.Instance.Server_ApplyRoundRanking(rankingSlots, addIntoMinigameTotal, winnerBonus: 1);

            RoundManager.Instance.Server_EndRound(winnerSlot);
        }
    }

    [Server]
    public void SpawnLightningBall(Vector3 pos)
    {
        if (!RoundManager.Instance.isRoundActive) return;

        pos.y = LIGHTNING_BALL_SPAWN_Y_POS;
        GameObject spawnedBall = Instantiate(lightningBallPrefab, pos, spawnCenterPoint.transform.rotation);
        NetworkServer.Spawn(spawnedBall);
        objectsToRemovePerRound.Add(spawnedBall);
    }

    [Server]
    public override void TeleportAllPlayersToSpawns()
    {
        for (int i = 0; i < spawnedPlayers.Count; i++)
        {
            var go = spawnedPlayers[i];
            if (go == null) continue;

            Vector3 pos = GetCircleSpawnPos(i, PlayerCount, 0, spawnCenterPoint, radius, yOffset);
            Quaternion rot = GetRotationTowardsCenter(pos, spawnCenterPoint.transform.position);

            var net = go.GetComponent<SurgeSphereNetwork>();

            // Human player (client authority): tell the owner to move itself
            if (net != null && net.connectionToClient != null)
            {
                net.TargetTeleportToSpawn(net.connectionToClient, pos, rot);
            }
            // AI / server-owned
            else
            {
                go.transform.SetPositionAndRotation(pos, rot);

            }
        }
    }
}
