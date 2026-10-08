# Troubleshooting

[Back to index](index.md)

This page has two parts. The first has one section per diagnostic id, `UIDIRECTOR101` to `UIDIRECTOR126`: what the diagnostic means, why it happens, how to fix it and how serious it is. The second part starts from symptoms, things you see in the game or in the editor that carry no id.

UIDirector reports every problem it can name with a stable diagnostic id. An id never changes meaning and is never reused, so you can search for it, link to it and filter on it. Each id has one section on this page.

The package logs a diagnostic as `UIDIRECTOR1xx: <message> (see Troubleshooting#uidirector1xx)` through `Debug.LogError` or `Debug.LogWarning`, in the editor and in development builds. Release builds log nothing. To find them, type `UIDIRECTOR` in the Console search box, or the full id such as `UIDIRECTOR104`. The message names the view id or layer involved.

Ids 101 to 120 and 126 are reported at run time. Ids 121 to 124 are reported only by the editor validator, which you start with `Tools/UIDirector/Validate Catalogs`; it checks every `ViewCatalog` in the project before you press Play. Id 125 is reported by the validator and, at run time, by the root when it registers a catalog. The validator also checks the catalog conditions behind ids 101, 102, 103, 108, 109 and 120.

Most run-time errors that stop an open also end that request without showing the view. The awaited `ViewResult` then reports `WasShown == false`, a `Rejected` reason and a `Detail` text.

## UIDIRECTOR101

**Unknown view id: no catalog row has the id that was requested.**

**What happened.** An open call, or `PreloadAsync`, used a `ViewKey` whose id is not in any catalog registered on the `ViewRoot`. Nothing was opened. An open ends as not shown with `RejectReason.UnknownKey`; `PreloadAsync` returns `false`.

**Why.** The id is mistyped, the row does not exist, or the catalog that holds it is not registered, either because it is missing from the `Catalogs` list on the `ViewRoot` or because it was never added with `AddCatalog`. Ids are case-sensitive strings.

**Fix.**
1. Compare the id in the log message with the `Id` field of the row in your `ViewCatalog` asset.
2. Make the catalog appear in the `Catalogs` list of the `ViewRoot` Inspector, or register it in code with `INavigator.AddCatalog` before the call.
3. Keep keys in one static class marked with `[ViewKeys]` so the validator can check them; run `Tools/UIDirector/Validate Catalogs`.

**Severity.** Error. Reported at run time in the editor and development builds.

## UIDIRECTOR102

**The prefab has no view for the key's argument and result types.**

**What happened.** The prefab loaded, but its root has no component that implements `IView<TArgs, TResult>` for the key you opened. The request ended as not shown with `RejectReason.TypeMismatch`.

**Why.** The key says `ViewKey<A, R>` while the view component implements different types, for example the key has arguments and the view is a `ViewBehaviour<NoArgs, NoResult>`. The view component is also looked up on the prefab root only; a view component on a child object is not found.

**Fix.**
1. Make the key's `TArgs` and `TResult` equal to the type arguments of the view component.
2. Put the view component on the same GameObject as the `ViewHost`, the prefab root.
3. Run `Tools/UIDirector/Validate Catalogs` to find the mismatch before play.

**Severity.** Error. Reported at run time in the editor and development builds.

## UIDIRECTOR103

**The prefab root has no `ViewHost`.**

**What happened.** The instance was created, but its root has no `ViewHost` component. The request ended as not shown with `RejectReason.TypeMismatch`, and the detail text says the instance has no view host.

**Why.** Every view prefab root needs a `ViewHost`. It is what connects the object to its request. It was not added, or it sits on a child instead of the root.

**Fix.**
1. Open the prefab and add the `ViewHost` component to the root object. It requires a `Canvas` and a `GraphicRaycaster`, which Unity adds with it.
2. Optionally assign a transition in the `ViewHost` Inspector.
3. Run `Tools/UIDirector/Validate Catalogs`; it flags prefabs without a `ViewHost`.

**Severity.** Error. Reported at run time in the editor and development builds.

## UIDIRECTOR104

**Load failed: the view could not be created.**

