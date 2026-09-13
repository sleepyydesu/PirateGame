using UnityEngine;

public class EquipmentSystem : MonoBehaviour
{
    [SerializeField] GameObject weaponHolder;
    [SerializeField] GameObject weaponPrefab;
    [SerializeField] GameObject weaponSheath;

    GameObject weaponInstance;
    //TrailRenderer swordTrail; // or ParticleSystem, depending what you use

    void Awake()
    {
        weaponInstance = Instantiate(weaponPrefab, weaponSheath.transform, false);
        //swordTrail = weaponInstance.GetComponentInChildren<TrailRenderer>();
        //swordTrail.emitting = false; // off by default

        weaponInstance.SetActive(true);
    }

    public void DrawWeapon()
    {
        weaponInstance.SetActive(true);
    }

    public void SheathWeapon()
    {
        weaponInstance.SetActive(false);
    }

    //    // called via Animation Event at swing start
    //    public void StartTrail()
    //    {
    //        swordTrail.emitting = true;
    //    }

    //    // called via Animation Event at swing end
    //    public void StopTrail()
    //    {
    //        swordTrail.emitting = false;
    //    }
}