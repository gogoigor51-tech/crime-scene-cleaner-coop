"""Checks the report suite (tools/run-pair.ps1 -Test report): after the host leaves the job, each machine shows its
job report once (owner's test: the score animations played twice, about a second apart)."""
import re
import sys

ONCE = [
    "MissionLeaveController.LeaveMissionWithReport",
    "MissionReportDataController.Display",
    "MissionReportFinalView.Setup",
    "JobDetailsPage.AnimateSuccessStatusLabel",
]


def totals(log):
    return {k: int(v) for k, v in re.findall(r"REPORT total (\S+) (\d+)", log)}


def main(_h, _c, host_log, client_log):
    try:
        h = open(host_log, encoding="utf-8", errors="replace").read()
        c = open(client_log, encoding="utf-8", errors="replace").read()
    except FileNotFoundError as e:
        print(f"FAIL  missing log: {e.filename}\nRESULT: FAIL")
        return 1
    if "REPORT dump done" not in h or "REPORT dump done" not in c:
        print("FAIL  dumps missing\nRESULT: FAIL")
        return 1
    th, tc = totals(h), totals(c)
    print(f"      host   {th}\n      client {tc}")
    checks = []
    for who, t in (("host", th), ("client", tc)):
        for k in ONCE:
            checks.append((t.get(k, 0) == 1, f"{who}: {k} once ({t.get(k, 0)})"))
    for ok, what in checks:
        print(f"{'PASS' if ok else 'FAIL'}  {what}")
    ok = all(x for x, _ in checks)
    print(f"RESULT: {'PASS' if ok else 'FAIL'} ({sum(1 for x, _ in checks if x)} of {len(checks)} checks)")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:5]))