**What happened.** Creating the view instance threw, the provider returned nothing, or preparing the instance (injection, binding or placement) threw. The request ended as not shown with `RejectReason.LoadFailed`, and the log message and `Detail` carry the exception message. The exception itself goes to the root's error handler (`ViewRootSetup.ErrorHandler`), or to `Debug.LogException` when none is set. It arrives as `ViewErrorSource.Inject` when `ViewRootSetup.Inject` threw, and as `ViewErrorSource.Provider` for every other cause. An instance that was destroyed before it was prepared fails the same way, as `Provider`. When the failed request was reopening a view that is already on screen, that on-screen instance is released once and the view disappears.

**Why.** Common causes are an asset key that Addressables cannot resolve, a missing or broken addressable group, a prefab that failed to instantiate, or an `Inject` callback that threw.

**Fix.**
1. Read the exception message in the log; for Addressables it names the key.
2. For an asset key, check that the key exists in an Addressables group and that the group's content is built and available to the player.
3. If you set `ViewRootSetup.Inject`, check that it does not throw for this prefab.
4. For a direct prefab, check that the row's `Prefab` reference is not missing.

**Severity.** Error. Reported at run time in the editor and development builds.

## UIDIRECTOR105

**Load timed out: the view took longer than the load timeout.**

**What happened.** Loading the view was still running when the load timeout passed. The load was cancelled and the request ended as not shown with `RejectReason.LoadTimedOut`.

**Why.** The default is 15 seconds of unscaled time. A slow or stalled download, a provider that never completes, or a timeout set too low for the platform will trigger it.

**Fix.**
1. Find out why the load is slow: check the network, the Addressables catalog and the asset size.
2. Preload the view before it is needed with `INavigator.PreloadAsync(viewId, owner)`.
3. If the load legitimately takes longer, raise `Load Timeout Seconds` on the `ViewRoot` Inspector (minimum 0.1).

**Severity.** Error. Reported at run time in the editor and development builds.

## UIDIRECTOR106

**A transition timed out and was snapped to its end.**

**What happened.** A show or hide transition did not finish within the transition timeout. The package cancelled it, put the view in its final state at once, and carried on as if the transition had ended. The request is not lost.

**Why.** The default is 3 seconds of unscaled time. A custom `ViewTransition` that never completes is the usual cause, for example one that animates on scaled time while `Time.timeScale` is 0. The built-in Fade, Slide and Scale transitions use unscaled time. Game code that calls `DOTween.KillAll()` or `DOTween.Clear()` while a `TweenTransition` plays may also prevent a clean finish: a kill ends the transition's await early and leaves the view in whatever pose the tween had reached, and `DOTween.Clear()` may remove the tween without running the kill callback the await waits for. The transition may then stay pending until the transition timeout snaps it to its end and logs this warning.

**Fix.**
1. The log message names the view and whether it was the show or the hide transition. Open that prefab's `ViewHost` and check the transition assigned to it.
2. In a custom transition, animate on unscaled time and always complete the returned task, also when it is cancelled.
3. If a transition really needs longer than 3 seconds, raise `Transition Timeout Seconds` on the `ViewRoot` Inspector.
4. Do not call `DOTween.KillAll()` or `DOTween.Clear()` while a view is opening or closing. To stop one tween of your own, kill that tween.

**Severity.** Warning. Reported at run time in the editor and development builds.

## UIDIRECTOR107

**An open view was destroyed or deactivated by code outside the package.**

**What happened.** The GameObject of a view that the package was showing was destroyed, or deactivated (`SetActive(false)`), by something other than UIDirector. The package ended the request: a view that was already shown ends with `CloseReason.Destroyed`, and one lost before it was shown ends as not shown with `RejectReason.Cancelled`. The layer, backdrop and input shield were cleaned up. The instance is released, not kept, also when the row's retention is `KeepInstance`: the next open of that id creates a new instance. A kept instance that is already parked and is destroyed outside the package is dropped without this warning, and the next open creates a new one as well.

**Why.** UIDirector owns the lifetime of a view instance. Calling `Destroy` on it, or `SetActive(false)` on it or on one of its parents, ends the request. Deactivating the `ViewRoot` object or a layer object therefore ends every view under it. Only a view lost while its root lives is reported: a scene unload or the destruction of the root ends its views without logging 107, because the root's teardown ends every request itself and drops the pending loss reports. The package decides one frame after the loss, so a view that is deactivated and active again within that frame ends nothing and logs nothing.

