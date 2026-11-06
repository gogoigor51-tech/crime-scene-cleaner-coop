"""Checks the mails suite (tools/run-pair.ps1 -Test mails -Mission 11): mails the host deletes are deleted on the
client too, and the "delete the mails" counts match."""
import re
import sys


def state(log, label):
    m = re.findall(r"MAILS " + label + r":(.*)", log)
    return m[-1].strip() if m else None


def counts(s):
    return re.findall(r"quest (\S+) (\d+)/(\d+) deleted (\w+);", s or "")


def main(_h, _c, host_log, client_log):
    try:
        h = open(host_log, encoding="utf-8", errors="replace").read()
        c = open(client_log, encoding="utf-8", errors="replace").read()
    except FileNotFoundError as e:
        print(f"FAIL  missing log: {e.filename}\nRESULT: FAIL")
        return 1
    if "MAILS dump done" not in h or "MAILS dump done" not in c:
        print("FAIL  dumps missing\nRESULT: FAIL")
        return 1
    hb, ha, cb, ca = state(h, "before"), state(h, "after"), state(c, "before"), state(c, "after")
    print(f"      host   {counts(hb)} -> {counts(ha)}\n      client {counts(cb)} -> {counts(ca)}")
    deleted = len(re.findall(r"MAILS host deleted", h))
    checks = [
        (hb and hb == cb and counts(hb), "mails and their quests the same on both before"),
        (deleted >= 1, f"the host deleted {deleted} mail(s)"),
        (ha != hb, "the host's mails and count changed"),
        (ca == ha, "the client's mails and count are the same as the host's"),
    ]
    for ok, what in checks:
        print(f"{'PASS' if ok else 'FAIL'}  {what}")
    ok = all(bool(x) for x, _ in checks)
    print(f"RESULT: {'PASS' if ok else 'FAIL'} ({sum(1 for x, _ in checks if x)} of {len(checks)} checks)")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:5]))
