using System.Collections.Generic;
using UnityEngine;
using PirateGame.Combat;

public class DamageDealer : MonoBehaviour
{
    bool canDealDamage;
    List<GameObject> hasDealtDamage;

    [SerializeField] float weaponLength;
    [SerializeField] float weaponDamage;
    [Tooltip("Layers the weapon can hit. Defaults to layer 9, matching the previous hardcoded mask.")]
    [SerializeField] LayerMask hitLayers = 1 << 9;

    void Start()
    {
        canDealDamage = false;
        hasDealtDamage = new List<GameObject>();
    }

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
                        DamageInfo info = new DamageInfo(weaponDamage, gameObject, hit.point);
                        damageable.TakeDamage(info);
                    }
                }
            }
        }
    }

    public void StartDealDamage()
    {
        canDealDamage = true;
        hasDealtDamage.Clear();
    }

    public void EndDealDamage()
    {
        canDealDamage = false;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, transform.position - transform.up * weaponLength);
    }
}