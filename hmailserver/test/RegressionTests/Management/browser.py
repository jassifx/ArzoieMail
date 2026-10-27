"""Browser interaction tests with explicit network fixtures; native/API tests run separately."""
import json
import pathlib
import sys
from urllib.parse import urlsplit, parse_qs
from playwright.sync_api import sync_playwright

base = sys.argv[1].rstrip('/')
output = pathlib.Path(sys.argv[2])
output.mkdir(parents=True, exist_ok=True)
providers = [{"id": 1, "Active": True, "DNSHost": "dnsbl.example.net", "ExpectedResult": "127.0.0.*", "RejectMessage": "Blocked", "Score": 3}]
user = {"address": "Administrator", "level": 2, "domainId": 0, "accountId": 0}
settings = {"SpamAssassinEnabled": True, "SpamAssassinHost": "127.0.0.1", "SpamAssassinPort": 783, "SpamAssassinMergeScore": True, "SpamAssassinScore": 5, "UseSPF": True, "UseSPFScore": 3, "DMARCEnabled": True, "SpamMarkThreshold": 5, "SpamDeleteThreshold": 20}
requests = []
domains = [{"id": 5, "name": "example.com", "active": True, "dkimEnabled": True, "selector": "arzoie", "maxSizeMB": 0, "maxAccountSizeMB": 0, "maxMessageSizeKB": 0}]
mailboxes = []
aliases = []
lists = []
list_members = {}
behavior = {"forwardEnabled": False, "forwardAddress": "", "forwardKeepOriginal": True, "forwardAbortSpamFlagged": True,
            "autoReplyEnabled": False, "autoReplySubject": "", "autoReplyBody": "", "autoReplyExpires": False,
            "autoReplyExpiresDate": "", "autoReplyAbortSpamFlagged": True}
revoked = False


