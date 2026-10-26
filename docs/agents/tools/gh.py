import json, pathlib, subprocess, sys, urllib.request

# Drives GitHub for jassifx/arzoiemail through its REST API, with the token Git Credential Manager
# holds for github.com (nothing is stored). See docs/agents/environment.md.
#
#   gh.py checks <sha>                       check results of a commit (full SHA)
#   gh.py wait <sha>                         wait until every check has finished, then print them
#   gh.py log <sha> "<check name>" <file>    download the newest log of a job
#   gh.py pr "<title>" <branch> <body.md>    open a pull request on master
#   gh.py merge <number> <branch>            merge a pull request, then delete its branch
REPO = "https://api.github.com/repos/jassifx/arzoiemail"


def token():
    out = subprocess.run(["git", "credential", "fill"], input="protocol=https\nhost=github.com\n\n",
                         capture_output=True, text=True).stdout
    return next(l.split("=", 1)[1] for l in out.splitlines() if l.startswith("password="))


TOKEN = token()


def call(method, url, body=None):
    req = urllib.request.Request(url, method=method, data=json.dumps(body).encode() if body is not None else None,
                                 headers={"Authorization": f"Bearer {TOKEN}", "Accept": "application/vnd.github+json"})
    try:
        with urllib.request.urlopen(req) as r:
            data = r.read()
            return r.status, json.loads(data) if data else {}
    except urllib.error.HTTPError as e:
        return e.code, json.loads(e.read() or b"{}")


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *a, **k):
        return None


def redirected(url):
    opener = urllib.request.build_opener(NoRedirect)
    req = urllib.request.Request(url, headers={"Authorization": f"Bearer {TOKEN}"})
    try:
        opener.open(req)
    except urllib.error.HTTPError as e:
        location = e.headers["Location"]
    with urllib.request.urlopen(location) as r:
        return r.read()


def checks(sha):
    return call("GET", f"{REPO}/commits/{sha}/check-runs?per_page=100")[1]["check_runs"]


cmd = sys.argv[1]
if cmd == "checks":
    for r in sorted(checks(sys.argv[2]), key=lambda r: r["name"]):
        print(f"   {r['conclusion'] or r['status']:8} {r['name']}")
elif cmd == "log":
    sha, name, out = sys.argv[2], sys.argv[3], sys.argv[4]
    run = max((r for r in checks(sha) if r["name"] == name), key=lambda r: r["id"])
    pathlib.Path(out).write_bytes(redirected(f"{REPO}/actions/jobs/{run['id']}/logs"))
    print("saved", out)
elif cmd == "merge":
    number, branch = sys.argv[2], sys.argv[3]
    print("merge:", call("PUT", f"{REPO}/pulls/{number}/merge", {"merge_method": "merge"}))
    print("delete branch:", call("DELETE", f"{REPO}/git/refs/heads/{branch}")[0])
elif cmd == "wait":
    import time
    sha, deadline = sys.argv[2], time.time() + 3 * 3600
    while time.time() < deadline:
        runs = checks(sha)
        if runs and all(r["status"] == "completed" for r in runs):
            break
        time.sleep(90)
    for r in sorted(checks(sha), key=lambda r: r["name"]):
        print(f"   {r['conclusion'] or r['status']:8} {r['name']}")
elif cmd == "pr":
    title, head, body = sys.argv[2], sys.argv[3], pathlib.Path(sys.argv[4]).read_text(encoding="utf-8")
    status, d = call("POST", f"{REPO}/pulls", {"title": title, "head": head, "base": "master", "body": body})
    print(status, d.get("number"), d.get("html_url"))
