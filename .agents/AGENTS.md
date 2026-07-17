# Project Rules — Adventure-2D

## Mandatory Programming Principles: SOLID

All code written or modified in this project **must** strictly follow the 5 SOLID principles:

### S — Single Responsibility Principle
- Each class, script, or module must have **only one reason to change**.
- Clear separation: game logic, UI, data, and input handling must reside in separate classes.
- Example: `Player.cs` must not simultaneously contain movement logic, combat, inventory, and UI.

### O — Open/Closed Principle
- Classes must be **open for extension** (add new features) but **closed for modification** (don't modify already-working code).
- Prefer **abstract classes**, **interfaces**, and **inheritance** over directly modifying base classes.
- When adding a new JobClass or Enemy type, create a new subclass only — do not modify base logic.

### L — Liskov Substitution Principle
- Every subclass must be able to **substitute its parent class** without breaking program logic.
- Avoid overriding methods in ways that violate the expected behavior of the parent class.
- Subclasses may only **narrow or preserve** the pre/post-conditions of parent methods.

### I — Interface Segregation Principle
- Do not force a class to implement interfaces it does not need.
- Split large interfaces into smaller, focused ones by responsibility.
- Example: `IDamageable`, `IMovable`, `IAttackable` instead of one giant `ICharacter`.

### D — Dependency Inversion Principle
- High-level classes **must not depend directly** on low-level classes; both must depend on **abstractions** (interfaces/abstract classes).
- Inject dependencies via constructor or field rather than instantiating directly inside a class.
- Use ScriptableObject or interfaces as intermediary layers where appropriate in Unity.

---

## Additional Rules for Unity C#

- Use **ScriptableObject** for static data (stats, item config, job class info) to decouple data from logic.
- Avoid overusing **Singleton**; only use it when a true global instance is required.
- Every `MonoBehaviour` should contain only Unity lifecycle logic (`Awake`, `Start`, `Update`); push business logic into plain C# classes.
- Name classes, methods, and variables clearly to reflect their responsibilities.

---

## Language Rule

**All code in this project must use English only.** This applies to:
- All code comments (both `//` inline and `/** */` doc comments)
- All log messages (`Debug.Log`, `log.Printf`, etc.)
- All string literals used in UI or status feedback
- All variable names, method names, and class names
- All documentation strings (Go doc comments, C# XML doc `<summary>`)

**No Vietnamese text is allowed anywhere in source code files.** This rule applies to both the Unity C# client and the Go server.
