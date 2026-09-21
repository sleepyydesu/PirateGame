using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.InputSystem;

public class ThirdPersonCameraController : MonoBehaviour
{
    [SerializeField] private PlayerInput playerInput;
    [SerializeField] private float zoomSpeed = 2f;
    [SerializeField] private float zoomLerpSpeed = 10f;
    [SerializeField] private float minDistance = 3f;
    [SerializeField] private float maxDistance = 15f;

    private CinemachineCamera cam;
    private CinemachineOrbitalFollow orbital;
    private InputAction zoomAction;

    private float targetZoom;
    private float currentZoom;

    void Awake()
    {
        cam = GetComponent<CinemachineCamera>();
        orbital = cam != null ? cam.GetComponent<CinemachineOrbitalFollow>() : null;
    }

    void Start()
    {
        if (playerInput == null)
        {
            Debug.LogWarning("ThirdPersonCameraController: no PlayerInput assigned.");
            return;
        }

        playerInput.actions.Enable();

        zoomAction = playerInput.actions.FindAction("MouseZoom");
        if (zoomAction == null)
        {
            Debug.LogWarning("ThirdPersonCameraController: 'MouseZoom' action not found.");
            return;
        }

        zoomAction.performed += HandleMouseScroll;

        Cursor.lockState = CursorLockMode.Locked;

        if (orbital != null)
        {
            targetZoom = currentZoom = orbital.Radius;
        }
    }

    private void HandleMouseScroll(InputAction.CallbackContext context)
    {
        Vector2 scrollDelta = context.ReadValue<Vector2>();

        if (scrollDelta.y != 0 && orbital != null)
        {
            targetZoom = Mathf.Clamp(orbital.Radius - scrollDelta.y * zoomSpeed, minDistance, maxDistance);
        }
    }

    void Update()
    {
        if (orbital == null) return;

        currentZoom = Mathf.Lerp(currentZoom, targetZoom, Time.deltaTime * zoomLerpSpeed);
        orbital.Radius = currentZoom;
    }

    void OnDestroy()
    {
        if (zoomAction != null)
        {
            zoomAction.performed -= HandleMouseScroll;
        }
    }
}