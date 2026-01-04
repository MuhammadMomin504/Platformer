using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameplayManager : MonoBehaviour
{
    public static GameplayManager instance;

    #region Exposed Variables

    [SerializeField] private PlayerController playerController;
    
    #endregion


    #region Getters

    public PlayerController PlayerController => playerController;
    public Transform PlayerTransform => playerController.transform;

    #endregion
    


    private void Awake()
    {
        if (!instance)
            instance = this;
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
