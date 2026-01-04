using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameplayManager : MonoBehaviour
{
    public enum GameStates
    {
        Play, Pause
    }
    public static GameplayManager instance;

    #region Private Variables

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
        
        SetGameState(GameStates.Pause);
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void SetGameState(GameStates gameState)
    {
        this.gameState = gameState;
    }
}
