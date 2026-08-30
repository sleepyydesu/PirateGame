using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;       // drag your Ship here in the Inspector

    [Header("Follow Settings")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 5f, -8f); // behind & above the ship
    [SerializeField] private float followSmoothTime = 0.2f;
    [SerializeField] private float rotationSmoothSpeed = 5f;

    private Vector3 velocity = Vector3.zero;

    void LateUpdate()
    {
        if (target == null) return;

        // --- Position: smoothly follow behind the ship ---
        Vector3 desiredPosition = target.position + target.TransformDirection(offset);
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, followSmoothTime);

        // --- Rotation: smoothly look at the ship ---
        Quaternion desiredRotation = Quaternion.LookRotation(target.position - transform.position + Vector3.up * 1.5f);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationSmoothSpeed * Time.deltaTime);
    }
}