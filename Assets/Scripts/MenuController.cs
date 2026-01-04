using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MenuController : MonoBehaviour
{
    public static MenuController instance;
    
    #region  Exposed Variables
    [SerializeField] private Image uiPanel = default;
    [SerializeField] private Image gameEndPanel = default;
    [SerializeField] private Image blackOverlay = default;
    [SerializeField] private TMP_Text ePressText = default;

    #endregion
    
    #region  Private Variables

    

    #endregion

    private void Awake()
    {
        if (!instance)
            instance = this;
    }

    // Start is called before the first frame update
    void Start()
    {
        ShowEPressText(false);
        gameEndPanel.gameObject.SetActive(false);
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
    
    public void ShowEPressText(bool show)
    {
        ePressText.gameObject.SetActive(show);
    }
    
    public void ShowGameEndPanel()
    {
        gameEndPanel.gameObject.SetActive(true);
        ShowEPressText(false);
    }
    
    public void PlayAgainButtonCallback()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
    
}
