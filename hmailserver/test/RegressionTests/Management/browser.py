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
signature = {"signatureEnabled": False, "signaturePlainText": "", "signatureHtml": ""}
profile = {"firstName": "", "lastName": "", "directoryBacked": False, "lastLogon": "2026-10-27 12:00:00"}
domain_settings = {"catchAllAddress": "", "plusAddressingEnabled": False, "plusAddressingCharacter": "+", "greylistingEnabled": True,
                   "maxAccounts": 0, "maxAliases": 0, "maxLists": 0, "signatureEnabled": False,
                   "signaturePlainText": "", "signatureHtml": "", "signatureMethod": 1, "signaturesOnReplies": False, "signaturesOnLocalMail": True}
domain_names = []
queue_items = [{"id": "9007199254740993", "sender": "sender@example.com", "size": 100, "attempts": 1, "nextTry": "2026-10-28 12:00:00", "created": "2026-10-28 11:00:00", "busy": False, "held": True, "recipientCount": 1, "recipients": ["recipient@example.net"]},
               {"id": "9007199254740994", "sender": "busy@example.com", "size": 200, "attempts": 0, "nextTry": "now", "busy": True, "held": False}]
routes = []
route_addresses = []
rules = {}
revoked = False
server_settings = {
    'protocols': {'section': 'protocols', 'values': {'smtpEnabled': True},
                  'schema': [{'name': 'smtpEnabled', 'type': 'boolean', 'minimum': 0, 'maximum': 1, 'help': 'SMTP listener enablement.', 'restart': True}]},
    'pop3': {'section': 'pop3', 'values': {'maxConnections': 7, 'welcomeMessage': 'ArzoieMail'},
             'schema': [{'name': 'maxConnections', 'type': 'integer', 'minimum': 0, 'maximum': 1000000, 'help': 'Connection limit.', 'restart': False},
                        {'name': 'welcomeMessage', 'type': 'string', 'minimum': 0, 'maximum': 255, 'help': 'Greeting text.', 'restart': False}]}
}
server_settings['passwordhash'] = {'section': 'passwordhash', 'values': {'algorithm': 1, 'memoryKiB': 0, 'iterations': 2, 'autoUpgrade': True},
    'schema': [{'name': name, 'type': 'boolean' if name == 'autoUpgrade' else 'integer', 'minimum': 0,
                'maximum': 1 if name == 'autoUpgrade' else 2 if name == 'algorithm' else 10000000,
                'help': 'Algorithm-specific policy; zero uses defaults.', 'restart': False}
               for name in ['algorithm', 'memoryKiB', 'iterations', 'autoUpgrade']]}
server_settings['logging'] = {'section': 'logging',
    'values': {key: key in ['enabled', 'application', 'debug'] for key in ['enabled', 'application', 'smtp', 'pop3', 'imap', 'tcpIp', 'debug', 'keepFilesOpen', 'awStats']},
    'schema': [{'name': key, 'type': 'boolean', 'minimum': 0, 'maximum': 1, 'help': 'Private native logging configuration.', 'restart': False}
               for key in ['enabled', 'application', 'smtp', 'pop3', 'imap', 'tcpIp', 'debug', 'keepFilesOpen', 'awStats']]}
server_settings['smtp'] = {'section': 'smtp', 'values': {'deliverySecurity': 2, 'smartHost': '', 'smartHostAuthentication': False},
    'schema': [{'name': 'deliverySecurity', 'type': 'integer', 'minimum': 0, 'maximum': 3, 'help': 'Direct delivery security.', 'restart': False,
                'options': [{'value': 0, 'label': 'None'}, {'value': 2, 'label': 'STARTTLS when offered'}, {'value': 3, 'label': 'STARTTLS required'}]},
               {'name': 'smartHost', 'type': 'string', 'minimum': 0, 'maximum': 253, 'help': 'Upstream relay.', 'restart': False},
               {'name': 'smartHostAuthentication', 'type': 'boolean', 'minimum': 0, 'maximum': 1, 'help': 'Relay authentication.', 'restart': False}],
    'credentials': {'smartHostConfigured': False}}
server_settings['imap'] = {'section': 'imap', 'values': {'masterUser': '', 'hierarchyDelimiter': '.'},
    'schema': [{'name': 'masterUser', 'type': 'string', 'minimum': 0, 'maximum': 320, 'help': 'Master user.', 'restart': False},
               {'name': 'hierarchyDelimiter', 'type': 'string', 'minimum': 0, 'maximum': 1, 'help': 'Separator.', 'restart': False, 'direct': True,
                'options': [{'value': '.', 'label': 'Period (.)'}, {'value': '/', 'label': 'Slash (/)'}, {'value': '\\', 'label': 'Backslash (\\)'}]}]}
server_state = {'state': 3}
server_messages = [{'id': '1', 'name': 'VIRUS_FOUND', 'text': 'Virus found'}, {'id': '2', 'name': 'SEND_FAILED_NOTIFICATION', 'text': '<b>inert</b> text'}]
sendout_requests = []
diagnostics_state = {'running': False, 'run': 0, 'completed': '', 'results': []}
log_text = 'first line\n<script>window.logInjected = true</script>\nlast line\n'
fetch_accounts = []
groups = [{'id': 1, 'name': 'Sales', 'members': 0}]
group_members = []
public_folders = [{'id': 7, 'parentId': 0, 'name': 'Shared', 'path': 'Shared'}]
folder_permissions = []
backup_state = {'running': False, 'destination': 'D:\\Backups', 'archives': [{'name': 'HMBackup 2026-10-30 090000.7z', 'size': 2097152, 'modified': '2026-10-30T09:00:00Z'}]}
restore_requests = []
server_settings['backup'] = {'section': 'backup', 'values': {'includeSettings': True, 'includeDomains': True, 'includeMessages': False, 'compress': True, 'destination': 'D:\\Backups'},
    'schema': [{'name': key, 'type': 'boolean', 'minimum': 0, 'maximum': 1, 'help': 'Backup content.', 'restart': False} for key in ['includeSettings', 'includeDomains', 'includeMessages', 'compress']]
              + [{'name': 'destination', 'type': 'string', 'minimum': 0, 'maximum': 240, 'help': 'Backup folder.', 'restart': False}],
    'credentials': {'passphraseConfigured': False}}
policy_items = {'surbl': [], 'blockedattachments': [], 'incomingrelays': [], 'securityranges': [], 'spamallowlist': [], 'greylistexceptions': []}
dkim_state = {'domainId': 5, 'domain': 'example.com', 'signEnabled': False, 'signAliases': False, 'selector': '', 'headerCanonicalization': 2, 'bodyCanonicalization': 2,
              'algorithm': 2, 'keyConfigured': False, 'keyManaged': False, 'keys': []}
dkim_requests = []
server_settings['antivirus'] = {'section': 'antivirus', 'values': {'clamAvEnabled': True, 'clamAvHost': 'clamav.internal', 'clamAvPort': 3310},
    'schema': [{'name': 'clamAvEnabled', 'type': 'boolean', 'minimum': 0, 'maximum': 1, 'help': 'ClamAV.', 'restart': False},
               {'name': 'clamAvHost', 'type': 'string', 'minimum': 0, 'maximum': 253, 'help': 'Host.', 'restart': False},
               {'name': 'clamAvPort', 'type': 'integer', 'minimum': 1, 'maximum': 65535, 'help': 'Port.', 'restart': False}]}
