/// <summary>
/// CharacterData — plain C# data class holding summary info for one character slot.
///
/// SRP: data-only, no logic.
/// </summary>
public class CharacterData
{
    /// <summary>Slot index: 0, 1, or 2.</summary>
    public int   Slot;

    /// <summary>True if the slot has a character, false if empty.</summary>
    public bool  Exists;

    /// <summary>Character name (empty if slot is unused).</summary>
    public string CharName;

    /// <summary>JobClass byte from the server (1 = Archer, 0 = Warrior, ...).</summary>
    public byte  JobClass;

    /// <summary>Current level of the character.</summary>
    public int   Level;

    public CharacterData() { }

    public CharacterData(int slot, bool exists, string charName, byte jobClass, int level)
    {
        Slot     = slot;
        Exists   = exists;
        CharName = charName;
        JobClass = jobClass;
        Level    = level;
    }
}
