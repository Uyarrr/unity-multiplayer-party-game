using Mirror;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RoundManager : NetworkBehaviour
{
    public static RoundManager Instance;

    public AudioSource musicSource;
    public AudioSource soundSource;

    [SyncVar] public bool isRoundActive;
    [SyncVar] public float RoundTimeElapsed;
    [SyncVar] public float NumberOfRoundsPlayed;

    [Header("Countdown")]
    public GameObject countdownRoot;
    public Image countdownImage;
    public Sprite[] countdownSprites;
    public AudioClip[] countdownSFX;

    [Header("After Round UI")]
    public GameObject afterRoundUI;
    public Image[] playerIcons;
    public TextMeshProUGUI[] winsText;
    public TextMeshProUGUI winnerNameText;
    public float roundOverDelay = 3f;

    [HideInInspector] public MinigameBase currentMinigame;

    // internal winner slot for UI
    private int lastWinnerSlot = -1;

    private void Awake() => Instance = this;

    private void Update()
    {
        if (isRoundActive)
            RoundTimeElapsed += Time.deltaTime;

        // Optional: auto-end if countdown reaches 0
        if (currentMinigame != null &&
            currentMinigame.RoundDurationSeconds > 0 &&
            RoundTimeElapsed >= currentMinigame.RoundDurationSeconds)
        {
            // currentMinigame.Server_OnTimeExpired();
        }
    }

    // ----------------- ROUND START -----------------

    [Server]
    public void BeginRoundSequence()
    {
        // round not active during countdown
        isRoundActive = false;
        RoundTimeElapsed = 0f;

        RpcPlayCountdown();
    }

    [ClientRpc]
    private void RpcPlayCountdown()
    {
        afterRoundUI.SetActive(false);
        StartCoroutine(CountdownRoutine());
    }

    private IEnumerator CountdownRoutine()
    {
        countdownRoot.SetActive(true);

        for (int i = 0; i < countdownSprites.Length; i++)
        {
            countdownImage.sprite = countdownSprites[i];
            if (soundSource && i < countdownSFX.Length && countdownSFX[i] != null)
                soundSource.PlayOneShot(countdownSFX[i]);

            yield return new WaitForSeconds(1f);
        }

        countdownRoot.SetActive(false);

        if (isServer && currentMinigame != null)
        {
            currentMinigame.Server_StartGameplay();
            isRoundActive = true;
        }
    }

    // ----------------- ROUND END (CALLED BY MINIGAME) -----------------

    /// <summary>
    /// Call this from the minigame when you know who won the round.
    /// </summary>
    [Server]
    public void Server_EndRound(int winnerSlot)
    {
        isRoundActive = false;
        RoundTimeElapsed = 0f;
        lastWinnerSlot = winnerSlot;
        NumberOfRoundsPlayed++;

        StartCoroutine(RoundEndRoutine());
    }

    [Server]
    private IEnumerator RoundEndRoutine()
    {
        string winnerName = "";
        int[] wins = new int[playerIcons.Length];

        for (int i = 0; i < playerIcons.Length; i++)
        {
            var ps = PlayerStateManager.Slots[i];
            if (ps == null || ps.selectedCharacter < 0) continue;

            wins[i] = ps.CurrentMinigamePoints;

            if (i == lastWinnerSlot)
                winnerName = ps.playerName;
        }

        RpcShowAfterRoundUI(winnerName, wins);

        yield return new WaitForSeconds(roundOverDelay);

        if (HasCompletedMinigameRounds())
        {
            if (SessionManager.Instance != null &&
                SessionManager.Instance.Server_TryAdvanceToNext(out string nextScene))
            {
                NetworkManager.singleton.ServerChangeScene(nextScene);
            }
            else
            {
                Debug.Log("Tournament playlist finished or unavailable; no next scene to load.");
            }

            yield break;
        }

        // ask minigame to reset itself and start next round
        if (currentMinigame != null)
        {
            currentMinigame.Server_RestartRound();
            BeginRoundSequence();
        }
    }

    [Server]
    private bool HasCompletedMinigameRounds()
    {
        if (SessionManager.Instance == null) return false;
        return NumberOfRoundsPlayed >= SessionManager.Instance.NumberOfRoundsPerMinigame;
    }

    [Server]
    public void Server_ApplyRoundRanking(List<int> rankingSlots, bool addIntoMinigameTotal, int winnerBonus = 1)
    {
        int n = rankingSlots.Count;

        // optional: clear per-round display fields first
        for (int i = 0; i < n; i++)
        {
            var ps0 = PlayerStateManager.Slots[rankingSlots[i]];
            if (ps0 == null) continue;
            ps0.CurrentRoundRank = 0;
            ps0.CurrentRoundPoints = 0;
        }

        for (int i = 0; i < n; i++)
        {
            int rank = i + 1;
            int slot = rankingSlots[i];
            var ps = PlayerStateManager.Slots[slot];
            if (ps == null) continue;

            int points = (n - rank);     // last=0
            if (rank == 1) points += winnerBonus;

            ps.CurrentRoundRank = rank;
            ps.CurrentRoundPoints = points;

            if (addIntoMinigameTotal)
                ps.CurrentMinigamePoints += points;
            else
                ps.CurrentMinigamePoints = points;
        }
    }

    [ClientRpc]
    private void RpcShowAfterRoundUI(string winnerName, int[] wins)
    {
        afterRoundUI.SetActive(true);

        for (int i = 0; i < playerIcons.Length; i++)
        {
            var ps = PlayerStateManager.Slots[i];
            if (ps == null || ps.selectedCharacter < 0)
            {
                winsText[i].text = "";
                playerIcons[i].enabled = false;
                continue;
            }

            var c = CharacterDatabase.Instance.characters[ps.selectedCharacter];

            playerIcons[i].enabled = true;
            playerIcons[i].sprite = c.icon;
            winsText[i].text = wins[i].ToString();
        }

        if (!string.IsNullOrEmpty(winnerName))
            winnerNameText.text = $"{winnerName} Wins!";
        else
            winnerNameText.text = "Round Over!";
    }
    public static int GetPointsForPlacement(int placement, int playerCount)
    {
        if (placement == playerCount) return 0;   // last place

        int points = playerCount - placement;

        if (placement == 1) points += 2;          // big winner bonus
        if (placement == 2) points += 1;          // second place bonus

        return Mathf.Max(0, points);
    }

    public bool TryGetMinigame<T>(out T gm) where T : MinigameBase
    {
        gm = currentMinigame as T;
        return gm != null;
    }

    public T GetMinigameOrNull<T>() where T : MinigameBase
    {
        return currentMinigame as T;
    }
}
