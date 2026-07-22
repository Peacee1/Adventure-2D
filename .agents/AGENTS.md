# Unity 2D MMORPG Coding Rules

## General Principles

- Always follow SOLID principles.
- Keep code simple (KISS).
- Avoid code duplication (DRY).
- Prefer composition over inheritance.
- Never create God Classes.
- Write readable code before clever code.

---

## Naming

Classes:
- PascalCase
- Example:
    PlayerController
    InventoryManager
    NetworkClient

Interfaces:
- Prefix with I
- Example:
    IMovement
    IDamageable

Methods:
- PascalCase
- Use verbs
- Example:
    Move()
    Attack()
    SendPacket()

Private fields:
- _camelCase
- Example:
    _health
    _rigidbody

Public fields:
- Avoid public fields.
- Use properties when possible.

Constants:
- PascalCase or ALL_CAPS
- Example:
    MaxLevel
    MAX_PACKET_SIZE

---

## Folder Structure

Assets/

    Scripts/
        Core/
        Network/
        Player/
        Monster/
        NPC/
        Item/
        Skill/
        UI/
        Manager/
        Config/
        Utils/

Separate gameplay logic from UI.

---

## Architecture

Use dependency injection whenever possible.

Managers should only coordinate systems.

Gameplay logic belongs to components.

UI never contains gameplay logic.

Networking never contains gameplay logic.

---

## Single Responsibility

Each class should have only one responsibility.

Bad:

Player.cs

- movement
- animation
- attack
- inventory
- quest
- crafting
- networking

Good:

PlayerMovement

PlayerCombat

PlayerAnimation

PlayerInventory

PlayerNetwork

---

## Networking

Server is always authoritative.

Client must never decide:

- Damage
- HP
- Position validation
- Loot
- Experience
- Currency

Client only:

- Input
- Prediction (optional)
- Visual effects
- UI

Server validates everything.

---

## Unity Rules

Never use FindObjectOfType during gameplay.

Avoid GameObject.Find.

Cache references.

Avoid Update() unless necessary.

Prefer events.

Avoid allocations every frame.

Use Object Pooling.

Destroy()/Instantiate() only when necessary.

---

## Events

Prefer C# events or EventBus.

Avoid direct dependencies between unrelated systems.

Example:

Player dies

↓

Raise event

↓

UI updates

↓

Quest updates

↓

Achievement updates

---

## Data

Use ScriptableObjects for:

- Items
- Monsters
- Skills
- Maps
- Configurations

Never store runtime state inside ScriptableObjects.

---

## Serialization

Network packets must be serializable.

Avoid sending unnecessary data.

Only synchronize required fields.

---

## Error Handling

Never ignore exceptions.

Use meaningful logs.

Example:

[Network]

[Combat]

[Inventory]

Avoid Debug.Log spam.

---

## Performance

Avoid LINQ in Update.

Avoid foreach on hot paths if profiling shows allocations.

Cache GetComponent.

Use pooling.

Minimize GC allocations.

---

## Pixel Art

Pixels Per Unit must remain consistent.

Camera must preserve pixel-perfect rendering.

Never scale sprites arbitrarily.

---

## Multiplayer

Never trust client data.

Validate:

Movement

Attack

Item usage

Trade

Quest

Chat

Server decides final state.

---

## Code Style

Methods should generally stay under 40 lines.

Classes should generally stay under 300 lines.

Avoid nested if statements.

Prefer early return.

Example:

if (!IsAlive)
    return;

instead of

if (IsAlive)
{
    ...
}

---

## Comments

Write code that explains itself.

Comment WHY.

Not WHAT.

Good:

// Prevent speed hacks by validating movement on server.

Bad:

// Increase x by speed.

---

## Async

Never block Unity main thread.

Use async/await carefully.

Cancel tasks properly.

---

## Git

One feature per branch.

Small commits.

Meaningful commit messages.

Example:

feat: add inventory packet

fix: resolve movement desync

refactor: split player combat

---

## Security

Never expose server secrets.

Never trust packet values.

Validate all packet lengths.

Sanitize chat messages.

Prevent packet spam.

Rate-limit requests.

---

## Testing

Core gameplay logic should be testable without UI.

Networking should be mockable.

Business logic should not depend on MonoBehaviour.

---

## Rule Summary

✔ SOLID
✔ DRY
✔ KISS
✔ Composition over Inheritance
✔ Server Authoritative
✔ Event-driven architecture
✔ Dependency Injection
✔ Object Pooling
✔ ScriptableObject for static data
✔ No gameplay logic inside UI
✔ No networking logic inside gameplay
✔ No GameObject.Find()
✔ Cache references
✔ Early Return
✔ Clean Architecture

MMO Specific Rules

- The server is the single source of truth.
- Client prediction must never overwrite server state.
- Every network message must have a dedicated Packet class.
- Never mix packet serialization with gameplay logic.
- Keep packet handlers lightweight; delegate business logic to services.
- Use finite state machines (FSM) for Player, Monster, and NPC behaviors.
- Separate static game data (configs) from runtime entity state.
- Design systems to support thousands of concurrent entities.
- Optimize for low garbage collection (GC) allocations.
- Every new feature should be extensible without modifying existing systems (Open/Closed Principle).