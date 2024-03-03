using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AnimationController : MonoBehaviour
{
    #region Exposed Variables...
    [SerializeField] private Animator _animator = default;
    
    #endregion
    
    #region Private Variables...
    
    #endregion
    
    #region Getters...
    
    #endregion
    
    
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void PlayIdle(bool shouldPlay)
    {
        _animator.SetBool(Constants.Animations.Idle, shouldPlay);
    }

    public void PlayWalk(bool shouldPlay)
    {
        _animator.SetBool(Constants.Animations.Walk, shouldPlay);
    }

    public void PlayDance(bool shouldPlay)
    {
        _animator.SetBool(Constants.Animations.Dance, shouldPlay);
    }

    public void DisableAllBools()
    {
        _animator.SetBool(Constants.Animations.Idle, false);
        _animator.SetBool(Constants.Animations.Walk, false);
        _animator.SetBool(Constants.Animations.Dance, false);
    }
    
}