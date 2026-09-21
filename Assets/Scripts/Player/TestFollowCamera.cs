using UnityEngine;
using UnityEngine.InputSystem;

namespace PirateGame.Player
{
    /// <summary>
    /// TEMPORARY third-person camera for testing the enemy AI.
    /// Replace with Cinemachine / the real camera rig later.
    /// Hold right mouse button and move the mouse to orbit.
    /// </summary>
    public class TestFollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0f, 2.2f, 0f);
        [SerializeField] private float distance = 8f;
        [SerializeField] private float height = 3f;
        [SerializeField] private float followSmooth = 8f;
        [SerializeField] private float orbitSensitivity = 0.15f;

        private float yaw;

        private void Start()
        {
            if (target == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) target = p.transform;
            }
            yaw = transform.eulerAngles.y;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed)
                yaw += mouse.delta.ReadValue().x * orbitSensitivity;

            Vector3 focus = target.position + offset;
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 desired = focus - rot * Vector3.forward * distance + Vector3.up * height;

            transform.position = Vector3.Lerp(transform.position, desired,
                followSmooth * Time.deltaTime);
            transform.rotation = Quaternion.LookRotation(focus - transform.position);
        }
    }
}
