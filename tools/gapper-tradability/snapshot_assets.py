"""Daily point-in-time record of Alpaca's us_equity asset list (IP-T496).

Alpaca's /v2/assets reports only today's tradable / shortable / easy_to_borrow flags and keeps no history, so a
gapper's tradability on its own day can only be known if the list was saved that morning. This saves the active and
inactive lists, gzipped, to D:/IdiotProofData/alpaca-assets/assets-<status>-<yyyymmdd>-<hhmm>ET.json.gz through the
idiotproof CLI (live account, read-only). Scheduled before 04:00 ET by register_snapshot_task.ps1.

    python -I tools/gapper-tradability/snapshot_assets.py [--out DIR]
"""
import argparse, datetime as dt, gzip, json, os, subprocess, sys
from zoneinfo import ZoneInfo


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="D:/IdiotProofData/alpaca-assets")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    stamp = dt.datetime.now(ZoneInfo("America/New_York")).strftime("%Y%m%d-%H%M") + "ET"
    env = dict(os.environ, MSYS_NO_PATHCONV="1")
    for status in ("active", "inactive"):
        r = subprocess.run(["idiotproof", "alpaca", "raw", "GET", f"/v2/assets?asset_class=us_equity&status={status}", "--mode", "live"],
                           capture_output=True, text=True, encoding="utf-8", env=env)
        body = r.stdout[r.stdout.find("["):] if "[" in r.stdout else ""
        assets = json.loads(body) if body else []
        if not assets:
            print(f"{status}: no assets returned (exit {r.returncode}): {r.stderr[:300]}", file=sys.stderr)
            sys.exit(1)
        path = os.path.join(a.out, f"assets-{status}-{stamp}.json.gz")
        with gzip.open(path, "wt", encoding="utf-8") as f:
            json.dump(assets, f)
        blocked = sum(1 for x in assets if x.get("status") == "active" and not x.get("tradable") and x.get("exchange") != "OTC")
        print(f"{status}: {len(assets)} assets -> {path} (listed but not tradable: {blocked})")


if __name__ == "__main__":
    main()
