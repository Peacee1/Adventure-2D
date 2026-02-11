using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base class cho tất cả các game object trong game.
/// Các class con (Enemy, Player, Item, etc.) sẽ kế thừa từ class này
/// để tự động có các thuộc tính HP, MP, Name và ID.
/// </summary>
public class BaseObject : MonoBehaviour
{
    [SerializeField] protected int maxHp;        // Maximum Health Points
    [SerializeField] protected int hp;           // Current Health Points
    [SerializeField] protected int maxMp;        // Maximum Mana Points
    [SerializeField] protected int mp;           // Current Mana Points
    [SerializeField] protected string objectName; // Tên của object
    [SerializeField] protected int objectId;      // ID của object

    // Properties với modifier virtual để các class con có thể override nếu cần
    public virtual int MaxHP
    {
        get => maxHp;
        set => maxHp = value;
    }
    
    public virtual int HP 
    { 
        get => hp; 
        set => hp = Mathf.Clamp(value, 0, maxHp); // Giới hạn HP từ 0 đến maxHp
    }
    
    public virtual int MaxMP
    {
        get => maxMp;
        set => maxMp = value;
    }
    
    public virtual int MP 
    { 
        get => mp; 
        set => mp = Mathf.Clamp(value, 0, maxMp); // Giới hạn MP từ 0 đến maxMp
    }
    
    public virtual string ObjectName 
    { 
        get => objectName; 
        set => objectName = value; 
    }
    
    public virtual int ObjectId 
    { 
        get => objectId; 
        set => objectId = value; 
    }
    public virtual void TakeDamage(int damage)
    {
        hp -= damage;
        if (hp <= 0)
        {
            Die();
        }
    }
    public virtual void Die()
    {
        gameObject.SetActive(false);
    }
    public virtual void Revive()
    {
        hp = maxHp; // Hồi full HP khi revive
        mp = maxMp; // Hồi full MP khi revive
        gameObject.SetActive(true);
    }
    
    public virtual void Heal(int amount)
    {
        HP += amount; // Sử dụng property HP để tự động clamp
    }
    
    public virtual void RestoreMP(int amount)
    {
        MP += amount; // Sử dụng property MP để tự động clamp
    }
    public virtual void UseMP(int amount)
    {
        MP -= amount; // Sử dụng property MP để tự động clamp
    }
    public virtual void OnSelected()
    {  
    }
}
