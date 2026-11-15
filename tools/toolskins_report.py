"""Checks the toolskins suite (tools/run-pair.ps1 -Test toolskins -QuitAfter 95) from both bots' logs: each player's
bucket and trash bag wear that player's skin on the other's screen (not the viewer's own choice), each own bucket keeps
its own, and the host sees the client hold the akimbo sponge in the client's boxing gloves, one in each hand."""
import re
import sys


def skins(text):
    """'bucket=9 garbageBag=-1' -> {'bucket': 9, ...}"""
    return {k: int(v) for k, v in re.findall(r"(\w+)=(-?\d+)", text or "")}


def state(log, who):
    m = re.findall(r"TOOLSKINS " + who + r" mine: bucket \[([^\]]*)\] bag \[([^\]]*)\]; other's: bucket '([^']*)' \[([^\]]*)\] bag \[([^\]]*)\]", log)
    if not m:
        return None
    mb, mg, oname, ob, og = m[-1]
    return {"mine_bucket": skins(mb), "mine_bag": skins(mg), "other_name": oname, "other_bucket": skins(ob), "other_bag": skins(og)}


def main(_h, _c, host_log, client_log):
    try:
        h = open(host_log, encoding="utf-8", errors="replace").read()
        c = open(client_log, encoding="utf-8", errors="replace").read()
    except FileNotFoundError as e:
        print(f"FAIL  missing log: {e.filename}\nRESULT: FAIL")
        return 1
    if "TOOLSKINS dump done" not in h or "TOOLSKINS dump done" not in c:
        print("FAIL  dumps missing\nRESULT: FAIL")
        return 1
    hs, cs = state(h, "host"), state(c, "client")
    hold = re.findall(r"TOOLSKINS host sees the client hold '([^']*)' skin (-?\d+) renderers (\d+) left hand (\w+)", h)
    checks = []
    checks.append((hs and hs["other_bucket"].get("bucket") == 9, f"the host sees the client's bucket in the client's skin 9 ({hs and hs['other_bucket']})"))
    checks.append((cs and cs["other_bucket"].get("bucket") == 14, f"the client sees the host's bucket in the host's skin 14 ({cs and cs['other_bucket']})"))
    checks.append((hs and cs and hs["mine_bucket"].get("bucket") == 14 and cs["mine_bucket"].get("bucket") == 9,
                   f"each own bucket keeps its own skin (host {hs and hs['mine_bucket']}, client {cs and cs['mine_bucket']})"))
    checks.append((hs and hs["other_bag"].get("garbageBag") == 29, f"the host sees the client's trash bag in the client's skin 29 ({hs and hs['other_bag']})"))
    checks.append((cs and cs["other_bag"].get("garbageBag") == -1 and cs["mine_bag"].get("garbageBag") == 29,
                   f"the client sees the host's bag without a skin, its own in 29 (other {cs and cs['other_bag']}, own {cs and cs['mine_bag']})"))
    ok_hold = bool(hold) and hold[0][0] == "DualSponge" and hold[0][1] == "7" and hold[0][3] == "True"
    checks.append((ok_hold, f"the host sees the client hold the akimbo sponge in boxing gloves, one in the left hand ({hold[0] if hold else 'not logged'})"))
    for when in ("lamp off", "lamp on"):
        m = re.findall(r"TOOLSKINS " + when + r": other's 'WorkLamp \(other player\)' skin (-?\d+) inst '[^']*' lamp on (\w+) drawn: (.*)", h)
        drawn = m[-1][2] if m else ""
        ok = bool(m) and m[-1][0] == "10" and "Furnace" in drawn and "Bogato" not in drawn
        checks.append((ok, f"{when}: the host sees the client's lamp in the client's skin only ({drawn[:160] if m else 'not logged'})"))
    pist = re.findall(r"TOOLSKINS host sees the client hold '([^']*)' skin (-?\d+) renderers (\d+) drawn (\d+) left hand (\w+) \(pistols\)", h)
    checks.append((bool(pist) and pist[-1][1] == "105" and int(pist[-1][3]) >= 2 and pist[-1][4] == "True",
                   f"the host sees the client hold the water pistols, drawn while not cleaning ({pist[-1] if pist else 'not logged'})"))
    for ok, what in checks:
        print(f"{'PASS' if ok else 'FAIL'}  {what}")
    ok = all(x for x, _ in checks)
    print(f"RESULT: {'PASS' if ok else 'FAIL'} ({sum(1 for x, _ in checks if x)} of {len(checks)} checks)")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:5]))
