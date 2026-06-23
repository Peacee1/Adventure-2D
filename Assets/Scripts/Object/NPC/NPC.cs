using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NPC : BaseObject
{
    protected virtual void Awake()
    {
        outlineColor = Color.white; // Mặc định outline của NPC là màu trắng tinh
        base.Awake();
        MaxHP = 999999;
        HP = 999999;
        MaxMP = 999999;
        MP = 999999;
    }
    public override void TakeDamage(int damage)
    {
    }
    public override void Die()
    {
    }
    
}
