using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TreeObject : BaseObject
{
    protected virtual void Awake()
    {
        maxHp = 3;
        hp = maxHp;
        maxMp = 0;
        mp = maxMp;
    }
    public override void Die()
    {
        gameObject.SetActive(false);
        Debug.Log("Tree die");
    }
}
