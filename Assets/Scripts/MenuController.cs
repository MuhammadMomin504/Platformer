using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MenuController : MonoBehaviour
{
    #region  Exposed Variables
    [SerializeField] private Image uiPanel = default;
    [SerializeField] private Image blackOverlay = default;

    #endregion
    
    #region  Private Variables

    

    #endregion
    
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void PlayButton()
    {
        StartCoroutine(PlayButtonCoroutine());
    }
    
    private IEnumerator PlayButtonCoroutine()
    {
        yield return StartCoroutine(FadePanelAlpha(blackOverlay, 1f, 1f));
        uiPanel.gameObject.SetActive(false);
        yield return StartCoroutine(FadePanelAlpha(blackOverlay, 0f, 1f));
        if (GameplayManager.instance)
        {
            GameplayManager.instance.SetGameState(GameplayManager.GameStates.Play);
        }
    }

    // Coroutine to fade UI panel alpha from 0 to 1 or 1 to 0
    public IEnumerator FadePanelAlpha(Image panel, float targetAlpha, float duration)
    {
        panel.gameObject.SetActive(true);

        float startAlpha = panel.color.a;
        float elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float newAlpha = Mathf.Lerp(startAlpha, targetAlpha, elapsedTime / duration);

            Color newColor = panel.color;
            newColor.a = newAlpha;
            panel.color = newColor;

            yield return null;
        }

        // Ensure final alpha is set
        Color finalColor = panel.color;
        finalColor.a = targetAlpha;
        panel.color = finalColor;
        
        panel.gameObject.SetActive(false);
    }
}
