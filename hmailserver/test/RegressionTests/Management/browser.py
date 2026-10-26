"""Browser interaction tests with explicit network fixtures; native/API tests run separately."""
import json
import pathlib
import sys
from playwright.sync_api import sync_playwright

base = sys.argv[1].rstrip('/')
output = pathlib.Path(sys.argv[2])
output.mkdir(parents=True, exist_ok=True)
providers = [{"id": 1, "Active": True, "DNSHost": "dnsbl.example.net", "ExpectedResult": "127.0.0.*", "RejectMessage": "Blocked", "Score": 3}]
user = {"address": "Administrator", "level": 2, "domainId": 0, "accountId": 0}
settings = {"SpamAssassinEnabled": True, "SpamAssassinHost": "127.0.0.1", "SpamAssassinPort": 783, "SpamAssassinMergeScore": True, "SpamAssassinScore": 5, "UseSPF": True, "UseSPFScore": 3, "DMARCEnabled": True, "SpamMarkThreshold": 5, "SpamDeleteThreshold": 20}
requests = []


def api(route):
    global user
    request = route.request
    path = request.url.split('/api/v1', 1)[1]
    body = json.loads(request.post_data or '{}')
    requests.append((request.method, path, body))
    data = {}
    if path == '/session':
        if request.method == 'DELETE':
            user = None
        data = {"user": user, "csrf": "browser-fixture-token"}
    elif path == '/status':
        data = {"version": "5.7.2", "state": 3, "domains": 2}
    elif path == '/dnsbl':
        if request.method == 'POST':
            providers.append({"id": len(providers) + 1, **body})
            data = providers[-1]
        else:
            data = {"items": providers}
    elif path.startswith('/dnsbl/'):
        item = next(v for v in providers if v['id'] == int(path.rsplit('/', 1)[1]))
        if request.method == 'DELETE':
            providers.remove(item)
            data = {"deleted": True}
        else:
            item.update(body)
            data = item
    elif path == '/antispam':
        settings.update(body)
        data = settings
    elif path == '/domains':
        data = {"items": [{"id": 5, "name": "example.com", "active": True, "dkimEnabled": True, "selector": "arzoie"}]}
    elif path.endswith('/dns'):
        record = {"name": "example.com", "type": "TXT", "published": "v=spf1 include:provider.example -all", "proposed": "v=spf1 include:provider.example ip4:203.0.113.10 -all", "status": "valid", "errors": [], "warnings": []}
        data = {"domain": "example.com", "spf": record,
                "dkim": {**record, "name": "arzoie._domainkey.example.com", "published": ["v=DKIM1; k=rsa; p=[public key]"], "proposed": None},
                "dmarc": {**record, "name": "_dmarc.example.com", "published": ["v=DMARC1; p=none; adkim=r; aspf=r"], "proposed": None},
                "notice": "DNS inspection only; message alignment needs testing."}
    elif path == '/diagnostics':
        data = {"checks": [{"safe": True, "message": "Native management channel connected; no COM dependency."}]}
    elif path == '/spamassassin/test':
        data = {"connected": True}
    else:
        raise AssertionError(f"Unexpected browser API call: {request.method} {path}")
    route.fulfill(status=200, content_type='application/json', body=json.dumps({"data": data}))


with sync_playwright() as p:
    browser = p.chromium.launch(channel='msedge', headless=True)
    page = browser.new_page(viewport={"width": 1440, "height": 1000})
    errors = []
    page.on('pageerror', lambda error: errors.append(str(error)))
    page.route('**/api/v1/**', api)
    page.goto(base)
    page.get_by_text('Running', exact=True).wait_for()
    page.screenshot(path=str(output / 'overview-desktop.png'), full_page=True)
    page.get_by_role('link', name='DNS blocklists').click()
    page.get_by_text('dnsbl.example.net', exact=True).wait_for()
    page.get_by_role('button', name='Disable', exact=True).click()
    page.get_by_role('button', name='Enable', exact=True).wait_for()
    page.get_by_role('button', name='Add provider').click()
    page.get_by_label('DNSHost').fill('second.example.net')
    page.get_by_role('button', name='Save changes').click()
    page.get_by_text('second.example.net', exact=True).wait_for()
    assert providers[-1]['Active'] is False, 'New providers must start disabled.'
    page.screenshot(path=str(output / 'dnsbl-desktop.png'), full_page=True)
    page.get_by_role('link', name='Spam protection').click()
    page.get_by_label('Spam Assassin Host', exact=True).wait_for()
    page.get_by_role('button', name='Test saved connection').click()
    page.get_by_text('SpamAssassin connection succeeded.', exact=True).wait_for()
    page.get_by_role('link', name='Domains & DNS').click()
    page.get_by_role('button', name='Inspect DNS').click()
    page.get_by_label('Sending IP addresses (comma separated)').fill('203.0.113.10')
    page.get_by_role('button', name='Validate published DNS & build recommendations').click()
    page.get_by_text('DNS inspection only; message alignment needs testing.', exact=True).wait_for()
    assert requests[-1][2]['addresses'] == ['203.0.113.10']
    page.screenshot(path=str(output / 'dns-desktop.png'), full_page=True)
    page.set_viewport_size({"width": 390, "height": 844})
    page.screenshot(path=str(output / 'dns-mobile.png'), full_page=True)
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Mobile page overflows horizontally.'
    page.get_by_role('button', name='Sign out').click()
    page.get_by_role('heading', name='Sign in to your server').wait_for()
    page.screenshot(path=str(output / 'login-mobile.png'), full_page=True)
    assert not errors, errors
    browser.close()
print('Passed browser navigation, DNSBL mutation, SpamAssassin test, DNS guidance, logout and mobile layout checks.')