**Fix.**
1. Close a view through the package: `ViewHandle.Close()`, `ViewHost.Close()`, the view's own `Complete`/`Close`, or `INavigator.Back()`.
2. To hide the whole UI for a moment, set a `CanvasGroup` on the root object to alpha 0 and not interactable, and hold input with `INavigator.HoldInput`, as [Hide the whole UI for a moment](Root-Canvas.md#hide-the-whole-ui-for-a-moment) shows. Do not disable the root `Canvas` component: the view canvases keep drawing.
3. To find the code that removed the view, look at the stack of the call that destroyed or deactivated it; the log names the view id and request id.

**Severity.** Warning. Reported at run time in the editor and development builds.

## UIDIRECTOR108

**Duplicate view id: a second row with the same id was refused.**

**What happened.** While registering a catalog, a row had an id that is already registered, in the same catalog or in another one. The second row was ignored; the first keeps the id.

**Why.** View ids must be unique across every catalog on one root.

**Fix.**
1. Search your catalogs for the id in the log message and rename one row.
2. Update every `ViewKey` that uses the renamed id.
3. Run `Tools/UIDirector/Validate Catalogs`, which lists duplicates across all catalogs.

**Severity.** Error. Reported at run time in the editor and development builds.

## UIDIRECTOR109

**A row names a layer that does not exist.**

**What happened.** A catalog row's layer id matches no layer of the root. At registration the row was refused and is never registered: a request for its id ends as not shown with `RejectReason.UnknownKey` and logs [UIDIRECTOR101](#uidirector101).

**Why.** A root has three built-in layers with the ids `screens`, `popups` and `system`, plus any layers added in code through `ViewRootSetup.ExtraLayers`. The layer id is compared as written, so a typo or a different case does not match. A row also fails this way when its extra layer is not declared on the root that registers the catalog. The layer is checked before the id is checked for duplicates, so a refused row does not take its id: a later row with the same id on a layer that exists is registered, and no [UIDIRECTOR108](#uidirector108) is reported for it. The validator reports a key that points at a refused row as [UIDIRECTOR101](#uidirector101) and names that row.

**Fix.**
1. Set the row's `Layer Id` to `screens`, `popups` or `system`, or to the id of a layer you pass in `ViewRootSetup.ExtraLayers`.
2. Make sure the root that registers the catalog declares that layer before the catalog is added.

**Severity.** Error. Reported at run time in the editor and development builds.

## UIDIRECTOR110

**An input hold has been active for more than 30 seconds.**

**What happened.** A `HoldInput` registration has kept the input shield up for 30 seconds. The warning is logged once per hold, with its reason and the call site that took it. Input stays blocked until the hold ends.

**Why.** `INavigator.HoldInput(reason, owner)` blocks input until its owner token is cancelled or the registration is removed. The owner token never ended and nothing called `Remove()`, or the awaited work behind the hold is stuck.

**Fix.**
1. Find the call site in the message and check what ends that hold.
2. Pass an owner token that ends with the work, for example the token of the object that took the hold.
3. Call `ViewRegistration.Remove()` on every path of the work, or tie the hold to a token so it cannot be forgotten.
4. To see every current hold, call `INavigator.Describe(StringBuilder)` and read the shield section, which lists each hold with its age and call site.

**Severity.** Warning. Reported at run time in the editor and development builds.

## UIDIRECTOR111

**A queue hold has been active for more than 60 seconds.**

**What happened.** A `ViewQueue.Hold(reason, owner)` registration has been active for 60 seconds, so the queue has not presented any waiting item in that time. The warning is logged once per hold and names the reason, the age and the call that took the hold (member and line).

**Why.** A hold keeps waiting items from being presented until its owner token ends or its registration is removed. Either the owner never ended or nobody removed the registration, or the flow that took the hold is stuck.

**Fix.**
1. Open the call site named in the message, or search for the `ViewQueue.Hold` call with the reason shown in it.
2. Give it an owner token that ends with the flow, and call `ViewRegistration.Remove()` on every path of the flow.
3. Call `ViewQueue.Describe(StringBuilder)` to list the holds and their ages.

**Severity.** Warning. Reported at run time in the editor and development builds.

## UIDIRECTOR112

**A layer reached its waiting cap; the new request was refused.**

**What happened.** A layer already had the maximum number of requests waiting, so a new open call was rejected. The request ended as not shown with `RejectReason.Busy`.

**Why.** Immediate opens are accepted and run one after another, but one layer keeps at most 4 requests waiting by default. Code that opens views faster than they can finish, such as a handler that runs on every frame or a button that is tapped repeatedly, reaches the cap.

**Fix.**
1. Find what opens so many views. The log names the layer.
2. For notices and other views that may wait, use a `ViewQueue` on that layer instead of opening immediately.
3. For tap handlers, open with `RejectWhenBusy` so a repeated tap is dropped quietly.
4. If the burst is intended, raise `Waiting Cap` on the `ViewRoot` Inspector (minimum 1).

**Severity.** Warning. Reported at run time in the editor and development builds.

## UIDIRECTOR113

**An owner token was cancelled on a thread other than the main thread; it was handled on the main thread.**

**What happened.** A `CancellationToken` that owns a view, a registration, a queue item or a queue (its lifetime token, or the root token that ends it) was cancelled from another thread, for example by a timer-based `CancellationTokenSource`. The package posted the work to the main thread, so the view closed or the registration was removed there. The result is correct; the warning marks a design smell.

**Why.** UIDirector is main-thread only. It accepts an owner cancelled elsewhere and moves it to the main thread, but it logs this so you know cancellation does not take effect at the moment of the call.

**Fix.**
1. Cancel owner tokens from the main thread when you can, for example from `Update` or after `await UniTask.SwitchToMainThread()`.
2. Do not rely on the view being closed immediately after `Cancel()` returns; it closes on the next main-thread turn.
3. `ViewRegistration.Remove()` is safe from any thread and does not log this warning.

**Severity.** Warning. Reported at run time in the editor and development builds.

## UIDIRECTOR114

**The root canvas is in Screen Space - Camera mode but has no camera.**

**What happened.** The `ViewRoot` was initialised with its Canvas in `Screen Space - Camera` mode, and its `Render Camera` is now empty. Views may not render. The warning is logged when a view is acquired, once for each stretch of time that the camera is missing.

**Why.** The camera was destroyed, usually because it lived in a scene that was unloaded while the root persisted. Unity then silently reads the canvas as `Screen Space - Overlay`, so the package remembers the mode it started with.

**Fix.**
1. Keep the UI camera alive as long as the root by making it a sibling of the root under a shared parent. `GameObject > UI > UIDirector Root (Camera)` builds exactly that: a `UIDirector` object that holds the root (`UIDirector Root`) and a `UI Camera`, with the root canvas in `Screen Space - Camera` mode and the camera assigned to it.
2. Never make the camera a child of the root or of its canvas. A `Screen Space - Camera` canvas that sits on its own child camera is placed at the camera's position and draws nothing.
3. Turn on `Persist Across Scenes` on the `ViewRoot`. It calls `DontDestroyOnLoad` on the outermost object of the hierarchy the root belongs to, so with the layout above the holder, the root and the camera persist together. A camera that sits elsewhere in the scene does not persist.
4. If you replace the camera, reassign `Canvas.worldCamera` on the root canvas.
5. Or switch the root canvas to `Screen Space - Overlay`, which needs no camera.

**Severity.** Warning. Reported at run time in the editor and development builds.

## UIDIRECTOR115

**The sorting band is exhausted; the request was refused.**

**What happened.** A request could not get a sorting order that fits, so the open was stopped. The request ended as not shown with `RejectReason.Busy`, and the log message names the layer, the next slot and the base order.

**Why.** Each view gets `Base Sorting Order + layer order x 1000 + slot x 10`. A layer has slots 1 to 99, a slot is the highest live slot plus one, and the whole band, including the input shield above the top layer, must stay below 29999. Either 99 views are live on one layer, or the base order is so high that the band no longer fits. Unity stores sorting orders as signed 16-bit values and wraps silently, so the package refuses instead of wrapping.

**Fix.**
1. Lower `Base Sorting Order` on the `ViewRoot` Inspector. The default is 0.
2. Close views you no longer need. A new view takes the highest live slot plus one, so slots only come back when the views on top end.
3. Check for a loop or a leak that opens views and never closes them.
4. Avoid very high layer orders in custom layers.

**Severity.** Error. Reported at run time in the editor and development builds.

## UIDIRECTOR116

**There is no EventSystem in the scene.**

**What happened.** When the `ViewRoot` initialised, the scene contained no `EventSystem`. Views are shown, but they receive no clicks or touches. The warning is logged once at initialisation.

**Why.** UI input needs an `EventSystem` and an input module. The package does not create one.

**Fix.**
1. Add one with `GameObject > UI > Event System`.
2. If you create the `EventSystem` in code, create it before the `ViewRoot` initialises, or ignore the warning when you know it will exist before the first input.

**Severity.** Warning. Reported at run time in the editor and development builds.

## UIDIRECTOR117

**`OnOpen` of a view threw an exception.**

**What happened.** The `OnOpen` method of the view threw. The exception goes to the root's error handler, or to `Debug.LogException`. The request ended as not shown with `RejectReason.Faulted`, and the log names the view and the exception type.

**Why.** There is a bug in `OnOpen`, or it ran on data that was not ready: a missing reference, a null argument, an unassigned Inspector field.

**Fix.**
1. Read the exception in the Console; it is logged next to this message.
2. Fix `OnOpen`, or check its arguments.
3. If the view cannot open for a legitimate reason, call `Decline(reason)` in `OnOpen` instead of throwing. The request then ends as not shown with `RejectReason.Declined`.

**Severity.** Error. Reported at run time in the editor and development builds.

## UIDIRECTOR118

**`Complete` or `Close` was called inside `OnOpen`.**

**What happened.** `OnOpen` called `Complete(value)` or `Close()`. The package latched the call: the view opens, and then it closes at once. The awaiter receives the result you gave. You see a view flash and close, which is rarely what you want.

**Why.** `OnOpen` runs before the view is shown, and the view cannot close while it is still opening. If you want to refuse the open, closing is the wrong tool.

**Fix.**
1. To refuse the open, call `Decline(reason)` in `OnOpen`. The view is never shown and the request ends as not shown with `RejectReason.Declined`.
2. To close after the view has been shown, call `Complete` or `Close` later, for example from a button handler or after the visible scope starts.

**Severity.** Warning. Reported at run time in the editor and development builds.

## UIDIRECTOR119

**A gate threw an exception; the request was treated as denied.**

**What happened.** An `IViewGate` added to a `ViewQueue` threw while it was asked about a request. The exception is routed to the root's error handler. The request is denied with a reason that names the gate type, and gates after the failing one are not asked.

**Why.** A bug in the gate's `Evaluate` method. A gate must decide with the information in the `GateRequest`; it must not depend on state that can be null or already destroyed.

**Fix.**
1. The log message names the gate type, the view id and the request id. The exception itself is logged next to it.
2. Fix `Evaluate` so it never throws. Return `GateDecision.Deny(reason)` for a normal refusal.
3. Remove a gate you no longer need with `Remove()` on the `ViewRegistration` that `AddGate` returned.

**Severity.** Error. Reported at run time in the editor and development builds.

## UIDIRECTOR120

**A row has an asset key, but the root has no view provider.**

**What happened.** A view whose row uses an `Asset Key` was opened or preloaded, but the root has no `IViewProvider` to resolve keys. An open throws inside the load and ends as not shown with `RejectReason.LoadFailed`; a preload returns `false`.

**Why.** A row without a `Prefab` needs a provider. The root takes one from `ViewRootSetup.AssetProvider`, or from an `IViewProvider` component on the same object. None was supplied, or the Addressables assembly is not compiled because the Addressables package is not installed.

**Fix.**
1. Add an `AddressablesViewProvider` component next to the `ViewRoot`, or pass a provider in `ViewRootSetup.AssetProvider` when you call `Initialize`.
2. Install the Addressables package if `AddressablesViewProvider` does not exist in your project.
3. Or fill in the row's `Prefab` field to use a direct reference instead of an asset key.
4. Run `Tools/UIDirector/Validate Catalogs` to list all rows that use an asset key.

**Severity.** Error. Reported at run time in the editor and development builds.

## UIDIRECTOR121

**A catalog row has neither a prefab nor an asset key.**

**What happened.** The validator found a row with an empty `Prefab` and an empty `Asset Key`. Opening that view would have nothing to load.

**Why.** The row was added but not finished, or its prefab reference was lost when the prefab was deleted.

**Fix.**
1. Open the `ViewCatalog` asset and find the row named in the validator message.
2. Assign its `Prefab`, or enter an `Asset Key` for Addressables, or delete the row.

**Severity.** Error. Reported by the editor validator only (`Tools/UIDirector/Validate Catalogs`); never at run time.

## UIDIRECTOR122

**A row uses `BackPolicy.Close` on a layer that does not take back.**

**What happened.** The validator found a row whose `Back` policy is `Close` on a layer without the `TakesBack` rule, such as the built-in `system` layer. That row never closes on Back, because the layer does not take back requests.

**Why.** `BackPolicy.Close` only has an effect where the layer lets back close its top view. On `system` back is swallowed. The policy on the row looks as if it should work, but it does nothing.

**Fix.**
1. Change the row's layer to one that takes back, such as `popups`, or to a custom layer with `ViewLayerRules.TakesBack`.
2. Or set the row's `Back` to `LayerDefault` or `Ignore` so the setting matches what happens.
3. Or give the view its own close button that calls `ViewHost.Close()` or `Close()`.

**Severity.** Warning. Reported by the editor validator only (`Tools/UIDirector/Validate Catalogs`); never at run time.

## UIDIRECTOR123

**A view prefab contains a dropdown.**

**What happened.** The validator found a dropdown (`TMP_Dropdown` or `Dropdown`) inside a view prefab. This is not an error, but it has a known limit.

**Why.** An open dropdown creates its own list canvas and a full-screen blocker at the top of the sorting range. When the view is covered by another view, the list is hidden with the view, but a TextMesh Pro dropdown's blocker is parented under the root canvas and stays. It is invisible, covers the screen and takes the next tap, which only closes the dropdown.

**Fix.**
1. Close open dropdowns when the view stops being visible: do it in the visible scope's end, or when your code opens another view.
2. Or replace the dropdown with a list that your own view controls.
3. If you accept the limit, ignore the warning.

**Severity.** Warning. Reported by the editor validator only (`Tools/UIDirector/Validate Catalogs`); never at run time.

## UIDIRECTOR124

**An asset key matches no Addressables address, GUID or label.**

**What happened.** The validator found a row whose `Asset Key` is not the address, the GUID or a label of any Addressables entry. With the Addressables provider that row would fail to load. The check runs only when the Addressables editor package is present.

**Why.** The key was mistyped, the asset was removed from its group or was never made addressable, or the key refers to another project configuration. It can also be a valid key for a custom `IViewProvider`, which the validator cannot know.

**Fix.**
1. Open `Window > Asset Management > Addressables > Groups` and check that the prefab is marked addressable and that its address, GUID or a label equals the row's `Asset Key`.
2. Correct the key in the `ViewCatalog` row or the address in the group.
3. If the root uses another asset provider and the key is valid for it, ignore the warning.
4. Run `Tools/UIDirector/Validate Catalogs` again.

**Severity.** Warning. Reported by the editor validator only (`Tools/UIDirector/Validate Catalogs`); never at run time. A batch run does not fail on it.

## UIDIRECTOR125

**A catalog row has an empty id.**

**What happened.** The validator, or the root when it registered the catalog, found a row whose id is empty or only whitespace. The row was refused: no view can be opened through it, and no other rule is checked for it. The runtime message names the row's index and its catalog. The root checks the id before the layer and the duplicate check, so an empty id is reported as 125 and never as 109 or 108.

**Why.** The row was added but its id was never filled in, or the id text was cleared. An id with only spaces counts as empty. (A `ViewKey` cannot be built from such an id either: its constructor throws `ArgumentException`.)

**Fix.**
1. Open the `ViewCatalog` asset and find the row named in the validator message.
2. Enter the id that your `ViewKey` uses, or delete the row.
3. Run `Tools/UIDirector/Validate Catalogs` again.

**Severity.** Error. Reported by the editor validator (`Tools/UIDirector/Validate Catalogs`), and at run time in the editor and development builds when a catalog with such a row is registered with a root.

## UIDIRECTOR126

**A queue presented a view whose row is on another layer.**

**What happened.** A `ViewQueue` reached the item at the head of its queue, but the catalog row for that key is on a different layer than the queue's. The navigator refused the request: the queued item ends as not shown with `RejectReason.WrongLayer`, nothing is placed and the input shield is not taken.

**Why.** A queue serializes the views of one layer. The most likely cause is the row's layer: its `Layer Id` was left on `screens` (its default, or copied from the row above it) while the queue is on `popups`. Otherwise the key was enqueued on the wrong queue, or the row's layer was changed (or another catalog with the same id was registered) after the item was enqueued. The check runs when the item is presented, against the rows registered at that moment.

**Fix.**
1. The log message names the view id, the row's layer and the queue's layer.
2. Enqueue the key on the queue for the row's layer, or move the row to the queue's layer in the `ViewCatalog`.
3. If the rows change at run time, register the catalog before you enqueue its keys.
4. The queue's constructor already refuses a layer that the root does not have (`ArgumentException`); this diagnostic covers a key whose row is on a different layer than a layer the queue does have.

**Severity.** Error. Reported at run time in the editor and development builds.

---

## A view does not appear in the camera variant

**What you see.** The root was made with `GameObject > UI > UIDirector Root (Camera)`. `ShowAsync` reports the view as shown, the shield and backdrop behave, but the view is not drawn.

**Why.** The `UI Camera` that the menu creates draws only the `UI` layer (layer 5). The package never changes the layer of a view prefab; it instantiates the prefab as authored. The objects it builds itself (the layer objects, the input shield and a generated backdrop) take the layer of the root object, which the menu sets to `UI`. A view prefab on another layer, for example `Default`, is culled by that camera. A root in `Screen Space - Overlay` mode does not depend on layers, so this only shows in the camera variant.

**Fix.**
1. Put the view prefab root and its children on the `UI` layer. In the Inspector, change the layer of the root and answer "Yes, change children".
2. Do the same for a custom backdrop prefab and for the load indicator object if you assigned them on the `ViewRoot`.
3. If the whole UI is missing and not only some views, check that the camera is a sibling of the root and not a child of its canvas; see [UIDIRECTOR114](#uidirector114).

## The loading indicator draws behind views

**What you see.** A load takes longer than the indicator delay, the `Load Indicator` object is switched on, but views or their backdrop cover it.

**Why.** The package sorts the indicator itself each time it shows it. It reuses a `Canvas` on the indicator object, or adds one the first time, sets `Override Sorting`, copies the sorting layer of the root canvas and sets the sorting order to one above the input shield. The shield sits above the top layer, so the indicator should cover every view. It adds no `GraphicRaycaster`, so the indicator takes no input. Three things can still hide it:

- The indicator only appears after a load has run longer than `Indicator Delay Seconds` (0.4 by default), only while a load runs, and only for a view on a layer that shields input (the three built-in layers do).
- Values you set on the indicator's own canvas (sorting order, override sorting, sorting layer) are overwritten each time the indicator is shown. Setting them by hand has no lasting effect.
- Something outside the package sorts above the root canvas, for example another canvas on a higher sorting layer, or a TextMesh Pro dropdown list (see the next section).

**Fix.**
1. Assign the indicator object to `Load Indicator` on the `ViewRoot`. With no object assigned nothing is shown.
2. If the indicator has its own canvas, leave its sorting fields alone; the package manages them.
3. Check other canvases in the scene for a higher sorting layer or order than the root canvas, and lower them.
4. To see the indicator sooner, lower `Indicator Delay Seconds` on the `ViewRoot`; `0` shows it on the next update after a load starts.

## A dropdown list stays above everything

**What you see.** An open dropdown list is drawn over later views, the input shield or the load indicator. After its view was covered, the next tap does nothing except close the dropdown.

**Why.** A TextMesh Pro dropdown puts its open list on its own canvas with sorting order 30000 and a full-screen blocker with order 29999, parented under the root canvas. The package keeps its own sorting band below 29999, so nothing it draws can cover them. When the view that owns the dropdown is covered or closed, the list is hidden with it but the blocker stays, invisible and catching the next tap. The validator reports a dropdown in a view prefab as [UIDIRECTOR123](#uidirector123).

**Fix.**
1. Close the dropdown when the view's visible scope ends: in the view's `OnVisible(ViewScope visible)`, register `visible.OnEnd(...)` with an action that calls the dropdown's `Hide()`.
2. Also close it before your code opens another view from inside the dropdown's handler.
3. Or replace the dropdown with a list that your own view controls and that sorts inside the view.

## The validator reports keys in my test assembly

**What you see.** `Tools/UIDirector/Validate Catalogs` reports key fields of a `[ViewKeys]` class that you consider test code, or it does not report keys that you expect it to check.

**Why.** The validator skips `[ViewKeys]` classes that live in a test assembly, because test fixtures have no catalog rows. It recognises a test assembly by its references: an assembly that references `nunit.framework` is a test assembly, whatever its name or folder. Two consequences follow. A production assembly that references `nunit.framework` is skipped, so its keys are never checked. A test assembly whose compiled code uses no NUnit type may carry no recorded reference to `nunit.framework`, because a compiler records only the references it uses; such an assembly is not recognised, and its keys are checked.

**Fix.**
1. Check which assembly the key class is in (the `.asmdef` next to the file) and whether that assembly uses an NUnit type.
2. To have the validator skip a fixture class, move it into an assembly whose code uses NUnit, for example next to your test classes.
3. To have the validator check a class, move it out of any assembly that references `nunit.framework`.

## Back does nothing while a system overlay is open

**What you see.** The hardware back key, or your call to `INavigator.Back()`, does not close the overlay on the `system` layer, and `BackInput.Unhandled` is not raised.

**Why.** The `system` layer is modal but does not take back, so by design `Back()` swallows the request and returns `true`. Because the call counts as handled, the game gets no `Unhandled` signal either. The same swallow happens in two other cases: while the input shield is up (an operation such as a load or a transition is running, or a `HoldInput` hold stands), and while the top view is still in its show or hide transition.

**Fix.**
1. Give the overlay its own way to finish: the work behind it calls `Complete` or `Close` on the view, or the code that opened it calls `ViewHandle.Close()`.
2. To let the overlay answer back itself, set the row's `Back` to `HandledByView` and implement `IViewBack` on the view. `OnBack` is then asked; if it returns false the request is still swallowed, because the layer does not take back.
3. `BackPolicy.Close` on a `system` row has no effect; the validator reports it as [UIDIRECTOR122](#uidirector122).
4. If you want back to close the overlay, put the view on a layer that takes back, such as `popups`, or on a custom layer with `ViewLayerRules.TakesBack`.

## The back key or a ViewButton does nothing right after start

**What you see.** Pressing Escape, or clicking a `ViewButton` whose action is `Back` or `OpenView`, does nothing during the first moments of the app. The console shows a warning from the button ("was clicked before its ViewRoot was initialised") and nothing from `BackInput`.

**Why.** The root has not been initialised yet: `Initialize On Awake` is off and your bootstrap has not called `ViewRoot.Initialize`, or an installer has not run. `BackInput` and `ViewButton` do not read `ViewRoot.Navigator` while the root is not initialised, because that read would initialise the root with default settings and defeat your bootstrap. `BackInput` ignores the key without raising `Unhandled`; `ViewButton` logs the warning.

**Fix.**
1. Initialise the root before the first input: call `ViewRoot.Initialize(setup)` in a bootstrap that runs early, or turn `Initialize On Awake` back on if you need no setup.
2. Do not enable the buttons and the `BackInput` of a screen before the bootstrap has finished.
3. A `ViewButton` with a blank view id logs "has no view id" and opens nothing; fill in the id in the Inspector.

## Initialize throws in a Zenject project

**What you see.** `NavigatorInstaller.Install` throws `InvalidOperationException` with the text that the root "was initialised before NavigatorInstaller.Install ran", or a call to `ViewRoot.Initialize` throws "The root is already initialised."

**Why.** With `Initialize On Awake` on, the root initialises itself in `Awake` with default settings and no injector, before the container installs its bindings. The installer then finds an initialised root. Reading `ViewRoot.Navigator` before `Install` runs has the same effect, because the first access initialises the root with defaults. A root that is initialised without the container would show views that receive no injection, so the installer refuses.

**Fix.**
1. Turn off `Initialize On Awake` on the `ViewRoot` Inspector.
2. Call `NavigatorInstaller.Install(container, root)` from `InstallBindings`. It initialises the root with the container as its injector and binds `INavigator` and `ViewRoot`. To pass `ExtraLayers`, `ErrorHandler` or `AssetProvider`, use `Install(container, root, setup)` with a `ViewRootSetup` whose `Inject` is left null.
3. Do not read `ViewRoot.Navigator` or call `Initialize` anywhere that can run before the installer.

## See also

- [Diagnostics](Diagnostics.md)
- [Navigation and back](Navigation-and-Back.md)
- [System overlays](System-Overlays.md)
- [Root canvas](Root-Canvas.md)
- [Zenject](Zenject.md)
- [Threading](Threading.md)
- [Limitations](Limitations.md)
- [FAQ](FAQ.md)
