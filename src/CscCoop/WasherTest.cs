using System;
using System.Linq;
using CoopCore;
using UnityEngine;

namespace CscCoop;

// washer suite (tools/run-pair.ps1 -Test washer -Mission 11): the host takes out its power washer and sets the station
// down; the client, holding its mop, clicks it (PowerWasherStation.Interact -> EquipLance, the click that takes the
// lance out). That click must stop before EquipLance (owner's test of 0.4.3-test: a friend's washer could be used and
// its water never ran out). Then the client clicks its own washer, which must still go through. The test saves own no
// washer: the bots add one mid-job, which skips the station's own setup, so the lance handout itself fails here
// (AddWasherToInventory instantiates null): the suite checks where the click gets to, not the lance in hand.
public partial class Bot
{
    PowerWasherStation wsStation;

    static PowerWasherStation Station(bool other) => UnityEngine.Object.FindObjectsOfType<PowerWasherStation>(true)
        .FirstOrDefault(s => s != null && s.name.Contains("(other player)") == other);

    // What this player has selected (InventoryController.SelectedItem): 0 nothing of a washer, 1 a lance or station of
    // its own, 2 one of another player's (a copy here, named "(other player)").
    static int LancesInHands(GameWorld w)
    {
        var sel = InventoryController.Instance?.SelectedItem;
        var c = sel?.TryCast<Component>();
        if (c == null || (c.GetComponentInParent<PowerWasherLance>() == null && c.GetComponentInParent<PowerWasherStation>() == null)) return 0;
        var st = c.GetComponentInParent<PowerWasherStation>();
        bool others = c.name.Contains("(other player)") || (st != null && st.name.Contains("(other player)")) || Dyn.IsPuppet(c.gameObject) || (st != null && Dyn.IsPuppet(st.gameObject));
        return others ? 2 : 1;
    }

    // The game's way to put the carried station down (the drop key: TryDropStation), so it isn't held any more.
    static void SetDown(GameWorld w, PowerWasherStation st, float dx)
    {
        if (w.Grabber.GrabbedRb != null) w.Grabber.TryRelease();
        st.TryDropStation();
        // Tools added mid-job miss the station's own setup (Start links the inventory's lance to it): link it here.
        if (st.managedWasherObject == null)
        {
            var lance = w.Player.GetComponentsInChildren<PowerWasherLance>(true).FirstOrDefault();
            if (lance != null) st.AssignWasherLance(lance, false);
        }
        Plugin.L.LogInfo($"WASHER dropped: held {(st.GrabableItemController != null && st.GrabableItemController.IsHeld)} active {st.gameObject.activeInHierarchy}");
    }

    void WasherLog(GameWorld w, string label)
    {
        var ic = InventoryController.Instance;
        var sel = ic?.SelectedItem;
        var obj = sel != null ? new Il2CppSystem.Object(sel.Pointer) : null;
        var lances = UnityEngine.Object.FindObjectsOfType<PowerWasherLance>(true);
        foreach (var st in UnityEngine.Object.FindObjectsOfType<PowerWasherStation>(true))
            Plugin.L.LogInfo($"WASHER {label} station '{st.name}' lanceItem {st.lanceItem} managed {(st.managedWasherObject != null ? st.managedWasherObject.name + " active " + st.managedWasherObject.gameObject.activeInHierarchy : "null")} held {(st.GrabableItemController != null && st.GrabableItemController.IsHeld)} available {st.IsAvailable} backpack {st.IsBackpack}");
        Plugin.L.LogInfo($"WASHER {label} detail: category {ic?.SelectedItemList} selected {(obj != null ? obj.GetIl2CppType().Name : "null")} lances {string.Join(", ", lances.Select(l => $"{l.name} active {l.gameObject.activeInHierarchy} under player {l.transform.IsChildOf(w.Player.transform)}"))}");
        var mine = Station(false); var theirs = Station(true);
        Plugin.L.LogInfo(FormattableString.Invariant($"WASHER {label}: selected washer {LancesInHands(w)} own station {(mine != null ? P3(mine.transform.position) + " water " + mine.WaterLevel01.ToString("F2") : "none")} other's station {(theirs != null ? P3(theirs.transform.position) + " water " + theirs.WaterLevel01.ToString("F2") : "none")} refused {Dyn.LanceRefused} own {Dyn.LanceOwn} others' let through {Dyn.LanceOthers}"));
    }

