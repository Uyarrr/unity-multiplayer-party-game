using Mirror;
using UnityEngine;
public class PropertiesBase : NetworkBehaviour
{
    [SyncVar] public int playerNumber;
    [SyncVar] public bool isCPU;
    [SyncVar] public bool isPaused = true;
    [SyncVar] public bool isInvulnerable = false;
    [SyncVar] public bool isAlive = true;
    [SyncVar] public bool isBusy = false;
    [SyncVar] public int playerMaxHealth;
    [SyncVar] public bool HasPermanentStun;
    [SyncVar] public bool HasPermanentInvul;

    public GameObject visualRoot;

    [SerializeField] public GameObject deathFXPrefab;

    // Stun
    [SyncVar] public double stunEndTime;
    public bool IsStunned => NetworkTime.time < stunEndTime || HasPermanentStun;

    // Invulnerability
    [SyncVar] public double invulEndTime;
    public bool IsInvulnerable => NetworkTime.time < invulEndTime || HasPermanentInvul;

    [SyncVar(hook = nameof(OnHealthChanged))]
    public int playerHealth;

    public AIDifficulty difficulty = AIDifficulty.Normal;
    public const float DEATH_ANIM_TIME = 3.292f;

    [HideInInspector] public Animator animator;
    [HideInInspector] public Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        animator = GetComponent<Animator>();
    }

    public override void OnStartClient()
    {
        //Remove physics , so no double simulation.

        base.OnStartClient();
        if (!isOwned && !isServer)
        {
            rb.isKinematic = true;
            rb.detectCollisions = false;
        }
        else
        {
            rb.isKinematic = false;
            rb.detectCollisions = true;
        }
    }

    public virtual void OnHealthChanged(int oldValue, int newValue) { }
    [Server] public void ClearStun()
    {
        stunEndTime = 0;
        HasPermanentStun = false;
    }
    [Server] public void ClearInvulnerability()
    {
        invulEndTime = 0;
        HasPermanentInvul = false;
    }
}