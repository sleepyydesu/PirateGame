using System.Collections.Generic;
using UnityEngine;
using PirateGame.Combat;

public class DamageDealer : MonoBehaviour
{
    private bool canDealDamage;

    private readonly List<GameObject> hasDealtDamage =
        new List<GameObject>();

    [SerializeField] float weaponLength;
    [SerializeField] float weaponDamage;
    [Tooltip("Layers the weapon can hit. Defaults to layer 9, matching the previous hardcoded mask.")]
    [SerializeField] LayerMask hitLayers = 1 << 9;

    [Tooltip("Set at runtime by PlayerUpgrades (quest rewards). 1 = base damage.")]
    public float damageMultiplier = 1f;

    void Update()
    {
        if (canDealDamage)
        {
            RaycastHit hit;

            if (Physics.Raycast(transform.position, -transform.up, out hit, weaponLength, hitLayers))
            {
                GameObject target = hit.transform.gameObject;

                if (!hasDealtDamage.Contains(target))
                {
                    hasDealtDamage.Add(target);

                    IDamageable damageable = hit.transform.GetComponentInParent<IDamageable>();
                    if (damageable != null)
                    {
                        DamageInfo info = new DamageInfo(weaponDamage * damageMultiplier, gameObject, hit.point);
                        damageable.TakeDamage(info);
                    }
                }
            }
        }
    }

    public void StartDealDamage()
    {
        // The Weapon Bridge disables this component.
        // Ignore old calls while it is disabled.
        if (!isActiveAndEnabled)
            return;

        hasDealtDamage.Clear();
        canDealDamage = true;
    }

    public void EndDealDamage()
    {
        canDealDamage = false;
    }
    private void OnDisable()
    {
        EndDealDamage();
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, transform.position - transform.up * weaponLength);
    }
}