/// <summary>
/// Enum tất cả chỉ số có thể bị ảnh hưởng bởi Buff/Debuff.
/// </summary>
public enum StatType
{
    MaxHP,          // HP tối đa
    MaxMP,          // MP tối đa
    ATKPhysical,    // ATK Vật Lý
    ATKMagic,       // ATK Phép
    DEFPhysical,    // DEF Vật Lý
    DEFMagic,       // DEF Phép
    HPRegen,        // HP hồi/giây
    MPRegen,        // MP hồi/giây
    Speed,          // Tốc độ di chuyển
    AttackRange,    // Tầm đánh
    AttackSpeed,    // Giây/đòn (thấp = nhanh)
    LifeSteal,      // Hút máu (0–1)
    HealingPower,   // Hệ số nhân hiệu quả hồi máu
}
