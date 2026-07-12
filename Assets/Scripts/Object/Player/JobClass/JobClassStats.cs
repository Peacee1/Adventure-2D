using UnityEngine;

/// <summary>
/// ScriptableObject lưu trữ chỉ số ban đầu (base stats) cho một chức nghiệp.
/// Tạo asset mới: Right-click > Create > Adventure2D > JobClass Stats
/// </summary>
[CreateAssetMenu(fileName = "JobClassStats", menuName = "Adventure2D/JobClass Stats")]
public class JobClassStats : ScriptableObject
{
    [Header("Thông Tin Chức Nghiệp")]
    public JobClass jobClass;
    public string displayName;   // Tên hiển thị tiếng Việt
    [TextArea(2, 4)]
    public string description;   // Mô tả ngắn về chức nghiệp

    [Header("Chỉ Số Cơ Bản")]
    public int maxHp;
    public int maxMp;

    [Header("Chỉ Số Tấn Công")]
    public int atkPhysical;      // ATK Vật Lý
    public int atkMagic;         // ATK Phép

    [Header("Chỉ Số Phòng Thủ")]
    public int defPhysical;      // DEF Vật Lý
    public int defMagic;         // DEF Phép
}
