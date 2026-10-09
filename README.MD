# Simple MXEngine

Simple MXEngine is a small Unity UI navigation framework for prototypes, casual
games, puzzle games, and teams that do not need an MVP layer for every prefab.
Each UI prefab can use one ordinary `MonoBehaviour` for its local state,
interaction, animation, and presentation logic.

MXEngine owns only UI navigation and UI lifetime. It does not provide gameplay
state, dependency injection, an event bus, or a replacement state container.

## Dependencies

- Unity and uGUI
- UniTask
- Addressables

All asynchronous MXEngine APIs return `UniTask`/`UniTask<T>`. The framework does
not expose `System.Threading.Tasks.Task` APIs.

## Quick start

### 1. Create the UI root

Add `UIRoot` to a persistent GameObject and assign active transforms for
`ScreenRoot`, `ModalRoot`, and `OverlayRoot`. UI instances are parented into
these layers automatically.

### 2. Create an Addressable prefab

Create a prefab with the UI component on its root GameObject. Mark it
Addressable and choose a key such as `UI/Home`. The prefab root may be active or
inactive; MXEngine stages it inactive and activates it only after navigation
commits.

### 3. Write the UI component

No MXEngine base class is required:

```csharp
using UnityEngine;
using UnityEngine.UI;

public sealed class HomeScreen : MonoBehaviour
{
    [SerializeField] private Button playButton;

    private void Awake()
    {
        playButton.onClick.AddListener(OnPlayClicked);
    }

    private void OnEnable()
    {
        // Refresh local UI state when the screen becomes visible.
    }

    private void OnDestroy()
    {
        playButton.onClick.RemoveListener(OnPlayClicked);
    }

    private void OnPlayClicked()
    {
        // Handle local interaction or call a game service.
    }
}
```

### 4. Create the navigation service

```csharp
using MXEngine;

var navigation = new NavigationService(new AddressableViewLoader(), uiRoot);
```

Keep the service with the owner of the UI root. Call `ShutdownAsync` when that
owner is destroyed.

### 5. Navigate

```csharp
var home = await navigation.ShowScreenAsync<HomeScreen>("UI/Home");
var settings = await navigation.ShowModalAsync<SettingsPopup>("UI/Settings");
var loading = await navigation.ShowOverlayAsync<LoadingOverlay>("UI/Loading");

await navigation.BackToPreviousScreenAsync();
await navigation.CloseModalAsync();
await navigation.CloseAllModalsAsync();
await navigation.HideOverlayAsync("UI/Loading");
```

Use `stack: false` to replace the screen history:

```csharp
await navigation.ShowScreenAsync<GameplayScreen>("UI/Gameplay", stack: false);
```

Every open method returns the requested `MonoBehaviour` instance. The same
screen key at the top of the stack is reused. A screen key already deeper in
the stack is brought to the top without instantiating another prefab. Modals
always create a new instance, including repeated uses of the same key.

## Lifecycle and ownership

- Unity calls `Awake`, `OnEnable`, `OnDisable`, and `OnDestroy` normally.
- A loaded prefab remains inactive until it has been prepared and navigation
  commits it to its Screen, Modal, or Overlay collection.
- A failed or cancelled request is cleaned up before it is reported.
- Addressables handles are owned by the loader, not by UI components.
- Do not call Addressables release directly for an instance returned by the
  navigation service.
- `ShutdownAsync` releases every screen, modal, and overlay owned by the
  service.
- If a release fails, the instance remains tracked and can be retried with
  `RetryPendingReleasesAsync`.
- `CleanupFailed` reports cleanup errors that occur after navigation has
  committed successfully.
- Navigation requests are serialized per screen/modal and overlay channel.
  Requests made from a UI lifecycle callback fail immediately instead of
  deadlocking the service.

Pass a `CancellationToken` to any open or close method when a caller owns the
request lifetime. Cancellation before commit does not remove the current
screen.

## API summary

| API | Behavior |
| --- | --- |
| `ShowScreenAsync<T>(key, stack)` | Opens or reuses a screen in `ScreenRoot`. |
| `ShowModalAsync<T>(key)` | Pushes a new modal instance into `ModalRoot`. |
| `ShowOverlayAsync<T>(key)` | Opens one overlay for a key in `OverlayRoot`. |
| `BackToPreviousScreenAsync()` | Releases the top screen and activates the previous one. |
| `CloseModalAsync()` | Releases the top modal. |
| `CloseAllModalsAsync()` | Releases all modals. |
| `HideOverlayAsync(key)` | Releases the overlay for a key. |
| `ShutdownAsync()` | Stops requests and releases all owned UI. |

See `Assets/MXEngine/Samples/NavigationDemo` for a complete example covering
screens, modals, overlays, buttons, Addressables, and returned instances.
