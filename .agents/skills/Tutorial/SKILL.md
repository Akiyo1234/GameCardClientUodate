---
name: Tutorial System Framework
description: Guidelines and architecture for implementing or modifying the game's Tutorial Mode. Trigger when user asks to edit the tutorial, add tutorial steps, or debug tutorial issues.
---

# Tutorial System Framework

## Architecture
The tutorial system is designed to be a non-invasive wrapper around the core game logic. Do NOT use `Time.timeScale = 0f` to pause the game, as this breaks animations, UI events, and network synchronization.

Instead, the system relies on extending the player's turn time infinitely and controlling AI bots.

### Core Components
1. **`TutorialManager` (MonoBehaviour)**
   - Entry point for the tutorial. 
   - Checks `PlayerPrefs.GetString("GameMode") == "Tutorial"`.
   - Stores references to important UI elements (e.g., `endTurnButton`, `clearButton`) to avoid `GameObject.Find()`.
   - Manages the `TutorialUIMask`.

2. **`TutorialGameProxy` (C# Class)**
   - Acts as a bridge between the tutorial logic and the `GameController`.
   - Responsible for setting up the tutorial environment (e.g., `turnDuration = 99999f`).
   - Handles toggling AI bots (`SetBotsEnabled`) safely.

3. **`TutorialStepHandler` (C# Class)**
   - Contains the step-by-step logic of the tutorial.
   - Uses `yield return` extensively to wait for user actions or game state changes.
   - ALWAYS uses `WaitUntilOrTimeout` or `WaitUntilClickOnRect` instead of raw `WaitUntil` to prevent permanent hangs.

4. **`TutorialUIMask` (MonoBehaviour)**
   - A full-screen UI overlay that blocks clicks outside a specific target.
   - Uses `FocusOn(RectTransform target, string message)` to highlight specific UI elements.
   - Animates smoothly using `Time.unscaledDeltaTime`.

## Rules and Best Practices

### 1. Wait Functions (CRITICAL)
- **NEVER** use `yield return new WaitUntil(...)` without a timeout. If a reference becomes null or a condition is never met, the game will hang permanently.
- **ALWAYS** use `WaitUntilOrTimeout(condition, timeout, label)`.
- **NEVER** use `onClick.AddListener` to wait for a button click if the button might be `interactable = false`. Instead, use `WaitUntilClickOnRect`, which checks mouse/touch position against the `RectTransform`.

### 2. Bot Management
- Bots must be disabled at the start of the tutorial to prevent them from taking actions while the player is reading.
- When the tutorial requires the player to end their turn, you MUST enable the bots immediately before waiting for the turn to cycle back.
- Example:
  ```csharp
  proxy.SetBotsEnabled(true);
  yield return WaitUntilOrTimeout(() => proxy.CurrentPlayerIndex == 0, 120f, "WaitPlayerTurn");
  proxy.SetBotsEnabled(false);
  proxy.ResetTurnTime();
  ```

### 3. Finding UI Elements
- Do NOT use `GameObject.Find()`. It is slow and breaks easily if the UI hierarchy changes.
- Add public references to `TutorialManager` and assign them in the Unity Inspector.
- For dynamic elements (like cards or resource buttons), use helper methods that check properties (e.g., `resourceType == "RAM"`) instead of hardcoded indices (e.g., `bankButtons[1]`).

### 4. UI Mask and Dialogues
- Always show a descriptive message BEFORE highlighting a button.
- Pattern:
  ```csharp
  tm.uiMask.FocusOn(null, "Explanation message here.");
  yield return WaitUntilTap();
  
  tm.uiMask.FocusOn(targetBtn, "Action message here. Click this.");
  yield return WaitUntilClickOnRect(targetBtn);
  ```

### 5. Skip Functionality
- Support skipping the tutorial. Check `tm.skipRequested` after every `yield return`.
- If `skipRequested` is true, immediately `yield break;`.
