import base64, json, sys, urllib.request, urllib.error

ORG = "AzureDevOpsDFW"
PROJECT = "Heartbeat"

with open(".env") as f:
    for line in f:
        if line.startswith("ADO_PAT="):
            PAT = line.split("=", 1)[1].strip()
            break

AUTH = base64.b64encode(f":{PAT}".encode()).decode()
BASE = f"https://dev.azure.com/{ORG}"
HEADERS = {
    "Authorization": f"Basic {AUTH}",
    "Accept": "application/json"
}

def api(method, path, body=None, extra_headers=None):
    url = f"{BASE}{path}"
    data = json.dumps(body).encode() if body else None
    h = dict(HEADERS)
    if extra_headers:
        h.update(extra_headers)
    req = urllib.request.Request(url, data=data, headers=h, method=method)
    try:
        with urllib.request.urlopen(req) as r:
            return r.status, json.load(r)
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read())
        except Exception:
            return e.code, {"raw": e.read().decode()}

# Detect a unique incident number that won't collide with heartbeatused INCs
used_incs = []
wiql_all = {"query": "SELECT [System.Id], [Custom.ServiceNowIncidentNumber] FROM WorkItems WHERE [Custom.ServiceNowIncidentNumber] <> ''"}
code, body = api("POST", f"/{PROJECT}/_apis/wit/wiql?api-version=7.1", wiql_all)
if code == 200:
    for wi in body.get("workItems", []):
        wid = wi.get("id")
        val = wi.get("fields", {}).get("Custom.ServiceNowIncidentNumber")
        if wid and val:
            used_incs.append(str(val))

# generate a fresh INC that won't collide
import datetime
fresh_inc = f"INC{datetime.datetime.utcnow().strftime('%Y%m%d%H%M%S')}"
print(f"Generated fresh incident: {fresh_inc}")
print(f"Existing INCs in scope: {len(used_incs)}")

TITLE = "VPN connectivity issue (live-test)"
DESC = "Automated e2e test — ServiceNow to ADO function validation."

# Duplicate check
wiql = {"query": f"SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = '{PROJECT}' AND [Custom.ServiceNowIncidentNumber] = '{fresh_inc}'"}
code, body = api("POST", f"/{PROJECT}/_apis/wit/wiql?api-version=7.1", wiql)
matches = body.get("workItems", [])
print(f"Duplicate check: {len(matches)} match(es)")
if matches:
    print(f"Unexpected duplicate: {matches[0]}")
    sys.exit(1)

# Create
incident_url = f"https://<snow-instance>/nav_to.do?uri=incident_do?sys_id=...&sysparm_query=number={fresh_inc}"
full_desc = f"[{fresh_inc}]({incident_url})\n\n{DESC}"
ops = [
    {"op": "add", "path": "/fields/System.Title", "value": TITLE},
    {"op": "add", "path": "/fields/System.Description", "value": full_desc},
    {"op": "add", "path": "/fields/Custom.ServiceNowIncidentNumber", "value": fresh_inc},
]
create_headers = dict(HEADERS)
create_headers["Content-Type"] = "application/json-patch+json"
url = f"{BASE}/{PROJECT}/_apis/wit/workitems/%24User%20Story?api-version=7.1"
req = urllib.request.Request(url, data=json.dumps(ops).encode(), headers=create_headers, method="POST")
try:
    with urllib.request.urlopen(req) as r:
        created = json.load(r)
        wi_id = created["id"]
        wi_url = created.get("_links", {}).get("html", {}).get("href", "N/A")
        print(f"Created US: id={wi_id} url={wi_url}")
except urllib.error.HTTPError as e:
    print(f"CREATE FAILED: HTTP {e.code}")
    print(e.read().decode())
    sys.exit(1)

# Verify via WIQL
code2, body2 = api("POST", f"/{PROJECT}/_apis/wit/wiql?api-version=7.1", wiql)
matches2 = body2.get("workItems", [])
print(f"Post-create WIQL: {len(matches2)} match(es)")
found = [w for w in matches2 if str(w.get("id")) == str(wi_id)]
if found:
    print(f"✓ VERIFIED: INC={fresh_inc}  US={wi_id}  project={PROJECT}")
else:
    print("✗ VERIFY FAILED")
    sys.exit(1)
