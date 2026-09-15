using System.Collections.Generic;
using UnityEngine;

public class DamageDealer : MonoBehaviour
{
    bool canDealDamage;
    List<GameObject> hasDealtDamage;

    [SerializeField] float weaponLength;
    [SerializeField] float weaponDamage;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        canDealDamage = false;
        hasDealtDamage = new List<GameObject>();
    }

    // Update is called once per frame
    void Update()
    {
        if(canDealDamage)
        {
            RaycastHit hit;

            int layerMask = 1 << 9;
            if(Physics.Raycast(transform.position, -transform.up, out hit, weaponLength, layerMask))
            {
                if (!hasDealtDamage.Contains(hit.transform.gameObject))
                {
                    Debug.Log("damage");
                    hasDealtDamage.Add(hit.transform.gameObject);
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
