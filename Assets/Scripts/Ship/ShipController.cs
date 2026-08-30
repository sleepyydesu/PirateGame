using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class ShipController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float maxSpeed = 10f;
    [SerializeField] private float acceleration = 5f;
    [SerializeField] private float deceleration = 3f;

    [Header("Turning")]
    [SerializeField] private float turnSpeed = 45f;
    [SerializeField] private float minSpeedToTurn = 0.5f;

    [Header("Collision")]
    [SerializeField] private float collisionSpeedLoss = 0.5f;  // fraction of speed lost on impact (0-1)
    [SerializeField] private float bounceForce = 3f;           // how hard we push back on impact

    private Rigidbody rb;
    private float inputVertical;
    private float inputHorizontal;
    private float currentSpeed;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        inputVertical = Input.GetAxis("Vertical");
        inputHorizontal = Input.GetAxis("Horizontal");
    }

    void FixedUpdate()
    {
        // --- Speed ---
        float targetSpeed = inputVertical * maxSpeed;
        float rate = Mathf.Abs(targetSpeed) > Mathf.Abs(currentSpeed) ? acceleration : deceleration;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rate * Time.fixedDeltaTime);

        // --- Turning ---
        float speedFactor = Mathf.Clamp01(Mathf.Abs(currentSpeed) / maxSpeed);
        if (Mathf.Abs(currentSpeed) > minSpeedToTurn)
        {
            float turnAmount = inputHorizontal * turnSpeed * speedFactor * Time.fixedDeltaTime;
            Quaternion turnRotation = Quaternion.Euler(0f, turnAmount, 0f);
            rb.MoveRotation(rb.rotation * turnRotation);
        }

        // --- Movement ---
        Vector3 moveDirection = transform.forward * currentSpeed;
        rb.MovePosition(rb.position + moveDirection * Time.fixedDeltaTime);
    }

    void OnCollisionEnter(Collision collision)
    {
        // Kill some speed on impact so we don't just plow through
        currentSpeed *= (1f - collisionSpeedLoss);

        // Push the ship back along the collision normal (away from what we hit)
        if (collision.contactCount > 0)
        {
            Vector3 pushDirection = collision.GetContact(0).normal;
            rb.MovePosition(rb.position + pushDirection * bounceForce * Time.fixedDeltaTime);
        }

        // Optional: log what we hit, useful for debugging/tagging later
        Debug.Log("Ship collided with: " + collision.gameObject.name);
    }
}