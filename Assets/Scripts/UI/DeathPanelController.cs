using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using PirateGame.Combat;

public class DeathPanelController : MonoBehaviour
{
    [SerializeField] Health playerHealth;
    [SerializeField] GameObject deathPanel;
    [SerializeField] float freezeDelay = 1.5f; // let ragdoll settle first

    void OnEnable()
    {
        if (playerHealth != null) playerHealth.OnDeath += HandleDeath;
    }

    void OnDisable()
    {
        if (playerHealth != null) playerHealth.OnDeath -= HandleDeath;
    }

    void HandleDeath()
    {
        StartCoroutine(ShowDeathPanelAfterDelay());
    }

    IEnumerator ShowDeathPanelAfterDelay()
    {
        yield return new WaitForSeconds(freezeDelay); // real-time wait, unaffected by timeScale

        deathPanel.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 0f;
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void QuitGame()
    {
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}