def api(route):
    global user
    request = route.request
    path = request.url.split('/api/v1', 1)[1]
    query = parse_qs(urlsplit(path).query)
    path = urlsplit(path).path
    body = json.loads(request.post_data or '{}')
    requests.append((request.method, path, body))
    if revoked and path == '/domains':
        user = None
        route.fulfill(status=401, body='')  # Cookie challenge has no JSON error envelope.
        return
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
        if request.method == 'POST':
            data = {"id": 6, "dkimEnabled": False, "selector": "", **body}
            domains.append(data)
        else:
            items = [v for v in domains if query.get('search', [''])[0].lower() in v['name'].lower()]
            offset = int(query.get('offset', [0])[0])
            data = {"items": items[offset:offset + 50], "total": len(items)}
    elif path.endswith('/behavior'):
        behavior.update(body)
        data = behavior
    elif '/accounts' in path:
        if path.endswith('/accounts'):
            if request.method == 'POST':
                data = {"id": 10, "domainId": 5, **{k: v for k, v in body.items() if k != 'password'}}
                mailboxes.append(data)
            else:
                data = {"items": mailboxes, "total": len(mailboxes)}
        else:
            item = next(v for v in mailboxes if v['id'] == int(path.rsplit('/', 1)[1]))
            if request.method == 'DELETE':
                mailboxes.remove(item)
                data = {"deleted": True}
            else:
                item.update({k: v for k, v in body.items() if k != 'password'})
                data = item
    elif '/aliases' in path or '/lists' in path:
        parts = path.strip('/').split('/')
        members = len(parts) >= 5 and parts[4] == 'members'
        collection = list_members.setdefault(int(parts[3]), []) if members else aliases if parts[2] == 'aliases' else lists
        collection_length = 5 if members else 3
        if len(parts) == collection_length:
            if request.method == 'POST':
                defaults = {} if members else {"active": False} if parts[2] == 'aliases' else {"active": False, "mode": 1, "requireAuth": True, "requireAddress": ""}
                data = {"id": max([v['id'] for v in collection], default=20) + 1, **defaults, **body}
                data['listId' if members else 'domainId'] = int(parts[3] if members else parts[1])
                collection.append(data)
            else:
                items = [v for v in collection if query.get('search', [''])[0].lower() in v['address'].lower()]
                offset = int(query.get('offset', [0])[0])
                data = {"items": items[offset:offset + 50], "total": len(items)}
        else:
            item = next(v for v in collection if v['id'] == int(parts[-1]))
            if request.method == 'DELETE':
                collection.remove(item)
                if not members and parts[2] == 'lists':
                    list_members.pop(item['id'], None)
                data = {"deleted": True}
            else:
                item.update(body)
                data = item
    elif path.startswith('/domains/') and not path.endswith('/dns'):
        item = next(v for v in domains if v['id'] == int(path.rsplit('/', 1)[1]))
        if request.method == 'DELETE':
            domains.remove(item)
            data = {"deleted": True}
        else:
            item.update(body)
            data = item
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
    page.get_by_role('button', name='New domain', exact=True).click()
    page.get_by_label('name', exact=True).fill('second.test')
    page.get_by_role('button', name='Save changes', exact=True).click()
    page.get_by_text('second.test', exact=True).wait_for()
    page.get_by_role('button', name='Edit domain', exact=True).first.click()
    page.get_by_label('max Account Size MB', exact=True).fill('100')
    page.get_by_role('button', name='Save changes', exact=True).click()
    page.get_by_text('Domain saved.', exact=True).wait_for()
    assert domains[0]['maxAccountSizeMB'] == 100
    page.get_by_role('button', name='Mailboxes', exact=True).first.click()
    page.get_by_role('button', name='New mailbox', exact=True).click()
    page.get_by_label('address', exact=True).fill('new@example.com')
    page.get_by_label('password', exact=True).fill('browser-fixture-only')
    assert page.get_by_label('password', exact=True).get_attribute('type') == 'password'
    page.get_by_role('button', name='Save changes', exact=True).click()
    page.get_by_text('new@example.com', exact=True).wait_for()
    page.get_by_role('button', name='Edit mailbox', exact=True).click()
    page.get_by_label('max Size MB', exact=True).fill('50')
    page.get_by_role('button', name='Save changes', exact=True).click()
    page.get_by_text('Mailbox saved.', exact=True).wait_for()
    assert mailboxes[0]['maxSizeMB'] == 50
    assert 'password' not in requests[-2][2], 'Blank edit password must not replace existing credentials.'
    page.get_by_role('button', name='Forwarding and replies', exact=True).click()
    page.get_by_label('forward Address', exact=True).fill('archive@example.com')
    page.get_by_label('forward Enabled', exact=True).check()
    page.get_by_label('auto Reply Enabled', exact=True).check()
    page.get_by_label('auto Reply Body', exact=True).fill('Away\nContact the team. <script>literal text</script>')
    page.get_by_label('auto Reply Expires', exact=True).check()
    page.get_by_label('auto Reply Expires Date', exact=True).fill('2099-01-01')
    page.once('dialog', lambda dialog: dialog.dismiss())
    before = len(requests)
    page.get_by_role('button', name='Save changes', exact=True).click()
    assert len(requests) == before, 'Cancelling behavior activation must not submit settings.'
    page.set_viewport_size({"width": 390, "height": 844})
    page.screenshot(path=str(output / 'mailbox-behavior-mobile.png'), full_page=True)
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Behavior controls overflow on mobile.'
    page.set_viewport_size({"width": 1440, "height": 1000})
    page.once('dialog', lambda dialog: dialog.accept())
    page.get_by_role('button', name='Save changes', exact=True).click()
    page.get_by_text('Forwarding and replies saved.', exact=True).wait_for()
    assert behavior['forwardEnabled'] and behavior['autoReplyExpiresDate'] == '2099-01-01'
    page.get_by_role('button', name='Forwarding and replies', exact=True).click()
    assert page.get_by_label('auto Reply Body', exact=True).input_value().endswith('<script>literal text</script>')
    page.get_by_role('button', name='Cancel', exact=True).click()
    page.screenshot(path=str(output / 'mailboxes-desktop.png'), full_page=True)
    page.set_viewport_size({"width": 390, "height": 844})
    page.screenshot(path=str(output / 'mailboxes-mobile.png'), full_page=True)
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Mailbox controls overflow on mobile.'
    page.set_viewport_size({"width": 1440, "height": 1000})
    page.once('dialog', lambda dialog: dialog.dismiss())
    page.get_by_role('button', name='Delete mailbox', exact=True).click()
    assert len(mailboxes) == 1, 'Cancelling deletion must preserve the mailbox.'
    page.once('dialog', lambda dialog: dialog.accept())
    page.get_by_role('button', name='Delete mailbox', exact=True).click()
    page.get_by_text('Mailbox deleted.', exact=True).wait_for()
    assert not mailboxes
    page.once('dialog', lambda dialog: dialog.accept())
    page.get_by_role('button', name='Delete domain', exact=True).last.click()
    page.get_by_text('Domain deleted.', exact=True).wait_for()
    assert len(domains) == 1
    assert page.get_by_role('button', name='Inspect DNS').count() == 1, 'Domain mutations must refresh the visible list after repeated saves.'
    page.get_by_role('button', name='Aliases', exact=True).click()
    page.get_by_role('button', name='New alias', exact=True).click()
    page.get_by_label('address', exact=True).fill('support@example.com')
    page.get_by_label('target', exact=True).fill('external@example.net')
    page.get_by_role('button', name='Save changes', exact=True).click()
    page.get_by_text('support@example.com', exact=True).wait_for()
    assert aliases[0]['active'] is False
    page.get_by_role('button', name='Edit alias', exact=True).click()
    page.get_by_label('active', exact=True).check()
    page.get_by_role('button', name='Save changes', exact=True).click()
    page.get_by_text('Alias saved.', exact=True).wait_for()
    assert aliases[0]['active'] is True
    page.once('dialog', lambda dialog: dialog.dismiss())
    page.get_by_role('button', name='Delete alias', exact=True).click()
    assert len(aliases) == 1
    page.once('dialog', lambda dialog: dialog.accept())
    page.get_by_role('button', name='Delete alias', exact=True).click()
    page.get_by_text('Alias deleted.', exact=True).wait_for()
    assert not aliases
    page.get_by_role('button', name='Distribution lists', exact=True).click()
    page.get_by_role('button', name='New distribution list', exact=True).click()
    page.get_by_label('address', exact=True).fill('news@example.com')
    assert page.get_by_label('mode', exact=True).input_value() == '1'
    assert page.get_by_label('require Auth', exact=True).is_checked()
    page.get_by_role('button', name='Save changes', exact=True).click()
    page.get_by_text('news@example.com', exact=True).wait_for()
    assert lists[0]['active'] is False and lists[0]['mode'] == 1 and lists[0]['requireAuth'] is True
    page.get_by_role('button', name='Members', exact=True).click()
    page.get_by_role('button', name='New member', exact=True).click()
    page.get_by_label('address', exact=True).fill('recipient@external.test')
    page.get_by_role('button', name='Save changes', exact=True).click()
    page.get_by_text('recipient@external.test', exact=True).wait_for()
    page.get_by_role('button', name='Edit member', exact=True).click()
    page.get_by_label('address', exact=True).fill('updated@external.test')
    page.get_by_role('button', name='Save changes', exact=True).click()
    page.get_by_text('updated@external.test', exact=True).wait_for()
    page.set_viewport_size({"width": 390, "height": 844})
    page.screenshot(path=str(output / 'list-members-mobile.png'), full_page=True)
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'List member controls overflow on mobile.'
    page.set_viewport_size({"width": 1440, "height": 1000})
    page.once('dialog', lambda dialog: dialog.accept())
    page.get_by_role('button', name='Delete member', exact=True).click()
    page.get_by_text('Member deleted.', exact=True).wait_for()
    page.get_by_role('button', name='Edit distribution list', exact=True).click()
    page.get_by_label('mode', exact=True).select_option('2')
    page.get_by_label('require Address', exact=True).fill('owner@example.com')
    page.get_by_label('active', exact=True).check()
    page.get_by_role('button', name='Save changes', exact=True).click()
    page.get_by_text('Announcements', exact=False).last.wait_for()
    assert lists[0]['mode'] == 2 and lists[0]['requireAddress'] == 'owner@example.com'
    page.get_by_role('button', name='Edit distribution list', exact=True).click()
    page.get_by_label('mode', exact=True).select_option('0')
    page.once('dialog', lambda dialog: dialog.dismiss())
    page.get_by_role('button', name='Save changes', exact=True).click()
    assert lists[0]['mode'] == 2, 'Cancelling public-mode activation must preserve the saved posting mode.'
    page.once('dialog', lambda dialog: dialog.accept())
    with page.expect_response(lambda response: '/lists/' in response.url and response.request.method == 'PATCH'):
        page.get_by_role('button', name='Save changes', exact=True).click()
    assert lists[0]['mode'] == 0
    page.screenshot(path=str(output / 'distribution-lists-desktop.png'), full_page=True)
    page.once('dialog', lambda dialog: dialog.accept())
    page.get_by_role('button', name='Delete distribution list', exact=True).click()
    page.get_by_text('Distribution list deleted.', exact=True).wait_for()
    assert not lists and not list_members
    page.get_by_role('button', name='Inspect DNS').click()
    page.get_by_label('Sending IP addresses (comma separated)').fill('203.0.113.10')
    page.get_by_role('button', name='Validate published DNS & build recommendations').click()
    page.get_by_text('DNS inspection only; message alignment needs testing.', exact=True).wait_for()
    assert requests[-1][2]['addresses'] == ['203.0.113.10']
    page.screenshot(path=str(output / 'dns-desktop.png'), full_page=True)
    page.set_viewport_size({"width": 390, "height": 844})
    page.screenshot(path=str(output / 'dns-mobile.png'), full_page=True)
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Mobile page overflows horizontally.'
    user = {"address": "manager@example.com", "level": 1, "domainId": 5, "accountId": 11}
    mailboxes.append({"id": 12, "domainId": 5, "address": "server@example.com", "active": True, "maxSizeMB": 10, "level": 2})
    page.reload()
    page.get_by_role('button', name='Mailboxes', exact=True).wait_for()
    assert page.get_by_role('button', name='New domain', exact=True).count() == 0
    assert page.get_by_role('button', name='Delete domain', exact=True).count() == 0
    page.get_by_role('button', name='Edit domain', exact=True).click()
    assert page.get_by_label('max Account Size MB', exact=True).count() == 0
    page.get_by_role('button', name='Mailboxes', exact=True).click()
    page.get_by_text('server@example.com', exact=True).wait_for()
    assert page.get_by_role('button', name='Edit mailbox', exact=True).count() == 0
    assert page.get_by_role('button', name='Delete mailbox', exact=True).count() == 0
    page.get_by_role('button', name='New mailbox', exact=True).click()
    assert page.get_by_label('level', exact=True).locator('option[value="2"]').count() == 0
    page.screenshot(path=str(output / 'domain-admin-mobile.png'), full_page=True)
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Domain administrator editor overflows on mobile.'
    page.get_by_role('button', name='Sign out').click()
    page.get_by_role('heading', name='Sign in to your server').wait_for()
    page.screenshot(path=str(output / 'login-mobile.png'), full_page=True)
    user = {"address": "Administrator", "level": 2, "domainId": 0, "accountId": 0}
    page.reload()
    page.get_by_role('button', name='Mailboxes', exact=True).wait_for()
    revoked = True
    with page.expect_response(lambda response: response.url.endswith('/api/v1/session') and response.request.method == 'GET'):
        page.get_by_role('button', name='Refresh', exact=True).click()
    page.get_by_role('heading', name='Sign in to your server').wait_for(timeout=5000)
    assert requests[-1][0:2] == ('GET', '/session'), 'Revocation must obtain a fresh anonymous CSRF token.'
    assert not errors, errors
    browser.close()
print('Passed browser navigation, domain/mailbox/alias/list/member CRUD, posting-policy confirmation, password handling, DNSBL mutation, SpamAssassin test, DNS guidance, session recovery and mobile layout checks.')