    void WasherTest(float t, Session ses, GameWorld w)
    {
        bool host = ses.IsHost;
        switch (step)
        {
            case 0 when t > 3:
                {
                    var slots = PlayerInventory.Instance?.Slots;
                    string inv = slots == null ? "-" : string.Join(", ", slots.ToArray().Where(x => x?.Item != null).Select(x => $"{x.Item.Category}:{x.Item.DisplayName}"));
                    Plugin.L.LogInfo($"WASHER inventory: {inv}; stations in scene {UnityEngine.Object.FindObjectsOfType<PowerWasherStation>(true).Length}");
                }
                // The test saves own no power washer yet: both bots add the game's washer to their inventory (test
                // copies only; the saves are put back after the run). Perks stay as they are: with perk 46 (the
                // backpack washer) Interact puts the whole station on the back instead of handing out the lance.
                {
                    // The game's item list (PlayerSave.ItemList, a ScriptableObject) holds every tool's data.
                    var list = UnityEngine.Object.FindObjectOfType<PlayerSave>()?.ItemList ?? Resources.FindObjectsOfTypeAll<ItemsContainer>().FirstOrDefault();
                    // A save that bought the washer owns two tools: the Basic Station and the Basic Lance.
                    var all = list?.toolDatas?.ToArray() ?? Array.Empty<ToolData>();
                    var station = all.FirstOrDefault(d => d != null && d.Category == ItemCategory.karcher && d.Type == ItemType.karcher);
                    var lanceData = all.FirstOrDefault(d => d != null && d.Category == ItemCategory.karcher && d.Type == ItemType.lance);
                    Plugin.L.LogInfo($"WASHER washer data: {(station != null ? station.DisplayName : "none")} + {(lanceData != null ? lanceData.DisplayName : "none")}");
                    if (list?.toolDatas != null)
                        foreach (var d in list.toolDatas.ToArray())
                            if (d != null && (d.Category == ItemCategory.karcher || d.DisplayName.Contains("Lance") || d.DisplayName.Contains("Wash")))
                                Plugin.L.LogInfo($"WASHER tool {d.id} '{d.DisplayName}' {d.Category}/{d.Type} prefab '{(d.ItemPrefab != null ? d.ItemPrefab.name : "-")}' station {(d.ItemPrefab != null && d.ItemPrefab.GetComponentInChildren<PowerWasherStation>(true) != null)} perk {d.requiredPerk}");
                    foreach (var td in new[] { station, lanceData })   // the station first: Equip(karcher, 0) takes it out
                        if (td != null) { try { PlayerInventory.Instance.AddItem(td); } catch (Exception e) { Plugin.L.LogInfo("WASHER add: " + e.Message); } }
                }
                step = 10; break;
            case 10 when t > 4.5f:
                {
                    var slots = PlayerInventory.Instance?.Slots;
                    Plugin.L.LogInfo($"WASHER inventory now: {(slots == null ? "-" : string.Join(", ", slots.ToArray().Where(x => x?.Item != null).Select(x => $"{x.Item.Category}:{x.Item.DisplayName}")))}");
                }
                if (host) { try { InventoryController.Instance.Equip(ItemCategory.karcher, 0); } catch (Exception e) { Plugin.L.LogInfo("WASHER equip: " + e.Message); } }
                step = 1; break;
            case 1 when t > 6:
                if (host)
                {
                    wsStation = Station(false);
                    if (wsStation != null) SetDown(w, wsStation, 0);
                    Plugin.L.LogInfo($"WASHER host set down its station: {(wsStation != null)}");
                }
                step = 2; break;
            case 2 when t > 10:
                WasherLog(w, "before");
                if (!host)
                {
                    // Holding something, as a player usually is (with nothing selected the click only puts a lance away).
                    try { InventoryController.Instance.Equip(ItemCategory.mop, 0); } catch (Exception e) { Plugin.L.LogInfo("WASHER mop: " + e.Message); }
                }
                step = 21; break;   // both bots go through the same steps (lockstep)
            case 21 when t > 12:
                if (!host)
                {
                    var st = Station(true);
                    if (st != null) { ApproachPoint(w, st.transform.position); st.Interact(); }
                    Plugin.L.LogInfo($"WASHER client clicked the host's station: {(st != null)}");
                }
                step = 3; break;
            case 3 when t > 14:
                WasherLog(w, "after other's");
                if (!host) { try { InventoryController.Instance.Equip(ItemCategory.karcher, 0); } catch (Exception e) { Plugin.L.LogInfo("WASHER equip: " + e.Message); } }
                step = 4; break;
            case 4 when t > 17:
                if (!host)
                {
                    var own = Station(false);
                    if (own != null) { SetDown(w, own, 1.2f); }
                    Plugin.L.LogInfo($"WASHER client set down its own station: {(own != null)}");
                }
                step = 5; break;
            case 5 when t > 20:
                if (!host)
                {
                    try { InventoryController.Instance.Equip(ItemCategory.mop, 0); } catch (Exception e) { Plugin.L.LogInfo("WASHER mop: " + e.Message); }
                }
                step = 51; break;
            case 51 when t > 22:
                if (!host)
                {
                    var own = Station(false);
                    if (own != null) { ApproachPoint(w, own.transform.position); own.Interact(); }
                    Plugin.L.LogInfo("WASHER client clicked its own station");
                }
                step = 6; break;
            case 6 when t > 24:
                WasherLog(w, "after own");
                Plugin.L.LogInfo("WASHER dump done");
                step = 7; break;
        }
    }
}
