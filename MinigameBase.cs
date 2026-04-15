using Mirror;
using System.Collections;
using System.Collections.Generic;
using Unity.AppUI.UI;
using UnityEngine;

public abstract class MinigameBase : NetworkBehaviour
{
    // All paddles/players spawned for this minigame (index == slot)
    public readonly List<GameObject> spawnedPlayers = new List<GameObject>();
    public readonly List<GameObject> objectsToRemovePerRound = new List<GameObject>();

    [Header("Round Time")]
    [Tooltip("0 = show elapsed time, >0 = countdown")]
    [SerializeField] protected float roundDurationSeconds = 0f;
    public float RoundDurationSeconds => roundDurationSeconds;
    protected readonly List<int> deathOrder = new();
    protected bool isRoundFinalized;

    /// <summary>How many player slots this minigame actually uses (0..PlayerCount-1).</summary>
    public abstract int PlayerCount { get; }
    public abstract GameObject GetVisualPrefabForCharacter(CharacterData character);

    public override void OnStartServer()
    {
        base.OnStartServer();

        // Register with RoundManager
        RoundManager.Instance.currentMinigame = this;

        // Let scene finish spawning, then set up minigame and start countdown
        StartCoroutine(StartupRoutine());
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (RoundManager.Instance != null)
            RoundManager.Instance.currentMinigame = this;
    }

    private IEnumerator StartupRoutine()
    {
        yield return new WaitForSeconds(1f);   // small delay so everything is ready
        isRoundFinalized = false;
        EnsureSpawnedListSized();
        Server_SetupMinigame();                // spawn players and objects etc depends on the minigame.
        RoundManager.Instance.BeginRoundSequence();
        RefreshInMinigameUI();
    }

    // ----------------- MUST IMPLEMENT IN EACH MINIGAME -----------------

    /// Spawns players and initializes minigame-specific objects (minigame elements, hazards, etc.)
    public abstract void Server_SetupMinigame();

    /// <summary>Start actual gameplay after countdown (unpause players, start spawners etc.).</summary>
    public virtual void Server_StartGameplay() { }

    /// <summary>Called when this slot's HP reached 0 (fatal damage). Start death flow here.</summary>
    public abstract void Server_OnPlayerTookFatalDamage(int slot);

    /// <summary>Called by the player object AFTER its death animation finished.</summary>
    public abstract void Server_OnPlayerFullyDied(int slot);

    /// <summary>Reset state (HP, positions, lines etc.) to prepare for the next round.</summary>
    public abstract void Server_RestartRound();

    public abstract void TeleportAllPlayersToSpawns();

    // ----------------- HELPER -----------------

    public GameObject GetPlayerObject(int slot)
    {
        if (slot < 0 || slot >= spawnedPlayers.Count)
            return null;

        return spawnedPlayers[slot];
    }

    [Server]
    public int CountAlivePlayers()
    {
        int c = 0;
        foreach (var p in spawnedPlayers)
        {
            if (p == null) continue;
            var props = p.GetComponent<PropertiesBase>();
            if (props != null && props.isAlive) c++;
        }
        return c;
    }

    [Server]
    public int GetLastAliveSlot()
    {
        for (int i = 0; i < spawnedPlayers.Count; i++)
        {
            var p = spawnedPlayers[i];
            if (p == null) continue;

            var props = p.GetComponent<PropertiesBase>();
            if (props != null && props.isAlive)
                return i;
        }

        return -1; // nobody alive
    }

    [Server]
    public void PauseAllPlayers(bool _isPaused)
    {
        foreach (var p in spawnedPlayers)
        {
            if (p == null) continue;
            p.GetComponent<PropertiesBase>().isPaused = _isPaused;
        }
    }

    [Server]
    public List<int> GetActiveSlotsThisRound()
    {
        var active = new List<int>();

        // spawnedPlayers must be sized to PlayerCount with nulls for missing slots
        for (int i = 0; i < PlayerCount; i++)
        {
            if (i < spawnedPlayers.Count && spawnedPlayers[i] != null)
                active.Add(i);
        }
        return active;
    }

    [Server]
    public int GetNumberofPlayers()
    {
        var amount = PlayerStateManager.Slots.Length;
        return amount;
    }

    [Server]
    protected void EnsureSpawnedListSized()
    {
        spawnedPlayers.Clear();
        for (int i = 0; i < PlayerCount; i++)
            spawnedPlayers.Add(null);
    }
    protected List<int> BuildSurvivalRankingSlots(int winnerSlot)
    {
        // From Best to Worst
        var ranking = new List<int>();

        if (winnerSlot >= 0)
            ranking.Add(winnerSlot);

        // reverse death order: last-to-die is better
        for (int i = deathOrder.Count - 1; i >= 0; i--)
        {
            int s = deathOrder[i];
            if (s == winnerSlot) continue;
            ranking.Add(s);
        }

        // safety: include all active participants (winner usually never "dies")
        var active = GetActiveSlotsThisRound();
        foreach (int s in active)
            if (!ranking.Contains(s))
                ranking.Add(s);

        return ranking;
    }

    protected virtual void ResetDeathOrder()
    {
        deathOrder.Clear();
    }

    protected void RegisterDeathGeneric(int slot)
    {
        if (!deathOrder.Contains(slot))
            deathOrder.Add(slot);
    }

    protected void ClearObjectsNewRound()
    {
        foreach (GameObject obj in objectsToRemovePerRound)
        {
            if (obj == null) continue;
            {
                NetworkServer.Destroy(obj);
            }
        }

        objectsToRemovePerRound.Clear();
    }

    private void RefreshInMinigameUI()
    {
        for (int i = 0; i < PlayerCount; i++)
        {
            MinigameUIManager.Instance.Refresh(i);
        }
    }

    protected NetworkConnectionToClient FindPlayerConnection(int slot)
    {
        foreach (var po in PlayerObject.AllPlayers)
            if (po.slotIndex == slot)
                return po.connectionToClient;
        return null;
    }
    protected Quaternion GetRotationTowardsCenter(Vector3 fromPos, Vector3 center)
    {
        Vector3 dir = center - fromPos;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return Quaternion.identity;
        return Quaternion.LookRotation(dir);
    }

    protected void FinalizeRound()
    {
        if (isRoundFinalized) return;
        isRoundFinalized = true;
        var winnerSlot = GetLastAliveSlot();
        if (CountAlivePlayers() == 1)
        {
            GameObject winnerObj = GetPlayerObject(winnerSlot);
            if (winnerObj != null)
            {
                var winnerProps = winnerObj.GetComponent<PropertiesBase>();
                winnerProps.isInvulnerable = true;
            }

            // Build final best->worst ranking and let RoundManager apply points/ranks
            List<int> rankingSlots = BuildSurvivalRankingSlots(winnerSlot);

            // If this minigame is multi-heat, pass true; if single-heat, pass false
            bool addIntoMinigameTotal = true; // set based on your design
            RoundManager.Instance.Server_ApplyRoundRanking(rankingSlots, addIntoMinigameTotal, winnerBonus: 1);

            RoundManager.Instance.Server_EndRound(winnerSlot);
        }
    }

    protected Vector3 GetCircleSpawnPos(int index, int count, float angleOffsetDeg, GameObject spawnCenterPoint, float radius, float yOffset)
    {
        float step = 360f / Mathf.Max(1, count);
        float ang = (angleOffsetDeg + step * index) * Mathf.Deg2Rad;

        Vector3 offset = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * radius;
        return spawnCenterPoint.transform.position + offset + Vector3.up * yOffset;
    }
}