policy_defaults = {
    'surbl': {'zone': '', 'active': False, 'score': 0, 'rejectMessage': ''},
    'blockedattachments': {'pattern': '', 'description': ''},
    'incomingrelays': {'name': '', 'lowerAddress': '0.0.0.0', 'upperAddress': '0.0.0.0'},
    'spamallowlist': {'lowerAddress': '0.0.0.0', 'upperAddress': '255.255.255.255', 'address': '', 'description': ''},
    'greylistexceptions': {'address': '', 'description': ''}
}
policy_defaults['securityranges'] = {'name': '', 'lowerAddress': '0.0.0.0', 'upperAddress': '0.0.0.0', 'priority': 0,
    **{key: False for key in ['allowSmtp', 'allowImap', 'allowPop3', 'localToLocal', 'localToExternal', 'externalToLocal',
        'externalToExternal', 'authLocalToLocal', 'authLocalToExternal', 'authExternalToLocal', 'authExternalToExternal',
        'requireTlsForAuth', 'spamProtection', 'virusProtection', 'expires', 'acknowledgeUnauthenticatedRelay']}, 'expiresAt': '2001-01-01 00:00:00'}


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
    elif path == '/self':
        data = {'address': user['address'], 'directoryBacked': False, 'passwordChangeable': True}
    elif path == '/self/password':
        user = None
        data = {'changed': True, 'user': None, 'csrf': 'browser-fixture-token'}
    elif path == '/status':
        data = {"version": "6.0.0", "architecture": "x64", "state": server_state['state'], "domains": 2, "startTime": "2026-10-30 08:00:00",
                "processedMessages": 41, "spamMessagesDetected": 3, "virusesRemoved": 0, "sessions": {"smtp": 2, "pop3": 0, "imap": 5},
                "cacheHitRates": {"domain": 90, "account": 80, "alias": 70, "list": 0}}
    elif path in ('/server/start', '/server/stop'):
        expected = 1 if path.endswith('start') else 3
        if body != {'expectedState': expected} or server_state['state'] != expected:
            route.fulfill(status=409, json={'error': {'message': 'Server state changed.'}})
            return
        server_state['state'] = 3 if expected == 1 else 1
        data = {'state': server_state['state']}
    elif path == '/settings/smtp/smart-host-credential':
        server_settings['smtp']['credentials']['smartHostConfigured'] = bool(body['password'])
        data = {'configured': bool(body['password'])}
    elif path == '/settings/performance/message-index/clear':
        data = {'cleared': True}
    elif path == '/servermessages':
        data = {'items': server_messages, 'maximum': 8000}
    elif path.startswith('/servermessages/'):
        item = next(value for value in server_messages if value['id'] == path.rsplit('/', 1)[1])
        if body.get('expected') != item:
            route.fulfill(status=409, json={'error': {'message': 'Server message changed. Reload before saving.'}})
            return
        item['text'] = body['text']
        data = item
    elif path.startswith('/domains/5/dkim'):
        dkim_requests.append((request.method, path, body))
        if path.endswith('/keys') and request.method == 'POST':
            dkim_state['keys'].append({'selector': body['selector'], 'dnsName': body['selector'] + '._domainkey.example.com', 'dnsRecord': 'v=DKIM1; k=rsa; p=MIIBfixture', 'active': False})
        elif path.endswith('/activate'):
            if not body.get('skipDnsCheck'):
                route.fulfill(status=409, json={'error': {'message': 'The DNS record does not publish this key yet.'}})
                return
            selector = path.split('/')[-2]
            for key in dkim_state['keys']: key['active'] = key['selector'] == selector
            dkim_state.update({'selector': selector, 'keyConfigured': True, 'keyManaged': True})
        elif request.method == 'PATCH':
            dkim_state.update(body)
        data = dkim_state
    elif path == '/antivirus':
        data = {'clamAv': {'enabled': True, 'host': 'clamav.internal', 'port': 3310}, 'customScanner': {'enabled': False, 'executable': '', 'returnValue': 1},
                'clamWin': {'enabled': False, 'executable': '', 'database': ''}}
    elif path == '/antivirus/test':
        data = {'scanner': body['scanner'], 'passed': True, 'message': 'Virus found: Eicar-Test-Signature <b>inert</b>'}
    elif '/fetchaccounts' in path:
        parts = path.split('/')
        if parts[-1] == 'fetchaccounts' and request.method == 'POST':
            data = {'id': len(fetch_accounts) + 1, 'accountId': 10, 'active': False, 'running': False, 'nextTry': '', 'passwordConfigured': True,
                    **{key: value for key, value in body.items() if key != 'password'}}
            data.setdefault('security', 1)
            fetch_accounts.append(data)
        elif parts[-1] == 'fetchaccounts':
            data = {'items': fetch_accounts, 'total': len(fetch_accounts), 'offset': 0, 'limit': 50}
        elif parts[-1] == 'run':
            data = fetch_accounts[0]
        else:
            item = next(value for value in fetch_accounts if value['id'] == int(parts[-1]))
            if request.method == 'DELETE':
                fetch_accounts.remove(item)
                data = {'deleted': True}
            else:
                item.update({key: value for key, value in body.items() if key != 'password'})
                data = item
    elif path.startswith('/groups'):
        parts = path.split('/')
        if path == '/groups' and request.method == 'POST':
            data = {'id': len(groups) + 1, 'name': body['name'], 'members': 0}
            groups.append(data)
        elif path == '/groups':
            data = {'items': groups, 'total': len(groups), 'offset': 0, 'limit': 50}
        elif parts[-1] == 'members' and request.method == 'POST':
            data = {'id': len(group_members) + 1, 'groupId': int(parts[2]), 'accountId': body['accountId'], 'address': 'user@example.com'}
            group_members.append(data)
        elif parts[-1] == 'members':
            data = {'items': group_members, 'total': len(group_members)}
        else:
            data = {'deleted': True}
    elif path.startswith('/publicfolders'):
        parts = path.split('/')
        if path == '/publicfolders' and request.method == 'POST':
            data = {'id': 8, 'parentId': body['parentId'], 'name': body['name'], 'path': body['name']}
            public_folders.append(data)
        elif path == '/publicfolders':
            data = {'items': public_folders, 'delimiter': '.'}
        elif parts[-1] == 'permissions' and request.method == 'POST':
            data = {'id': len(folder_permissions) + 1, 'folderId': int(parts[2]), 'holder': '', 'accountId': 0, 'groupId': body.get('groupId', 0),
                    'rights': {**{right: False for right in ['lookup', 'read', 'writeSeen', 'writeOthers', 'insert', 'post', 'create', 'deleteMailbox', 'writeDeleted', 'expunge', 'administer']}, **body['rights']}, 'type': body['type']}
            folder_permissions.append(data)
        elif parts[-1] == 'permissions':
            data = {'items': folder_permissions, 'rights': ['lookup', 'read', 'writeSeen', 'writeOthers', 'insert', 'post', 'create', 'deleteMailbox', 'writeDeleted', 'expunge', 'administer']}
        else:
            data = {'deleted': True}
    elif path == '/backup':
        data = backup_state
    elif path == '/backup/start':
        data = backup_state
    elif path == '/backup/restore':
        restore_requests.append(body)
        data = backup_state
    elif path == '/settings/backup/passphrase':
        server_settings['backup']['credentials']['passphraseConfigured'] = bool(body['passphrase'])
        data = {'configured': bool(body['passphrase'])}
    elif path == '/secretkey':
        data = {'keyId': 'abc123'}
    elif path == '/secretkey/export':
        data = {'keyId': 'abc123', 'exported': 'ARZOIEMAIL-KEY-1:fixture'}
    elif path == '/secretkey/import':
        data = {'keyId': 'abc123'}
    elif path == '/sendout/preview':
        data = {'recipients': 2 if body['recipients'] == '*@example.com' else 0}
    elif path == '/sendout':
        sendout_requests.append(body)
        data = {'queued': True, 'recipients': body['expectedRecipients']}
    elif path == '/diagnostics/run':
        diagnostics_state.update({'running': False, 'run': diagnostics_state['run'] + 1, 'completed': '2026-10-30 09:00:00',
            'results': [{'name': 'Data directory', 'description': 'Checks the data directory.', 'success': True, 'details': ''},
                        {'name': 'Outbound port', 'description': 'Connects to port 25.', 'success': False, 'details': '<img src=x onerror=alert(1)>'}]})
        data = diagnostics_state
    elif path == '/diagnostics/mail-servers':
        data = {'domain': body['domain'], 'resolved': True, 'items': [{'host': 'mx.example.net', 'address': '192.0.2.25'}]}
    elif path == '/diagnostics/results':
        data = diagnostics_state
    elif path == '/logs':
        data = {'items': [{'name': 'hmailserver_2026-10-30.log', 'size': 40000, 'modified': '2026-10-30T09:00:00Z'}], 'pageBytes': 16384}
    elif path.startswith('/logs/'):
        before = int(query.get('before', [40000])[0])
        data = {'name': path.rsplit('/', 1)[1], 'size': 40000, 'start': max(0, before - 16384), 'end': before, 'text': log_text}
    elif path.startswith('/settings/'):
        data = server_settings[path.rsplit('/', 1)[1]]
        if request.method == 'PATCH':
            if any(data['values'][key] != value for key, value in body['expected'].items()):
                route.fulfill(status=409, json={'error': {'message': 'Settings changed. Reload before saving.'}})
                return
            data['values'].update(body['fields'])
    elif path == '/policies/securityranges/match':
        data = {'matched': policy_items['securityranges'][0] if policy_items['securityranges'] else None, 'note': 'Fixture native-selection result.'}
    elif path.startswith('/policies/'):
        parts = path.split('/')
        resource = parts[2]
        items = policy_items[resource]
        if len(parts) == 3 and request.method == 'GET':
            defaults = policy_defaults[resource]
            schema = [{'name': key, 'type': 'boolean' if isinstance(value, bool) else 'integer' if isinstance(value, int) else 'string',
                       'minimum': 0, 'maximum': 1 if isinstance(value, bool) else 10000 if isinstance(value, int) else 255, 'help': 'Fixture policy field.'} for key, value in defaults.items()]
            data = {'items': items, 'total': len(items), 'offset': 0, 'limit': 50, 'defaults': {'id': '0', **defaults}, 'schema': schema, 'idMaximum': 2147483647}
        elif len(parts) == 3:
            data = {'id': str(len(items) + 1), **body}
            items.append(data)
        else:
            item = next(value for value in items if value['id'] == parts[3])
            if body.get('expected') != item:
                route.fulfill(status=409, json={'error': {'message': 'Policy changed. Reload before saving.'}})
                return
            if request.method == 'DELETE':
                items.remove(item)
                data = {'deleted': True}
            else:
                item.update({key: value for key, value in body.items() if key != 'expected'})
                data = item
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
    elif path.endswith('/settings'):
        domain_settings.update(body)
        data = domain_settings
    elif '/names' in path:
        if path.endswith('/names'):
            if request.method == 'POST':
                data = {"id": 30, "domainId": 5, **body}
                domain_names.append(data)
            else:
                data = {"items": domain_names, "total": len(domain_names)}
        else:
            item = next(v for v in domain_names if v['id'] == int(path.rsplit('/', 1)[1]))
            domain_names.remove(item)
            data = {"deleted": True}
    elif path.endswith('/profile'):
        profile.update(body)
        data = profile
    elif path.endswith('/signature'):
        signature.update(body)
        data = signature
    elif path.endswith('/behavior'):
        behavior.update(body)
        data = behavior
    elif path == '/queue/wake':
        data = {"requested": True}
    elif path == '/queue':
        data = {"items": queue_items, "total": len(queue_items)}
    elif path.startswith('/queue/'):
        item = next(v for v in queue_items if v['id'] == path.split('/')[2])
        if request.method == 'DELETE':
            queue_items.remove(item)
            data = {"requested": True}
        else:
            data = item
    elif '/rules' in path:
        collection_path = path.split('/rules')[0] + '/rules'
        collection = rules.setdefault(collection_path, [])
        if path == collection_path:
            if request.method == 'POST':
                data = {"id": 40, "sortOrder": 1, **body}
                collection.append(data)
            else:
                data = {"items": collection, "total": len(collection)}
        else:
            item = next(v for v in collection if v['id'] == int(path.rsplit('/', 1)[1]))
            if request.method == 'DELETE':
                collection.remove(item)
                data = {"deleted": True}
            else:
                item.update(body)
                data = item
    elif path.startswith('/routes'):
        collection = route_addresses if '/addresses' in path else routes
        if path.endswith('/routes') or path.endswith('/addresses'):
            if request.method == 'POST':
                data = {"id": 35, **{k: v for k, v in body.items() if k != 'password'}}
                if collection is routes:
                    data['passwordConfigured'] = bool(body.get('password'))
                collection.append(data)
            else:
                data = {"items": collection, "total": len(collection)}
        else:
            item = next(v for v in collection if v['id'] == int(path.rsplit('/', 1)[1]))
            if request.method == 'DELETE':
                collection.remove(item)
                data = {"deleted": True}
            else:
                item.update({k: v for k, v in body.items() if k != 'password'})
                if 'password' in body:
                    item['passwordConfigured'] = bool(body['password'])
                data = item
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
        data = {"connected": True, "gtubeDetected": True, "headers": "X-Spam-Status: Yes, score=1000.0\n"}
    else:
        raise AssertionError(f"Unexpected browser API call: {request.method} {path}")
    route.fulfill(status=200, content_type='application/json', body=json.dumps({"data": data}))


