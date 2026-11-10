"""Momentum. (@trade.momentum) shorts collector (IP-T493).

Lists the channel's shorts newest-first, keeps the ones whose title starts with TOP/Top or carries a
date (11/9/26, 11/9/2026, MONDAY 11/9/26), and for each one saves its metadata and YouTube caption
transcript as JSON under <out>/transcripts/<trade-date>/<video-id>.json. Resumable: a video already
on disk is skipped. Paced and backs off on HTTP 429.

youtubetotranscript.com sits behind a Cloudflare bot check, so the captions are read straight from
YouTube (the same source that site shows) with youtube-transcript-api; yt-dlp supplies the list and
the upload timestamp.

usage: python tools/momentum/fetch_momentum.py [--out D:/IdiotProofData/momentum] [--limit N]
"""
import argparse, datetime as dt, json, os, re, subprocess, sys, time

CHANNEL = "https://www.youtube.com/@trade.momentum/shorts"
TITLE_KEEP = re.compile(r"^\s*top\b|\b\d{1,2}/\d{1,2}/\d{2,4}\b", re.IGNORECASE)
TITLE_DATE = re.compile(r"\b(\d{1,2})/(\d{1,2})/(\d{2,4})\b")


def ytdlp(*args):
    cmd = [sys.executable, "-m", "yt_dlp", "--js-runtimes", "node", *args]
    return subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")


def list_shorts():
    r = ytdlp("--flat-playlist", "--print", "%(id)s\t%(title)s", CHANNEL)
    rows = []
    for line in r.stdout.splitlines():
        if "\t" in line:
            vid, title = line.split("\t", 1)
            rows.append((vid.strip(), title.strip()))
    return rows  # channel order = newest first


def title_date(title):
    m = TITLE_DATE.search(title)
    if not m:
        return None
    mo, d, y = int(m.group(1)), int(m.group(2)), int(m.group(3))
    if y < 100:
        y += 2000
    try:
        return dt.date(y, mo, d)
    except ValueError:
        return None


def upload_info(vid):
    r = ytdlp("--skip-download", "--print", "%(timestamp)s\t%(upload_date)s\t%(duration)s",
              f"https://www.youtube.com/shorts/{vid}")
    for line in r.stdout.splitlines():
        parts = line.split("\t")
        if len(parts) == 3:
            ts = int(parts[0]) if parts[0].isdigit() else None
            return ts, parts[1], int(parts[2]) if parts[2].isdigit() else None
    return None, None, None


def transcript(vid, api):
    for attempt in range(6):
        try:
            t = api.fetch(vid, languages=["en", "en-US"])
            return [{"start": s.start, "dur": s.duration, "text": s.text} for s in t]
        except Exception as e:  # noqa: BLE001 - library raises many types
            msg = str(e)
            if ("429" in msg or "Too Many" in msg or "blocked" in msg.lower()
                    or "ConnectionError" in type(e).__name__ or "Connection aborted" in msg or "RemoteDisconnected" in msg):
                time.sleep(120 * (attempt + 1))  # YouTube's caption limiter needs minutes, not seconds
                continue
            return {"error": type(e).__name__ + ": " + msg[:300]}
    return {"error": "rate-limited after retries"}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="D:/IdiotProofData/momentum")
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--pace", type=float, default=120.0, help="seconds between videos")
    a = ap.parse_args()
    from youtube_transcript_api import YouTubeTranscriptApi
    api = YouTubeTranscriptApi()

    os.makedirs(a.out, exist_ok=True)
    rows = list_shorts()
    kept = [(v, t) for v, t in rows if TITLE_KEEP.search(t)]
    with open(os.path.join(a.out, "channel-list.json"), "w", encoding="utf-8") as f:
        json.dump({"fetchedUtc": dt.datetime.utcnow().isoformat() + "Z", "all": len(rows), "kept": len(kept),
                   "videos": [{"id": v, "title": t, "kept": bool(TITLE_KEEP.search(t))} for v, t in rows]}, f, indent=1)
    print(f"channel shorts {len(rows)}, kept {len(kept)}", flush=True)

    # done = a saved transcript with text; a saved failure is retried (and overwritten) on the next run
    seen = set()
    for root, _, files in os.walk(os.path.join(a.out, "transcripts")):
        for n in files:
            try:
                with open(os.path.join(root, n), encoding="utf-8") as f:
                    if json.load(f).get("text"):
                        seen.add(os.path.splitext(n)[0])
                    else:
                        os.remove(os.path.join(root, n))
            except (OSError, ValueError):
                pass
    done = 0
    for i, (vid, title) in enumerate(kept):
        if a.limit and done >= a.limit:
            break
        if vid in seen:
            continue
        ts, upload_date, duration = upload_info(vid)
        published = dt.datetime.fromtimestamp(ts, dt.timezone.utc) if ts else None
        td = title_date(title)
        if td is None and published is not None:
            td = published.date()
        day = td.isoformat() if td else "unknown"
        tr = transcript(vid, api)
        rec = {
            "videoId": vid, "url": f"https://www.youtube.com/shorts/{vid}", "title": title,
            "publishedUtc": published.isoformat() if published else None, "uploadDate": upload_date,
            "durationSec": duration, "titleDate": td.isoformat() if td else None, "tradeDate": day,
            "channelIndex": i, "fetchedUtc": dt.datetime.utcnow().isoformat() + "Z",
            "segments": tr if isinstance(tr, list) else None,
            "text": " ".join(s["text"] for s in tr) if isinstance(tr, list) else None,
            "error": tr.get("error") if isinstance(tr, dict) else None,
        }
        d = os.path.join(a.out, "transcripts", day)
        os.makedirs(d, exist_ok=True)
        with open(os.path.join(d, vid + ".json"), "w", encoding="utf-8") as f:
            json.dump(rec, f, ensure_ascii=False, indent=1)
        done += 1
        print(f"[{done}] {day} {vid} {'ok' if rec['text'] else rec['error']} :: {title[:70]}", flush=True)
        if rec["error"] and "rate-limited" in rec["error"]:
            print("rate-limited/blocked: cooling down 30 minutes", flush=True)
            time.sleep(1800)
        time.sleep(a.pace)
    print(f"done {done}", flush=True)


if __name__ == "__main__":
    main()
