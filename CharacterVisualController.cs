using System.Collections;
using UnityEngine;

public class CharacterVisualController : MonoBehaviour
{
    [Header("Setup")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Animator targetAnimator;
    [SerializeField] private Avatar targetAvatar;

    private GameObject currentVisual;
    private Coroutine refreshRoutine;

    public void SetVisual(GameObject visualPrefab, Avatar avatar)
    {
        ClearVisual();

        if (visualRoot == null)
        {
            Debug.LogWarning($"{name}: CharacterVisualController visualRoot is not assigned.");
            return;
        }

        if (visualPrefab == null)
        {
            Debug.LogWarning($"{name}: CharacterVisualController received null visualPrefab.");
            return;
        }

        currentVisual = Instantiate(visualPrefab, visualRoot);
        currentVisual.transform.localPosition = Vector3.zero;
        currentVisual.transform.localRotation = Quaternion.identity;
        currentVisual.transform.localScale = Vector3.one;
        targetAvatar = avatar;

        if (refreshRoutine != null)
            StopCoroutine(refreshRoutine);

        refreshRoutine = StartCoroutine(CoRefreshAnimator());
    }

    public void ClearVisual()
    {
        if (refreshRoutine != null)
        {
            StopCoroutine(refreshRoutine);
            refreshRoutine = null;
        }

        if (currentVisual != null)
        {
            Destroy(currentVisual);
            currentVisual = null;
        }
    }

    private IEnumerator CoRefreshAnimator()
    {
        if (targetAnimator == null)
            yield break;

        // wait one frame so hierarchy/skinned meshes are fully initialized
        yield return null;

        targetAnimator.avatar = targetAvatar;
        targetAnimator.enabled = false;
        yield return null;
        targetAnimator.enabled = true;

        targetAnimator.Rebind();
        targetAnimator.Update(0f);

        refreshRoutine = null;
    }

    public GameObject GetCurrentVisual()
    {
        return currentVisual;
    }
}