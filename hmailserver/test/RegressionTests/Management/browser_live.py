"""Published panel -> real REST -> native service -> selected CI database. No API interception."""
import os
import pathlib
import sys
import uuid
from urllib.parse import urlsplit
from playwright.sync_api import sync_playwright

base = sys.argv[1].rstrip('/')
output = pathlib.Path(sys.argv[2])
output.mkdir(parents=True, exist_ok=True)
password = os.environ['ARZOIEMAIL_TEST_PASSWORD']
stage = 'startup'


def login(page, username):
    page.goto(base)
    page.get_by_role('heading', name='Sign in to your server').wait_for()
    page.get_by_label('Username', exact=True).fill(username)
    page.get_by_label('Password', exact=True).fill(password)
    page.get_by_role('button', name='Sign in', exact=True).click()
    page.get_by_role('button', name='Sign out', exact=True).wait_for()


with sync_playwright() as playwright:
    browser = playwright.chromium.launch(channel='msedge', headless=True)
    context = browser.new_context(viewport={"width": 1440, "height": 1000})
    page = context.new_page()
    errors = []
    page.on('pageerror', lambda error: errors.append(type(error).__name__))
    domain_id = None
    try:
        stage = 'server login and version'
        login(page, os.environ['ARZOIEMAIL_TEST_USERNAME'])
        page.get_by_text('Running', exact=True).wait_for()
        response = context.request.get(base + '/api/v1/status')
        assert response.status == 200 and response.json()['data']['version'].startswith('6.')
        assert 'frame-ancestors' in response.headers['content-security-policy']
        stage = 'domain persistence'
        domain = 'browser-' + uuid.uuid4().hex + '.test'
        page.get_by_role('link', name='Domains & DNS').click()
        page.get_by_role('button', name='New domain', exact=True).click()
        page.get_by_label('name', exact=True).fill(domain)
        with page.expect_response(lambda item: urlsplit(item.url).path == '/api/v1/domains' and item.request.method == 'POST') as created:
            page.get_by_role('button', name='Save changes', exact=True).click()
        assert created.value.status == 200
        domain_id = created.value.json()['data']['id']
        page.reload()
        row = page.locator('.domain-row').filter(has=page.get_by_text(domain, exact=True))
        row.get_by_role('button', name='Mailboxes', exact=True).click()
        stage = 'mailbox and profile persistence'
        page.get_by_role('button', name='New mailbox', exact=True).click()
        address = 'mail@' + domain
        page.get_by_label('address', exact=True).fill(address)
        page.get_by_label('password', exact=True).fill(password)
        with page.expect_response(lambda item: item.url.endswith(f'/domains/{domain_id}/accounts') and item.request.method == 'POST') as mailbox:
            page.get_by_role('button', name='Save changes', exact=True).click()
        assert mailbox.value.status == 200 and 'password' not in mailbox.value.json()['data']
        account_id = mailbox.value.json()['data']['id']
        page.get_by_text(address, exact=True).wait_for()
        page.get_by_role('button', name='Profile', exact=True).click()
        page.get_by_label('first Name', exact=True).fill('Published panel')
        with page.expect_response(lambda item: item.url.endswith('/profile') and item.request.method == 'PATCH'):
            page.get_by_role('button', name='Save changes', exact=True).click()
        persisted = context.request.get(base + f'/api/v1/domains/{domain_id}/accounts/{account_id}/profile')
        assert persisted.status == 200 and persisted.json()['data']['firstName'] == 'Published panel'
        stage = 'rule draft through real API'
        page.get_by_role('button', name='Rules', exact=True).click()
        page.get_by_role('button', name='New rule', exact=True).click()
        page.get_by_label('name', exact=True).fill('Published browser draft')
        page.get_by_label('value', exact=True).fill('browser-fixture')
        with page.expect_response(lambda item: item.url.endswith('/rules') and item.request.method == 'POST') as rule:
            page.get_by_role('button', name='Save draft', exact=True).click()
        assert rule.value.status == 200 and rule.value.json()['data']['active'] is False
        page.get_by_text('Published browser draft', exact=True).wait_for()
        page.set_viewport_size({"width": 390, "height": 844})
        assert page.evaluate('document.documentElement.scrollWidth <= innerWidth')
        page.screenshot(path=str(output / 'published-live-mobile.png'), full_page=True)
        stage = 'domain cleanup through UI'
        page.get_by_role('link', name='Domains & DNS').click()
        row = page.locator('.domain-row').filter(has=page.get_by_text(domain, exact=True))
        page.once('dialog', lambda dialog: dialog.accept())
        with page.expect_response(lambda item: urlsplit(item.url).path == f'/api/v1/domains/{domain_id}' and item.request.method == 'DELETE') as deleted:
            row.get_by_role('button', name='Delete domain', exact=True).click()
        assert deleted.value.status == 200
        domain_id = None
        stage = 'live role boundaries'
        for key, level in [('ARZOIEMAIL_TEST_DOMAIN_USERNAME', 1), ('ARZOIEMAIL_TEST_USER_USERNAME', 0)]:
            role_context = browser.new_context(viewport={"width": 390, "height": 844})
            try:
                role_page = role_context.new_page()
                login(role_page, os.environ[key])
                identity = role_context.request.get(base + '/api/v1/session').json()['data']['user']
                assert identity['level'] == level
                for title in ['Delivery queue', 'Routing', 'Global rules']:
                    assert not role_page.get_by_role('link', name=title).is_visible()
                assert role_context.request.get(base + '/api/v1/queue').status == 403
                role_page.get_by_role('button', name='Sign out', exact=True).click()
                role_page.get_by_role('heading', name='Sign in to your server').wait_for()
                assert role_context.request.get(base + '/api/v1/status').status == 401
            finally:
                role_context.close()
        assert not errors, 'Browser JavaScript errors'
        print('Passed published live browser login/version, persisted domain/mailbox/profile/rule workflows, roles, logout and mobile checks (no API mocks).')
    except Exception as error:
        # Never print Playwright's input argument history or capture HAR/traces containing passwords.
        print(f'Published live browser failed at {stage}: {type(error).__name__}', file=sys.stderr)
        page.locator('input[type=password]').evaluate_all('(items) => items.forEach(item => item.value = "")')
        page.screenshot(path=str(output / 'published-live-failure.png'), full_page=True)
        raise SystemExit(1) from None
    finally:
        if domain_id is not None:
            try:
                session = context.request.get(base + '/api/v1/session')
                if session.status == 200:
                    token = session.json()['data']['csrf']
                    context.request.delete(base + f'/api/v1/domains/{domain_id}', data='{}', headers={'Content-Type': 'application/json', 'X-CSRF-Token': token})
            except Exception:
                print('Published live browser fixture cleanup could not complete.', file=sys.stderr)
        browser.close()
