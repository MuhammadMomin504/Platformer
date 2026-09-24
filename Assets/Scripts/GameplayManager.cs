using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameplayManager : MonoBehaviour
{
    public enum GameStates
    {
        Play, Pause, End
    }
    public static GameplayManager instance;

    #region Private Variables

    private bool isEndingArea = false;
    private bool shouldGameEnd = false;

    private GameStates gameState;

    #endregion
    

    #region Exposed Variables

    [SerializeField] private PlayerController playerController;
    
    #endregion


    #region Getters

    public PlayerController PlayerController => playerController;
    public Transform PlayerTransform => playerController.transform;
    
    public GameStates GameState => gameState;

    #endregion
    


    private void Awake()
    {
        if (!instance)
            instance = this;
        
        SetGameState(GameStates.Play);
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (isEndingArea)
        {
            if (InputManager.enter && !shouldGameEnd)
            {
                shouldGameEnd = true;
                GameEnd();
            }
        }
    }

    public void SetGameState(GameStates gameState)
    {
        this.gameState = gameState;
    }
    
    public void EndColliderTrigger(bool isTrigger)
    {
        if (isTrigger)
            isEndingArea = true;
        else
            isEndingArea = false;
        
        if (MenuController.instance && !shouldGameEnd)
        {
            MenuController.instance.ShowEPressText(isTrigger);
        }
    }

    public void GameEnd()
    {
        if(MenuController.instance)
        {
            MenuController.instance.ShowGameEndPanel();
        }
        SetGameState(GameStates.End);
        
        if(playerController)
            playerController.PlayDanceAnimation();
    }
}
