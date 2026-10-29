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
    original_pop_limit = None
    original_tls_cipher = None
    try:
        stage = 'server login and version'
        login(page, os.environ['ARZOIEMAIL_TEST_USERNAME'])
        page.get_by_text('Running', exact=True).wait_for()
        response = context.request.get(base + '/api/v1/status')
        assert response.status == 200 and response.json()['data']['version'].startswith('6.')
        assert 'frame-ancestors' in response.headers['content-security-policy']
        stage = 'configured transport inventory'
        page.get_by_role('link', name='Mail transport', exact=True).click()
        page.get_by_role('heading', name='Configured listeners', exact=True).wait_for()
        page.get_by_text('These are saved settings. Running listeners can differ until the service is restarted.', exact=True).wait_for()
        page.get_by_label('Transport inventory', exact=True).select_option('certificates')
        page.get_by_role('heading', name='Configured certificates', exact=True).wait_for()
        page.get_by_label('search', exact=True).fill(os.environ['ARZOIEMAIL_TEST_CERTIFICATE_NAME'])
        page.get_by_role('button', name='Search transport', exact=True).click()
        page.get_by_text(os.environ['ARZOIEMAIL_TEST_CERTIFICATE_NAME'], exact=True).wait_for()
        assert page.evaluate('typeof window.transportInventoryExecuted') == 'undefined', 'Certificate names must remain inert text.'
        assert 'not-used-private-key.pem' not in page.locator('#content').inner_text()
        page.set_viewport_size({"width": 390, "height": 844})
        assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Transport inventory overflows on mobile.'
        page.screenshot(path=str(output / 'transport-inventory-mobile.png'), full_page=True)
        page.set_viewport_size({"width": 1440, "height": 1000})
        stage = 'server settings persistence'
        settings = context.request.get(base + '/api/v1/settings/pop3')
        assert settings.status == 200
        original_pop_limit = settings.json()['data']['values']['maxConnections']
        changed_limit = 137 if original_pop_limit != 137 else 138
        page.get_by_role('link', name='Server settings', exact=True).click()
        page.get_by_label('Settings section', exact=True).select_option('pop3')
        page.get_by_label('max Connections', exact=True).fill(str(changed_limit))
        page.once('dialog', lambda dialog: dialog.accept())
        with page.expect_response(lambda item: item.url.endswith('/settings/pop3') and item.request.method == 'PATCH') as saved_settings:
            page.get_by_role('button', name='Save server settings', exact=True).click()
        assert saved_settings.value.status == 200
        page.reload()
        page.get_by_label('Settings section', exact=True).select_option('pop3')
        assert page.get_by_label('max Connections', exact=True).input_value() == str(changed_limit)
        page.get_by_label('max Connections', exact=True).fill(str(original_pop_limit))
        page.once('dialog', lambda dialog: dialog.accept())
        with page.expect_response(lambda item: item.url.endswith('/settings/pop3') and item.request.method == 'PATCH') as restored_settings:
            page.get_by_role('button', name='Save server settings', exact=True).click()
        assert restored_settings.value.status == 200
        original_pop_limit = None
        stage = 'TLS policy validation and persistence'
        tls = context.request.get(base + '/api/v1/settings/tls')
        assert tls.status == 200
        original_tls_cipher = tls.json()['data']['values']['cipherList']
        page.get_by_label('Settings section', exact=True).select_option('tls')
        cipher = page.get_by_label('cipher List', exact=True)
        cipher.fill('ARZOIEMAIL-NO-SUCH-CIPHER')
        page.once('dialog', lambda dialog: dialog.accept())
        with page.expect_response(lambda item: item.url.endswith('/settings/tls') and item.request.method == 'PATCH') as rejected_tls:
            page.get_by_role('button', name='Save server settings', exact=True).click()
        assert rejected_tls.value.status == 422
        assert context.request.get(base + '/api/v1/settings/tls').json()['data']['values']['cipherList'] == original_tls_cipher
        cipher.fill('HIGH:!aNULL:!eNULL')
        page.once('dialog', lambda dialog: dialog.accept())
        with page.expect_response(lambda item: item.url.endswith('/settings/tls') and item.request.method == 'PATCH') as saved_tls:
            page.get_by_role('button', name='Save server settings', exact=True).click()
        assert saved_tls.value.status == 200
        page.reload()
        page.get_by_label('Settings section', exact=True).select_option('tls')
        cipher = page.get_by_label('cipher List', exact=True)
        assert cipher.input_value() == 'HIGH:!aNULL:!eNULL'
        assert page.evaluate('document.documentElement.scrollWidth <= innerWidth')
        cipher.fill(original_tls_cipher)
        page.once('dialog', lambda dialog: dialog.accept())
        with page.expect_response(lambda item: item.url.endswith('/settings/tls') and item.request.method == 'PATCH') as restored_tls:
            page.get_by_role('button', name='Save server settings', exact=True).click()
        assert restored_tls.value.status == 200
        original_tls_cipher = None
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
                assert not role_page.get_by_role('link', name='Server settings', exact=True).is_visible()
                assert role_context.request.get(base + '/api/v1/settings/pop3').status == 403
                assert role_context.request.get(base + '/api/v1/settings/tls').status == 403
                assert not role_page.get_by_role('link', name='Mail transport', exact=True).is_visible()
                assert role_context.request.get(base + '/api/v1/transport/listeners').status == 403
                assert role_context.request.get(base + '/api/v1/transport/certificates').status == 403
                stage = 'live mailbox self-service'
                own_profile = role_context.request.get(base + '/api/v1/self/profile')
                assert own_profile.status == 200
                original_name = own_profile.json()['data']['firstName']
                role_page.get_by_role('link', name='My mailbox', exact=True).click()
                role_page.get_by_role('button', name='My profile', exact=True).click()
                role_page.get_by_label('first Name', exact=True).fill('Own live browser profile')
                with role_page.expect_response(lambda item: item.url.endswith('/self/profile') and item.request.method == 'PATCH') as own_saved:
                    role_page.get_by_role('button', name='Save changes', exact=True).click()
                assert own_saved.value.status == 200
                assert role_context.request.get(base + '/api/v1/self/profile').json()['data']['firstName'] == 'Own live browser profile'
                role_page.reload()
                role_page.get_by_role('button', name='My profile', exact=True).click()
                assert role_page.get_by_label('first Name', exact=True).input_value() == 'Own live browser profile'
                role_page.get_by_label('first Name', exact=True).fill(original_name)
                with role_page.expect_response(lambda item: item.url.endswith('/self/profile') and item.request.method == 'PATCH') as own_restored:
                    role_page.get_by_role('button', name='Save changes', exact=True).click()
                assert own_restored.value.status == 200
                assert role_page.evaluate('document.documentElement.scrollWidth <= innerWidth')
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
        if original_tls_cipher is not None:
            try:
                session = context.request.get(base + '/api/v1/session')
                current = context.request.get(base + '/api/v1/settings/tls')
                if session.status == 200 and current.status == 200:
                    restored = context.request.patch(base + '/api/v1/settings/tls', data={
                        'fields': {'cipherList': original_tls_cipher},
                        'expected': {'cipherList': current.json()['data']['values']['cipherList']}},
                        headers={'Content-Type': 'application/json', 'X-CSRF-Token': session.json()['data']['csrf']})
                    assert restored.status == 200
            except Exception:
                print('Published live browser TLS cleanup could not complete.', file=sys.stderr)
        if original_pop_limit is not None:
            try:
                session = context.request.get(base + '/api/v1/session')
                current = context.request.get(base + '/api/v1/settings/pop3')
                if session.status == 200 and current.status == 200:
                    context.request.patch(base + '/api/v1/settings/pop3', data={
                        'fields': {'maxConnections': original_pop_limit},
                        'expected': {'maxConnections': current.json()['data']['values']['maxConnections']}},
                        headers={'Content-Type': 'application/json', 'X-CSRF-Token': session.json()['data']['csrf']})
            except Exception:
                print('Published live browser settings cleanup could not complete.', file=sys.stderr)
        if domain_id is not None:
            try:
                session = context.request.get(base + '/api/v1/session')
                if session.status == 200:
                    token = session.json()['data']['csrf']
                    context.request.delete(base + f'/api/v1/domains/{domain_id}', data='{}', headers={'Content-Type': 'application/json', 'X-CSRF-Token': token})
            except Exception:
                print('Published live browser fixture cleanup could not complete.', file=sys.stderr)
        browser.close()
