using System;
using System.Linq;
using CoopCore;
using UnityEngine;

namespace CscCoop;

// arcade suite (tools/run-pair.ps1 -Test arcade -Mission 11, Developers' Hell): the host uses the Super Stacker
// machine the way a click does (its Interactable.Interact -> StackMiniGamePresenter.EnterTheGame) and plays a little.
// The minigame must open only on the host, and the two must stay in the same job all along (owner's test: it opened
// for everyone, the others vanished, "in another place", nothing synced): a door the client clicks while the host
// plays and one the host clicks after closing it must reach the other side.
public partial class Bot
{
    StackMiniGamePresenter arGame;
    Interactable arButton;
    int[] arDoors = Array.Empty<int>();

    void ArcadeLog(Session ses, GameWorld w, string label)
    {
        bool together = ses.Players.Values.Any(rp => rp.SceneKey != 0 && rp.SceneKey == w.SceneKey);
        Plugin.L.LogInfo($"ARCADE {label}: key {w.SceneKey:X8} together {together} player active {(w.Player != null && w.Player.gameObject.activeInHierarchy)} game open {(arGame != null && arGame.gameCamera != null && arGame.gameCamera.gameObject.activeInHierarchy)} {DoorStates()}");
    }

    void ArcadeTest(float t, Session ses, GameWorld w)
    {
        bool host = ses.IsHost;
        switch (step)
        {
            case 0 when t > 3:
                arGame = UnityEngine.Object.FindObjectOfType<StackMiniGamePresenter>();
                arButton = arGame != null ? arGame.GetComponent<Interactable>() : null;
                arDoors = Enumerable.Range(0, Doors.Shortcuts.Count).Where(i => Doors.Shortcuts[i] != null && Doors.Shortcuts[i].gameObject.activeInHierarchy && !Doors.Shortcuts[i].IsLocked && Doors.Shortcuts[i].RequiredKeys.Count == 0).Take(2).ToArray();
                Plugin.L.LogInfo($"ARCADE machine {(arGame != null)} button {(arButton != null)} doors {string.Join(",", arDoors)}");
                ArcadeLog(ses, w, "before");
                step = 1; break;
            case 1 when t > 5:
                if (host && arButton != null) { Plugin.L.LogInfo("ARCADE host uses the machine"); arButton.Interact(); }
                step = 2; break;
            case 2 when t > 9:
                if (host && arGame?.manager != null) { arGame.manager.StartGame(); }
                ArcadeLog(ses, w, "open");
                step = 3; break;
            case 3 when t > 11:
                if (!host && arDoors.Length > 0) Click(arDoors[0], "client");   // while the host plays
                if (host && arGame?.manager != null) { arGame.manager.PlaceCube(); arGame.manager.PlaceCube(); }
                step = 4; break;
            case 4 when t > 15:
                ArcadeLog(ses, w, "playing");
                if (host && arGame != null) { Plugin.L.LogInfo("ARCADE host leaves the machine"); arGame.Close(); }
                step = 5; break;
            case 5 when t > 19:
                if (host && arDoors.Length > 1) Click(arDoors[1], "host");
                step = 6; break;
            case 6 when t > 25:
                ArcadeLog(ses, w, "after");
                Plugin.L.LogInfo("ARCADE dump done");
                step = 7; break;
        }
    }
}