with sync_playwright() as p:
    browser = p.chromium.launch(channel='msedge', headless=True)
    page = browser.new_page(viewport={"width": 1440, "height": 1000})
    errors = []
    page.on('pageerror', lambda error: errors.append(str(error)))
    # Expected 4xx responses log console errors, so they stay out of the page-error assertion.
    console = []
    page.on('console', lambda message: console.append(message.text) if message.type == 'error' else None)

    def visible(locator, what):
        # Fail with the page state, not only a locator timeout.
        try:
            locator.wait_for(timeout=15000)
        except Exception as error:
            page.screenshot(path=str(output / 'failure.png'), full_page=True)
            notice = page.locator('#notice').inner_text() if page.locator('#notice').is_visible() else '(no notice)'
            raise AssertionError(f'{what} did not appear. Hash {page.evaluate("location.hash")}; notice: {notice}; page errors: {errors}; console: {console[-5:]}; '
                                 f'content: {page.locator("#content").inner_text()[:600]}') from error
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
    page.get_by_text('SpamAssassin is reachable and recognized the GTUBE spam test message.', exact=True).wait_for()
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
    page.get_by_role('button', name='Advanced settings', exact=True).first.click()
    page.get_by_label('catch All Address', exact=True).fill('archive@example.com')
    page.get_by_label('plus Addressing Enabled', exact=True).check()
    page.get_by_label('max Accounts', exact=True).fill('100')
    page.get_by_label('signature Enabled', exact=True).check()
    page.get_by_label('signature Method', exact=True).select_option('3')
    page.get_by_label('signature Plain Text', exact=True).fill('Company\n%User.FirstName%')
    markup = '</textarea><img src=x onerror="window.domainMarkupExecuted=true">'
    page.get_by_label('signature Html', exact=True).fill(markup)
    before = len(requests)
    page.once('dialog', lambda dialog: dialog.dismiss())
    page.get_by_role('button', name='Save changes', exact=True).click()
    assert len(requests) == before, 'Cancelling domain-wide changes must not submit.'
    page.set_viewport_size({"width": 390, "height": 844})
    page.screenshot(path=str(output / 'advanced-domain-mobile.png'), full_page=True)
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Advanced settings overflow on mobile.'
    page.set_viewport_size({"width": 1440, "height": 1000})
    page.once('dialog', lambda dialog: dialog.accept())
    page.get_by_role('button', name='Save changes', exact=True).click()
    page.get_by_text('Advanced domain settings saved.', exact=True).wait_for()
    assert domain_settings['signatureMethod'] == 3 and domain_settings['maxAccounts'] == 100
    assert domain_settings['signaturePlainText'] == 'Company\r\n%User.FirstName%'
    assert 'greylistingEnabled' not in requests[-1][2], 'Omitted domain settings must be preserved.'
    page.get_by_role('button', name='Advanced settings', exact=True).first.click()
    assert page.get_by_label('signature Html', exact=True).input_value() == markup
    assert page.evaluate('typeof window.domainMarkupExecuted') == 'undefined'
    assert page.locator('img[src="x"]').count() == 0, 'Domain HTML source must not become DOM elements.'
    page.get_by_role('button', name='Cancel', exact=True).click()
    page.get_by_role('button', name='DKIM', exact=True).first.click()
    page.get_by_text('No keys have been generated for this domain.', exact=True).wait_for()
    page.get_by_label('New key selector', exact=True).fill('s2026')
    page.get_by_role('button', name='Generate key', exact=True).click()
    page.get_by_text('Key generated. Publish its DNS record, then activate it.', exact=True).wait_for()
    page.get_by_role('button', name='Activate this key', exact=True).click()
    page.get_by_text('The DNS record does not publish this key yet.', exact=True).wait_for()
    assert not dkim_state['keyConfigured'], 'Without DNS the key stays inactive.'
    page.get_by_label('Activate without checking DNS (the record must still be published before mail is signed)', exact=True).check()
    page.get_by_role('button', name='Activate this key', exact=True).click()
    page.get_by_text('Key s2026 is now active.', exact=True).wait_for()
    page.get_by_label('Sign outgoing mail', exact=True).check()
    page.get_by_role('button', name='Save DKIM settings', exact=True).click()
    page.get_by_text('DKIM settings saved.', exact=True).wait_for()
    assert [entry[2] for entry in dkim_requests if entry[0] == 'PATCH'][-1] == {'signEnabled': True, 'signAliases': False, 'headerCanonicalization': 2, 'bodyCanonicalization': 2, 'algorithm': 2}
    page.get_by_role('button', name='Alternate domains', exact=True).first.click()
    page.get_by_label('name', exact=True).fill('alternate.test')
    page.once('dialog', lambda dialog: dialog.dismiss())
    page.get_by_role('button', name='Save changes', exact=True).click()
    assert not domain_names, 'Cancelling alternate-domain activation must preserve the namespace.'
    page.once('dialog', lambda dialog: dialog.accept())
    page.get_by_role('button', name='Save changes', exact=True).click()
    page.get_by_text('Alternate domain added.', exact=True).wait_for()
    assert domain_names[0]['name'] == 'alternate.test'
    page.once('dialog', lambda dialog: dialog.dismiss())
    page.get_by_role('button', name='Remove alternate domain', exact=True).click()
    assert len(domain_names) == 1
    page.once('dialog', lambda dialog: dialog.accept())
    page.get_by_role('button', name='Remove alternate domain', exact=True).click()
    page.get_by_text('Alternate domain removed.', exact=True).wait_for()
    assert not domain_names
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
    page.get_by_role('button', name='Profile', exact=True).click()
    assert page.get_by_label('directory Backed', exact=True).count() == 0, 'Directory state must be read-only.'
    page.get_by_label('first Name', exact=True).fill('Ada')
    page.get_by_label('last Name', exact=True).fill('Team')
    page.get_by_role('button', name='Save changes', exact=True).click()
    page.get_by_text('Profile saved.', exact=True).wait_for()
    assert profile['firstName'] == 'Ada' and profile['lastName'] == 'Team'
    assert set(requests[-1][2]) == {'firstName', 'lastName'}
    page.get_by_role('button', name='Signature', exact=True).click()
    page.get_by_label('signature Enabled', exact=True).check()
    page.get_by_label('signature Plain Text', exact=True).fill('Regards\n%User.Address%')
    literal_html = '</textarea><img src=x onerror="window.signatureExecuted=true"><script>window.signatureExecuted=true</script>'
    page.get_by_label('signature Html', exact=True).fill('✓' * 6000)
    before = len(requests)
    page.get_by_role('button', name='Save changes', exact=True).click()
    page.get_by_text('Each signature format is limited to 16,000 UTF-8 bytes.', exact=True).wait_for()
    assert len(requests) == before, 'Oversized UTF-8 formats must fail before sending a request.'
    page.get_by_label('signature Plain Text', exact=True).fill('P' * 9000)
    page.get_by_label('signature Html', exact=True).fill('H' * 9000)
    page.get_by_role('button', name='Save changes', exact=True).click()
    page.get_by_text('This save exceeds the 16 KiB request limit. Shorten the signature or save each format separately.', exact=True).wait_for()
    assert len(requests) == before, 'Oversized signature saves must fail before sending a request.'
    page.get_by_label('signature Plain Text', exact=True).fill('Regards\n%User.Address%')
    page.get_by_label('signature Html', exact=True).fill(literal_html)
    before = len(requests)
    page.once('dialog', lambda dialog: dialog.dismiss())
    page.get_by_role('button', name='Save changes', exact=True).click()
    assert len(requests) == before, 'Cancelling signature activation must not write.'
    page.set_viewport_size({"width": 390, "height": 844})
    page.screenshot(path=str(output / 'mailbox-signature-mobile.png'), full_page=True)
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Signature controls overflow on mobile.'
    page.set_viewport_size({"width": 1440, "height": 1000})
    page.once('dialog', lambda dialog: dialog.accept())
    page.get_by_role('button', name='Save changes', exact=True).click()
    page.get_by_text('Signature saved.', exact=True).wait_for()
    assert signature['signatureHtml'] == literal_html and signature['signatureEnabled']
    assert signature['signaturePlainText'] == 'Regards\r\n%User.Address%', 'Plain-text edits must retain line breaks in the native HTML fallback.'
    signature['signatureHtml'] = literal_html.replace('><script>', '>\r\n<script>')
    page.get_by_role('button', name='Signature', exact=True).click()
    assert page.get_by_label('signature Html', exact=True).input_value() == signature['signatureHtml'].replace('\r\n', '\n')
    assert page.evaluate('typeof window.signatureExecuted') == 'undefined', 'Stored HTML must remain inert source text.'
    page.get_by_label('signature Enabled', exact=True).uncheck()
    page.get_by_role('button', name='Save changes', exact=True).click()
    page.get_by_text('Signature saved.', exact=True).wait_for()
    assert requests[-1][2] == {'signatureEnabled': False}, 'Signature PATCH preserves omitted formats.'
    assert signature['signatureHtml'] == literal_html.replace('><script>', '>\r\n<script>')
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
    page.get_by_role('button', name='External accounts', exact=True).click()
    page.get_by_text('No external accounts.', exact=True).wait_for()
    page.get_by_role('button', name='New external account', exact=True).click()
    page.locator('#content').get_by_label('Name', exact=True).fill('Old provider')
    page.locator('#content').get_by_label('POP3 server', exact=True).fill('pop.example.net')
    page.locator('#content').get_by_label('User name', exact=True).fill('remote')
    page.locator('#content').get_by_label('Password', exact=True).fill('fetch secret')
    page.locator('#content').get_by_label('Messages on the server', exact=True).select_option('days')
    page.locator('#content').get_by_label('Days to keep', exact=True).fill('14')
    assert page.locator('#content').get_by_label('Connection security', exact=True).locator('option').count() == 2, 'New fetch accounts offer only encrypted connections.'
    with page.expect_response(lambda response: response.url.endswith('/fetchaccounts') and response.request.method == 'POST') as fetch_created:
        page.get_by_role('button', name='Create external account', exact=True).click()
    fetch_body = fetch_created.value.request.post_data_json
    assert fetch_body['password'] == 'fetch secret' and fetch_body['daysToKeep'] == 14 and fetch_body['security'] == 1 and fetch_body['active'] is False
    page.get_by_text('External account created.', exact=True).wait_for()
    assert 'fetch secret' not in page.content(), 'The fetch password is not kept in the page.'
    page.get_by_role('button', name='Rules', exact=True).click()
    page.get_by_role('button', name='New rule', exact=True).click()
    page.get_by_label('name', exact=True).fill('Mailbox draft')
    page.get_by_label('value', exact=True).fill('mailbox-subject')
    page.get_by_role('button', name='Save draft', exact=True).click()
    page.get_by_text('Rule draft saved. Enable it separately when ready.', exact=True).wait_for()
    assert '/domains/5/accounts/10/rules' in rules, 'Mailbox rule requests must retain the parent route.'
    page.once('dialog', lambda dialog: dialog.accept())
    page.get_by_role('button', name='Delete rule', exact=True).click()
    page.get_by_text('Rule deleted.', exact=True).wait_for()
    page.get_by_role('link', name='Server settings', exact=True).click()
    page.get_by_label('Settings section', exact=True).select_option('pop3')
    limit = page.get_by_label('max Connections', exact=True)
    limit.wait_for()
    assert limit.input_value() == '7'
    limit.fill('9')
    before = len(requests)
    page.once('dialog', lambda dialog: dialog.dismiss())
    page.get_by_role('button', name='Save server settings', exact=True).click()
    assert len(requests) == before and server_settings['pop3']['values']['maxConnections'] == 7
    server_settings['pop3']['values']['maxConnections'] = 12
    page.once('dialog', lambda dialog: dialog.accept())
    page.get_by_role('button', name='Save server settings', exact=True).click()
    page.get_by_text('Settings changed. Reload before saving.', exact=True).wait_for()
    assert server_settings['pop3']['values']['maxConnections'] == 12
    page.get_by_role('button', name='Refresh', exact=True).click()
    page.get_by_label('Settings section', exact=True).select_option('pop3')
    limit = page.get_by_label('max Connections', exact=True)
    limit.wait_for()
    assert limit.input_value() == '12'
    limit.fill('13')
    page.once('dialog', lambda dialog: dialog.accept())
    with page.expect_response(lambda response: response.url.endswith('/settings/pop3') and response.request.method == 'PATCH') as saved:
        page.get_by_role('button', name='Save server settings', exact=True).click()
    assert saved.value.status == 200
    assert saved.value.request.post_data_json == {'fields': {'maxConnections': 13}, 'expected': {'maxConnections': 12}}
    page.get_by_text('Server settings saved.', exact=True).wait_for()
    assert page.get_by_label('max Connections', exact=True).input_value() == '13'
    page.set_viewport_size({'width': 390, 'height': 844})
    page.screenshot(path=str(output / 'server-settings-mobile.png'), full_page=True)
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Server settings overflow on mobile.'
    page.get_by_label('Settings section', exact=True).select_option('passwordhash')
    page.get_by_label('algorithm', exact=True).fill('2')
    page.get_by_label('iterations', exact=True).fill('0')
    page.once('dialog', lambda dialog: dialog.accept())
    with page.expect_response(lambda response: response.url.endswith('/settings/passwordhash') and response.request.method == 'PATCH') as hash_saved:
        page.get_by_role('button', name='Save server settings', exact=True).click()
    assert hash_saved.value.request.post_data_json == {'fields': {'algorithm': 2, 'iterations': 0}, 'expected': {'algorithm': 1, 'iterations': 2}}
    page.get_by_text('Server settings saved.', exact=True).wait_for()
    assert server_settings['passwordhash']['values']['autoUpgrade'] is True
    page.get_by_label('Settings section', exact=True).select_option('logging')
    page.get_by_label('smtp', exact=True).check()
    page.get_by_label('pop3', exact=True).check()
    page.once('dialog', lambda dialog: dialog.accept())
    with page.expect_response(lambda response: response.url.endswith('/settings/logging') and response.request.method == 'PATCH') as logs_saved:
        page.get_by_role('button', name='Save server settings', exact=True).click()
    assert logs_saved.value.request.post_data_json == {'fields': {'smtp': True, 'pop3': True}, 'expected': {'smtp': False, 'pop3': False}}
    page.get_by_text('Server settings saved.', exact=True).wait_for()
    assert server_settings['logging']['values']['debug'] is True
    page.get_by_label('Settings section', exact=True).select_option('smtp')
    security = page.get_by_label('delivery Security', exact=True)
    visible(security, 'The SMTP delivery security select')
    assert security.input_value() == '2' and security.locator('option').count() == 3, 'Native options render as a select.'
    page.get_by_text('No credential is stored.', exact=True).wait_for()
    page.get_by_label('New credential', exact=True).fill('relay secret')
    with page.expect_response(lambda response: response.url.endswith('/settings/smtp/smart-host-credential')) as stored:
        page.get_by_role('button', name='Store credential', exact=True).click()
    assert stored.value.request.method == 'PUT' and stored.value.request.post_data_json == {'password': 'relay secret'}
    page.get_by_text('Smart host credential stored.', exact=True).wait_for()
    page.get_by_text('A credential is stored. It is never displayed.', exact=True).wait_for()
    assert page.get_by_label('New credential', exact=True).input_value() == '' and 'relay secret' not in page.content()
    page.get_by_label('delivery Security', exact=True).select_option('3')
    page.once('dialog', lambda dialog: dialog.accept())
    with page.expect_response(lambda response: response.url.endswith('/settings/smtp') and response.request.method == 'PATCH') as smtp_saved:
        page.get_by_role('button', name='Save server settings', exact=True).click()
    assert smtp_saved.value.request.post_data_json == {'fields': {'deliverySecurity': 3}, 'expected': {'deliverySecurity': 2}}
    page.get_by_text('Server settings saved.', exact=True).wait_for()
    page.once('dialog', lambda dialog: dialog.accept())
    page.get_by_role('button', name='Clear credential', exact=True).click()
    page.get_by_text('Smart host credential cleared.', exact=True).wait_for()
    assert server_settings['smtp']['credentials']['smartHostConfigured'] is False
    page.get_by_label('Settings section', exact=True).select_option('imap')
    page.get_by_label('hierarchy Delimiter', exact=True).select_option('/')
    page.get_by_label('master User', exact=True).fill('master')
    before = len(requests)
    page.get_by_role('button', name='Save server settings', exact=True).click()
    page.get_by_text('Save hierarchy Delimiter on its own, then the other changes.', exact=True).wait_for()
    assert len(requests) == before, 'A direct setting is never sent together with other fields.'
    page.get_by_label('master User', exact=True).fill('')
    page.once('dialog', lambda dialog: dialog.accept())
    with page.expect_response(lambda response: response.url.endswith('/settings/imap') and response.request.method == 'PATCH') as imap_saved:
        page.get_by_role('button', name='Save server settings', exact=True).click()
    assert imap_saved.value.request.post_data_json == {'fields': {'hierarchyDelimiter': '/'}, 'expected': {'hierarchyDelimiter': '.'}}
    page.get_by_text('Server settings saved.', exact=True).wait_for()
    page.get_by_role('link', name='Server status', exact=True).click()
    page.get_by_text('SMTP SESSIONS', exact=True).wait_for()
    before = len(requests)
    page.once('dialog', lambda dialog: dialog.dismiss())
    page.get_by_role('button', name='Stop mail services', exact=True).click()
    assert len(requests) == before, 'Dismissing the stop confirmation sends nothing.'
    page.once('dialog', lambda dialog: dialog.accept())
    with page.expect_response(lambda response: response.url.endswith('/server/stop')) as stopped:
        page.get_by_role('button', name='Stop mail services', exact=True).click()
    assert stopped.value.request.post_data_json == {'expectedState': 3}
    page.get_by_text('Mail services stopped.', exact=True).wait_for()
    with page.expect_response(lambda response: response.url.endswith('/server/start')) as started:
        page.get_by_role('button', name='Start mail services', exact=True).click()
    assert started.value.request.post_data_json == {'expectedState': 1}
    page.get_by_text('Mail services started.', exact=True).wait_for()
    page.get_by_role('link', name='Server messages', exact=True).click()
    page.get_by_text('VIRUS_FOUND', exact=True).wait_for()
    assert page.get_by_label('Text').nth(1).input_value() == '<b>inert</b> text' and page.locator('#content b').count() == 0
    page.get_by_label('Text').first.fill('Virus removed by policy')
    with page.expect_response(lambda response: '/servermessages/1' in response.url) as message_saved:
        page.get_by_role('button', name='Save message', exact=True).first.click()
    assert message_saved.value.request.post_data_json == {'expected': {'id': '1', 'name': 'VIRUS_FOUND', 'text': 'Virus found'}, 'text': 'Virus removed by policy'}
    page.get_by_text('Server message saved.', exact=True).wait_for()
    page.get_by_role('link', name='Administrator sendout', exact=True).click()
    send = page.get_by_role('button', name='Send message', exact=True)
    send.wait_for()
    assert send.is_disabled(), 'Sending needs a recipient preview first.'
    page.get_by_label('recipients', exact=True).fill('*@example.com')
    page.get_by_role('button', name='Preview recipients', exact=True).click()
    page.get_by_text('2 account(s) match.', exact=True).wait_for()
    page.get_by_label('from Address', exact=True).fill('admin@example.com')
    page.get_by_label('subject', exact=True).fill('Maintenance')
    page.get_by_label('body', exact=True).fill('Tonight at 22:00.')
    page.once('dialog', lambda dialog: dialog.accept())
    send.click()
    page.get_by_text('Message queued to 2 account(s).', exact=True).wait_for()
    assert sendout_requests[-1] == {'recipients': '*@example.com', 'fromAddress': 'admin@example.com', 'fromName': 'Server administrator',
        'subject': 'Maintenance', 'body': 'Tonight at 22:00.', 'expectedRecipients': 2}
    page.get_by_label('recipients', exact=True).fill('*@other.example')
    assert send.is_disabled(), 'Changing the pattern requires a new preview.'
    page.get_by_role('link', name='Logs', exact=True).click()
    page.get_by_role('button', name='View', exact=True).click()
    page.get_by_text('Bytes 23616–40000 of 40000.', exact=True).wait_for()
    assert '<script>window.logInjected' in page.locator('pre.log-text').inner_text()
    assert page.evaluate('window.logInjected === undefined'), 'Log text renders inertly.'
    with page.expect_response(lambda response: 'before=23616' in response.url) as older:
        page.get_by_role('button', name='Older', exact=True).click()
    assert older.value.status == 200
    page.set_viewport_size({'width': 390, 'height': 844})
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Logs overflow on mobile.'
    page.screenshot(path=str(output / 'logs-mobile.png'), full_page=True)
    page.set_viewport_size({'width': 1440, 'height': 1000})
    page.get_by_role('link', name='Diagnostics').click()
    page.get_by_text('No diagnostics have run since the service started.', exact=True).wait_for()
    page.get_by_role('button', name='Run diagnostics', exact=True).click()
    page.get_by_text('Outbound port', exact=True).wait_for()
    assert page.locator('#content img').count() == 0, 'Diagnostic details render inertly.'
    page.get_by_label('Domain', exact=True).fill('example.net')
    with page.expect_response(lambda response: response.url.endswith('/diagnostics/mail-servers')) as looked_up:
        page.get_by_role('button', name='Look up mail servers', exact=True).click()
    assert looked_up.value.request.post_data_json == {'domain': 'example.net'}
    page.get_by_text('mx.example.net', exact=True).wait_for()
    page.get_by_role('link', name='Groups and public folders', exact=True).click()
    page.get_by_text('Sales', exact=True).wait_for()
    page.get_by_label('New group name', exact=True).fill('Support')
    with page.expect_response(lambda response: response.url.endswith('/groups') and response.request.method == 'POST') as group_created:
        page.get_by_role('button', name='Create group', exact=True).click()
    assert group_created.value.request.post_data_json == {'name': 'Support'}
    page.get_by_text('Group created.', exact=True).wait_for()
    page.get_by_label('New folder name', exact=True).fill('Projects')
    page.get_by_label('Inside', exact=True).select_option('7')
    with page.expect_response(lambda response: response.url.endswith('/publicfolders') and response.request.method == 'POST') as folder_created:
        page.get_by_role('button', name='Create folder', exact=True).click()
    assert folder_created.value.request.post_data_json == {'name': 'Projects', 'parentId': 7}
    page.get_by_text('Public folder created.', exact=True).wait_for()
    page.get_by_role('button', name='Permissions', exact=True).first.click()
    page.get_by_text('No permissions: only the server can see this folder.', exact=True).wait_for()
    page.get_by_label('Grant to', exact=True).select_option('1')
    page.get_by_label('Mailbox address or group name', exact=True).fill('sales')
    with page.expect_response(lambda response: response.url.endswith('/publicfolders/7/permissions') and response.request.method == 'POST') as granted:
        page.get_by_role('button', name='Add permission', exact=True).click()
    assert granted.value.request.post_data_json == {'type': 1, 'rights': {'lookup': True, 'read': True}, 'groupId': 1}
    page.get_by_role('link', name='Backup and maintenance', exact=True).click()
    page.get_by_text('HMBackup 2026-10-30 090000.7z', exact=True).wait_for()
    page.get_by_role('button', name='Restore…', exact=True).click()
    page.get_by_label('Settings', exact=True).check()
    page.get_by_label('Archive passphrase (if encrypted)', exact=True).fill('archive secret')
    page.get_by_label('Type the archive name to confirm', exact=True).fill('HMBackup 2026-10-30 090000.7z')
    page.get_by_role('button', name='Restore', exact=True).click()
    page.get_by_text('Restore started. See hmailserver_backup.log for progress.', exact=True).wait_for()
    assert restore_requests[-1] == {'name': 'HMBackup 2026-10-30 090000.7z', 'confirmName': 'HMBackup 2026-10-30 090000.7z', 'settings': True, 'domains': False, 'messages': False, 'passphrase': 'archive secret'}
    assert page.get_by_label('Archive passphrase (if encrypted)', exact=True).input_value() == '', 'The archive passphrase field is cleared.'
    page.get_by_label('Export passphrase', exact=True).fill('export passphrase')
    page.get_by_role('button', name='Export key', exact=True).click()
    page.get_by_text('Key exported. Copy it to a safe place.', exact=True).wait_for()
    assert page.get_by_label('Export passphrase', exact=True).input_value() == ''
    page.set_viewport_size({'width': 390, 'height': 844})
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Maintenance overflows on mobile.'
    page.set_viewport_size({'width': 1440, 'height': 1000})
    page.get_by_role('link', name='Server settings', exact=True).click()
    page.get_by_label('Settings section', exact=True).select_option('antivirus')
    page.get_by_role('button', name='Test ClamAV', exact=True).click()
    page.get_by_text('ClamAV detected the test virus.', exact=True).wait_for()
    assert page.locator('#content b').count() == 0, 'Scanner output renders inertly.'
    page.get_by_label('Settings section', exact=True).select_option('backup')
    page.get_by_text('No passphrase is set, so new backups are not encrypted. Use at least 12 characters.', exact=True).wait_for()
    page.get_by_label('New passphrase', exact=True).fill('backup passphrase')
    with page.expect_response(lambda response: response.url.endswith('/settings/backup/passphrase')) as stored_passphrase:
        page.get_by_role('button', name='Store passphrase', exact=True).click()
    assert stored_passphrase.value.request.post_data_json == {'passphrase': 'backup passphrase'}
    page.get_by_text('Backup passphrase stored.', exact=True).wait_for()
    page.get_by_role('link', name='Filtering and relay policies', exact=True).click()
    for resource in ['surbl', 'blockedattachments', 'incomingrelays', 'securityranges']:
        page.get_by_label('Policy collection', exact=True).select_option(resource)
        page.get_by_role('button', name='Add policy', exact=True).click()
        values = {'surbl': {'zone': 'uri.example.invalid', 'score': '3', 'reject Message': 'Provider match'},
                  'blockedattachments': {'pattern': '*.blocked-fixture', 'description': 'Fixture attachment'},
                  'incomingrelays': {'name': 'Trusted fixture', 'lower Address': '192.0.2.10', 'upper Address': '192.0.2.20'},
                  'securityranges': {'name': 'IP fixture', 'lower Address': '192.0.2.10', 'upper Address': '192.0.2.20', 'priority': '100'}}[resource]
        for key, value in values.items():
            page.get_by_label(key, exact=True).fill(value)
        before = len(requests)
        page.once('dialog', lambda dialog: dialog.dismiss())
        page.get_by_role('button', name='Save changes', exact=True).click()
        assert len(requests) == before and not policy_items[resource], 'Cancelled policy creation must not write.'
        page.once('dialog', lambda dialog: dialog.accept())
        with page.expect_response(lambda response: response.url.endswith('/policies/' + resource) and response.request.method == 'POST'):
            page.get_by_role('button', name='Save changes', exact=True).click()
        page.get_by_text('Policy saved.', exact=True).wait_for()
        assert len(policy_items[resource]) == 1
        if resource == 'securityranges':
            page.get_by_label('address', exact=True).fill('192.0.2.15')
            page.get_by_role('button', name='Inspect matching IP policy', exact=True).click()
            page.get_by_text('IP fixture (priority 100). Fixture native-selection result.', exact=True).wait_for()
        page.get_by_role('button', name='Edit policy', exact=True).click()
        field = 'score' if resource == 'surbl' else 'description' if resource == 'blockedattachments' else 'name'
        page.get_by_label(field, exact=True).fill('7' if field == 'score' else 'Changed fixture')
        page.once('dialog', lambda dialog: dialog.accept())
        with page.expect_response(lambda response: '/policies/' + resource + '/' in response.url and response.request.method == 'PATCH') as changed:
            page.get_by_role('button', name='Save changes', exact=True).click()
        assert set(changed.value.request.post_data_json) == {field, 'expected'}, 'Policy patches preserve omitted fields and carry their snapshot.'
        page.get_by_text('Policy saved.', exact=True).wait_for()
        page.get_by_role('button', name='Delete policy', exact=True).wait_for()
        page.once('dialog', lambda dialog: dialog.accept())
        page.get_by_role('button', name='Delete policy', exact=True).click()
        page.get_by_text('Policy deleted.', exact=True).wait_for()
        assert not policy_items[resource]
    page.screenshot(path=str(output / 'policy-collections-mobile.png'), full_page=True)
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Policy controls overflow on mobile.'
    page.set_viewport_size({'width': 1440, 'height': 1000})
    page.get_by_role('link', name='Domains & DNS').click()
    page.get_by_role('button', name='Mailboxes', exact=True).first.click()
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
    page.set_viewport_size({"width": 1440, "height": 1000})
    page.get_by_role('link', name='Delivery queue').click()
    page.get_by_role('button', name='Inspect message', exact=True).first.click()
    page.get_by_text('recipient@example.net', exact=True).wait_for()
    assert page.get_by_role('button', name='Retry message', exact=True).nth(1).is_disabled()
    assert page.get_by_role('button', name='Remove message', exact=True).nth(1).is_disabled()
    before = len(requests)
    page.once('dialog', lambda dialog: dialog.dismiss())
    page.get_by_role('button', name='Remove message', exact=True).first.click()
    assert len(requests) == before, 'Cancelling queue removal must not write.'
    page.once('dialog', lambda dialog: dialog.accept())
    page.get_by_role('button', name='Retry message', exact=True).first.click()
    page.get_by_text('Message retry requested.', exact=True).wait_for()
    assert requests[-2][1] == '/queue/9007199254740993/retry', 'Queue IDs must retain every digit in browser requests.'
    page.once('dialog', lambda dialog: dialog.accept())
    page.get_by_role('button', name='Remove message', exact=True).first.click()
    page.get_by_text('Queue message removed.', exact=True).wait_for()
    assert len(queue_items) == 1
    page.get_by_role('button', name='Check for due mail', exact=True).click()
    page.get_by_text('Delivery check requested.', exact=True).wait_for()
    page.get_by_role('link', name='Routing').click()
    page.get_by_role('button', name='New route', exact=True).click()
    assert page.get_by_label('security', exact=True).input_value() == '3'
    assert not page.get_by_label('all Addresses', exact=True).is_checked()
    page.get_by_label('domain', exact=True).fill('relay.example.net')
    page.get_by_label('host', exact=True).fill('smtp.example.net')
    page.get_by_label('requires Auth', exact=True).check()
    page.get_by_label('username', exact=True).fill('relay-user')
    page.get_by_label('password', exact=True).fill('write-only-fixture')
    assert page.get_by_label('password', exact=True).get_attribute('type') == 'password'
    page.once('dialog', lambda dialog: dialog.accept())
    page.get_by_role('button', name='Save changes', exact=True).click()
    page.get_by_text('Route saved.', exact=True).wait_for()
    page.get_by_role('button', name='Edit route', exact=True).click()
    assert page.get_by_label('password', exact=True).input_value() == ''
    page.get_by_label('description', exact=True).fill('Updated relay')
    page.once('dialog', lambda dialog: dialog.accept())
    with page.expect_response(lambda response: urlsplit(response.url).path == '/api/v1/routes/35' and response.request.method == 'PATCH') as route_update:
        page.get_by_role('button', name='Save changes', exact=True).click()
    page.get_by_text('Route saved.', exact=True).wait_for()
    assert route_update.value.request.post_data_json == {'description': 'Updated relay'}, 'Unchanged route credentials and TLS must be omitted.'
    page.get_by_role('button', name='Route recipients', exact=True).click()
    page.get_by_label('address', exact=True).fill('allowed@example.net')
    page.once('dialog', lambda dialog: dialog.accept())
    page.get_by_role('button', name='Save changes', exact=True).click()
    page.get_by_text('Route recipient added.', exact=True).wait_for()
    page.once('dialog', lambda dialog: dialog.accept())
    page.get_by_role('button', name='Remove route recipient', exact=True).click()
    page.get_by_text('Route recipient removed.', exact=True).wait_for()
    page.once('dialog', lambda dialog: dialog.accept())
    page.get_by_role('button', name='Delete route', exact=True).click()
    page.get_by_text('Route deleted.', exact=True).wait_for()
    page.get_by_role('link', name='Global rules').click()
    page.get_by_role('button', name='New rule', exact=True).click()
    page.get_by_label('name', exact=True).fill('Controlled rule')
    page.get_by_label('value', exact=True).fill('subject-fixture')
    page.get_by_label('type', exact=True).select_option('7')
    page.get_by_label('header', exact=True).last.fill('X-Processed')
    literal_rule = '<img src=x onerror="window.ruleExecuted=true">'
    page.get_by_label('value', exact=True).last.fill(literal_rule)
    page.get_by_role('button', name='Add action', exact=True).click()
    page.get_by_role('button', name='Move action up', exact=True).last.click()
    page.set_viewport_size({"width": 390, "height": 844})
    page.screenshot(path=str(output / 'rule-builder-mobile.png'), full_page=True)
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Rule builder overflows on mobile.'
    page.get_by_role('button', name='Save draft', exact=True).click()
    page.get_by_text('Rule draft saved. Enable it separately when ready.', exact=True).wait_for()
    assert rules['/rules'][0]['active'] is False
    assert [v['type'] for v in rules['/rules'][0]['actions']] == [6, 7]
    page.once('dialog', lambda dialog: dialog.dismiss())
    page.get_by_role('button', name='Enable rule', exact=True).click()
    assert not rules['/rules'][0]['active']
    page.once('dialog', lambda dialog: dialog.accept())
    page.get_by_role('button', name='Enable rule', exact=True).click()
    page.get_by_role('button', name='Disable rule', exact=True).wait_for()
    assert page.get_by_role('button', name='Edit rule', exact=True).count() == 0
    page.once('dialog', lambda dialog: dialog.accept())
    page.get_by_role('button', name='Disable rule', exact=True).click()
    page.get_by_role('button', name='Edit rule', exact=True).click()
    assert page.get_by_label('value', exact=True).last.input_value() == literal_rule
    assert page.evaluate('typeof window.ruleExecuted') == 'undefined'
    page.get_by_role('button', name='Cancel', exact=True).click()
    page.once('dialog', lambda dialog: dialog.accept())
    page.get_by_role('button', name='Delete rule', exact=True).click()
    page.get_by_text('Rule deleted.', exact=True).wait_for()
    page.get_by_role('link', name='Domains & DNS').click()
    page.get_by_role('button', name='Mailboxes', exact=True).wait_for()
    user = {"address": "manager@example.com", "level": 1, "domainId": 5, "accountId": 11}
    mailboxes.append({"id": 12, "domainId": 5, "address": "server@example.com", "active": True, "maxSizeMB": 10, "level": 2})
    page.reload()
    page.get_by_role('button', name='Mailboxes', exact=True).wait_for()
    for title in ['Delivery queue', 'Routing', 'Global rules', 'Server settings', 'Filtering and relay policies', 'Server status', 'Server messages', 'Administrator sendout', 'Logs', 'Groups and public folders', 'Backup and maintenance']:
        assert not page.get_by_role('link', name=title).is_visible(), 'Server operations must be hidden from domain administrators.'
    assert page.get_by_role('button', name='New domain', exact=True).count() == 0
    assert page.get_by_role('button', name='Delete domain', exact=True).count() == 0
    page.get_by_role('button', name='Edit domain', exact=True).click()
    assert page.get_by_label('max Account Size MB', exact=True).count() == 0
    page.get_by_role('button', name='Advanced settings', exact=True).click()
    assert page.get_by_label('max Accounts', exact=True).count() == 0, 'Only server administrators may set object limits.'
    page.get_by_role('button', name='Cancel', exact=True).click()
    page.get_by_role('button', name='Alternate domains', exact=True).click()
    assert page.get_by_label('name', exact=True).count() == 0, 'Domain administrators cannot claim global names.'
    page.get_by_role('button', name='Mailboxes', exact=True).click()
    page.get_by_text('server@example.com', exact=True).wait_for()
    assert page.get_by_role('button', name='Edit mailbox', exact=True).count() == 0
    assert page.get_by_role('button', name='Signature', exact=True).count() == 0
    assert page.get_by_role('button', name='Profile', exact=True).count() == 0
    assert page.get_by_role('button', name='Rules', exact=True).count() == 0
    assert page.get_by_role('button', name='Delete mailbox', exact=True).count() == 0
    page.get_by_role('button', name='New mailbox', exact=True).click()
    assert page.get_by_label('level', exact=True).locator('option[value="2"]').count() == 0
    page.screenshot(path=str(output / 'domain-admin-mobile.png'), full_page=True)
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Domain administrator editor overflows on mobile.'
    user = {"address": "mailbox@example.com", "level": 0, "domainId": 5, "accountId": 10}
    page.goto(base + '/#self')
    page.reload()
    page.get_by_role('button', name='My profile', exact=True).click()
    page.get_by_label('first Name', exact=True).fill('Own mailbox profile')
    page.get_by_role('button', name='Save changes', exact=True).click()
    page.get_by_text('Profile saved.', exact=True).wait_for()
    assert requests[-1][0:2] == ('PATCH', '/self/profile')
    assert profile['firstName'] == 'Own mailbox profile'
    page.get_by_role('button', name='My rules', exact=True).click()
    page.get_by_role('button', name='New rule', exact=True).click()
    page.get_by_label('name', exact=True).fill('Own mailbox draft')
    page.get_by_label('value', exact=True).fill('own-message')
    page.get_by_role('button', name='Save draft', exact=True).click()
    page.get_by_text('Own mailbox draft', exact=True).wait_for()
    assert '/self/rules' in rules and not rules['/self/rules'][0]['active']
    page.get_by_role('button', name='Change my password', exact=True).click()
    page.get_by_label('Current password', exact=True).fill('fixture-current')
    page.get_by_label('New password', exact=True).fill('fixture-new')
    page.get_by_label('Confirm new password', exact=True).fill('different')
    before_password = len(requests)
    page.get_by_role('button', name='Change password and sign out', exact=True).click()
    page.get_by_text('The new passwords do not match.', exact=True).wait_for()
    assert len(requests) == before_password
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Mailbox self-service overflows on mobile.'
    page.screenshot(path=str(output / 'mailbox-self-service-mobile.png'), full_page=True)
    page.get_by_label('Confirm new password', exact=True).fill('fixture-new')
    page.get_by_role('button', name='Change password and sign out', exact=True).click()
    page.get_by_role('heading', name='Sign in to your server').wait_for()
    assert requests[-1][0:2] == ('POST', '/self/password')
    user = {"address": "manager@example.com", "level": 1, "domainId": 5, "accountId": 11}
    page.goto(base + '/#domains')
    page.reload()
    page.get_by_role('button', name='Mailboxes', exact=True).wait_for()
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
    print('Passed browser queue precision/busy/confirmation, routing secrets/recipients, rule drafts/order/activation/source safety, role controls, navigation, domain/mailbox/alias/list/member CRUD, forwarding/replies, signatures, DNSBL, SpamAssassin, DNS guidance, session recovery and mobile layout checks.')
