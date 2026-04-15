using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

public class MinigamePlayerNetwork : NetworkBehaviour
{
    [SyncVar] public int slotIndex;
    [SyncVar] public bool isAI;

    [SyncVar(hook = nameof(OnSelectedCharacterChanged))]
    public int selectedCharacter = -1;

    protected PlayerInput playerInput;
    protected InputBase playerController;
    protected CharacterVisualController visualController;
    protected PropertiesBase properties;
    public CPUBase cpuController;

    protected virtual void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
        playerController = GetComponent<InputBase>();
        cpuController = GetComponent<CPUBase>();
        properties = GetComponentInChildren<PropertiesBase>();
        visualController = GetComponent<CharacterVisualController>();
        DisableAll();
    }

    protected void DisableAll()
    {
        if (playerInput) playerInput.enabled = false;
        if (playerController) playerController.enabled = false;
        if (cpuController) cpuController.enabled = false;
    }

    protected void ConfigureNetworkTransform()
    {
        var nt = GetComponent<NetworkTransformBase>();
        if (!nt) return;

        nt.syncDirection = isAI ?
            SyncDirection.ServerToClient :
            SyncDirection.ClientToServer;
    }

    [Server]
    public virtual void ServerInitialize(PlayerState ps)
    {
        slotIndex = ps.slotIndex;
        isAI = ps.isAI;
        properties.playerNumber = slotIndex;
        properties.isCPU = ps.isAI;
        selectedCharacter = ps.selectedCharacter;
        if (isAI && cpuController) cpuController.enabled = true;
        ConfigureNetworkTransform();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        ConfigureNetworkTransform();
        RefreshCharacterVisual();
    }

    public override void OnStartAuthority()
    {
        if (isAI) return;
        if (playerController) playerController.enabled = true;
        if (playerInput) playerInput.enabled = true;
    }

    public override void OnStopAuthority()
    {
        if (playerInput) playerInput.enabled = false;
        if (playerController) playerController.enabled = false;
    }

    private void OnSelectedCharacterChanged(int oldValue, int newValue)
    {
        RefreshCharacterVisual();
    }
    protected void RefreshCharacterVisual()
    {
        if (visualController == null) return;
        if (selectedCharacter < 0) return;
        if (CharacterDatabase.Instance == null) return;
        if (selectedCharacter >= CharacterDatabase.Instance.characters.Length) return;
        if (RoundManager.Instance == null) return;
        if (RoundManager.Instance.currentMinigame == null) return;

        var charDef = CharacterDatabase.Instance.characters[selectedCharacter];
        if (charDef == null) return;

        GameObject visualPrefab = RoundManager.Instance.currentMinigame.GetVisualPrefabForCharacter(charDef);
        if (visualPrefab == null) return;

        visualController.SetVisual(visualPrefab, charDef.avatar);
    }

    //Helpers
    [Server]
    public void ServerTeleportTo(Vector3 pos, Quaternion rot)
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = pos;
            rb.rotation = rot;
        }
        else
        {
            transform.SetPositionAndRotation(pos, rot);
        }

        if (!isAI)
        {
            NetworkConnectionToClient owner = connectionToClient;
            if (owner != null)
                TargetTeleportToPos(owner, pos, rot);
        }
    }

    [TargetRpc]
    public void TargetTeleportToPos(NetworkConnection target, Vector3 pos, Quaternion rot)
    {
        transform.SetPositionAndRotation(pos, rot);

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = pos;
            rb.rotation = rot;
        }
    }
}