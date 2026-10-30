'use strict';
(() => {
  const $ = (id) => document.getElementById(id);
  let csrf = '', user = null, currentPage = '', generation = 0, fieldSequence = 0;
  const pages = {
    overview: ['Overview', 'Your mail infrastructure, at a glance.'],
    domains: ['Domains & DNS', 'Manage domains, mailboxes, storage limits and authentication records.'],
    antispam: ['Spam protection', 'Fine-tune filtering, authentication checks, and your SpamAssassin connection.'],
    dnsbl: ['DNS blocklists', 'Your providers. Your policy. Manage multiple lists without vendor restrictions.'],
    diagnostics: ['Diagnostics', 'Check the administration runtime, deployment and server configuration.'],
    queue: ['Delivery queue', 'Inspect pending mail and request a retry or removal.'],
    routes: ['Routing', 'Manage next-hop delivery and allowed recipients.'],
    rules: ['Global rules', 'Build disabled drafts, then validate and enable processing rules.'],
    settings: ['Server settings', 'Configure protocols, security policy, performance and existing scanners.'],
    policies: ['Filtering & relays', 'Manage URI blocklists, attachment rules and explicitly trusted incoming relays.'],
    self: ['My mailbox', 'Manage your own profile, signature, forwarding, replies, rules and password.'],
    transport: ['Mail transport', 'Inspect configured mail listeners and certificate registrations.'],
    server: ['Server status', 'Start or stop mail services and watch sessions and counters.'],
    messages: ['Server messages', 'Edit the texts used in server-generated notifications and replies.'],
    sendout: ['Administrator sendout', 'Send one message to all or matching accounts.'],
    logs: ['Logs', 'Read the server log files.'],
    folders: ['Groups & public folders', 'Manage groups, shared public folders and who may use them.'],
    maintenance: ['Backup & maintenance', 'Back up and restore the server, and move its installation key.'],
  };
  function el(tag, text, className) {
    const node = document.createElement(tag);
    if (text !== undefined && text !== null) node.textContent = String(text);
    if (className) node.className = className;
    return node;
  }
  function notify(message, error = false) {
    $('notice').textContent = message;
    $('notice').className = `alert ${error ? 'alert-danger' : 'alert-success'}`;
    $('notice').hidden = false;
  }
  async function request(path, method = 'GET', body = {}) {
    const options = { method, credentials: 'same-origin', headers: { Accept: 'application/json' } };
    if (method !== 'GET') {
      options.headers['Content-Type'] = 'application/json';
      options.headers['X-CSRF-Token'] = csrf;
      options.body = JSON.stringify(body);
    }
    const response = await fetch(`api/v1${path}`, options);
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) {
      if (response.status === 401 && path !== '/session') {
        user = null; ++generation; showIdentity();
        // Cookie challenges can be empty. Refresh the anonymous token after revocation so
        // the sign-in form does not keep a token bound to the former identity.
        try { const session = await request('/session'); csrf = session.csrf; } catch { csrf = ''; }
      }
      throw new Error(payload.error?.message || `Request failed (${response.status}).`);
    }
    return payload.data;
  }
  function button(text, action, style = 'btn btn-outline-secondary btn-sm') {
    const node = el('button', text, style); node.type = 'button';
    node.addEventListener('click', async () => {
      node.disabled = true;
      try { await action(); } catch (error) { notify(error.message, true); }
      finally { node.disabled = false; }
    });
    return node;
  }
  function card(title) {
    const outer = el('article', null, 'card'); const body = el('div', null, 'card-body');
    outer.append(body); if (title) body.append(el('h2', title));
    return { outer, body };
  }
  function pill(text, kind = '') { return el('span', text, `pill ${kind}`); }
  function showIdentity() {
    $('startup').hidden = true;
    $('login-view').hidden = !!user;
    $('panel-view').hidden = !user;
    if (user) {
      $('identity').textContent = user.address;
      document.querySelectorAll('[data-server]').forEach(n => { n.hidden = user.level !== 2; });
      document.querySelectorAll('[data-page=domains]').forEach(n => { n.hidden = user.level < 1; });
      document.querySelectorAll('[data-mailbox]').forEach(n => { n.hidden = !(user.accountId > 0); });
    }
  }
  async function overview(root) {
    if (user.level !== 2) {
      const c = card('Your domain workspace');
      c.body.append(el('p', user.level === 1 ? 'Manage your mailboxes and inspect domain authentication records.' : 'Manage your profile, forwarding, signatures and rules from My mailbox.', 'text-secondary'));
      root.append(c.outer); return;
    }
    const status = await request('/status');
    const metrics = el('div', null, 'grid');
    const state = ['Unknown', 'Stopped', 'Starting', 'Running', 'Stopping'][status.state] || `State ${status.state}`;
    [['SERVER STATUS', state], ['CONFIGURED DOMAINS', status.domains], ['SERVER VERSION', status.version]].forEach(([label, value]) => {
      const c = card(); c.body.append(el('div', label, 'metric-label'), el('div', value, 'metric')); metrics.append(c.outer);
    });
    root.append(metrics);
    const c = card('A stronger foundation for your mail'); c.outer.classList.add('section-gap');
    c.body.append(el('p', 'Manage spam protection, choose your blocklist providers, and inspect SPF, DKIM and DMARC from one workspace.', 'text-secondary'));
    c.body.append(el('p', 'Preview scope: domains, mailboxes, aliases, distribution lists, DNS inspection, spam settings and DNSBL management. Other administration functions are still being migrated.', 'help'));
    root.append(c.outer);
  }
  function inputField(name, value, help = '') {
    const wrap = el('div'); const label = el('label', name.replace(/([a-z])([A-Z])/g, '$1 $2'), 'form-label');
    const input = el(['autoReplyBody', 'signaturePlainText', 'signatureHtml', 'cipherList'].includes(name) ? 'textarea' : ['level', 'mode', 'signatureMethod', 'security'].includes(name) ? 'select' : 'input'); input.name = name; input.id = `field-${name}-${++fieldSequence}`; label.htmlFor = input.id;
    if (['level', 'mode', 'signatureMethod', 'security'].includes(name)) {
      input.className = 'form-select';
      const choices = name === 'security' ? ['Plaintext', 'Implicit TLS', 'STARTTLS optional', 'STARTTLS required'] : name === 'signatureMethod' ? ['Use domain format when mailbox format is empty', 'Replace mailbox signature', 'Append to mailbox signature'] : name === 'mode' ? ['Anyone (public)', 'List members', 'Specified sender (announcements)', 'Domain senders'] : ['Mailbox user', 'Domain administrator', 'Server administrator'];
      choices.forEach((title, level) => {
        if (name === 'level' && level > (user.level === 2 ? 2 : 1)) return;
        const option = el('option', title); option.value = String(level + (name === 'signatureMethod' ? 1 : 0)); input.append(option);
      }); input.value = String(value); wrap.append(label, input);
      return { wrap, input };
    }
    if (typeof value === 'boolean') {
      wrap.className = 'form-check'; input.type = 'checkbox'; input.className = 'form-check-input'; input.checked = value;
      label.className = 'form-check-label'; wrap.append(input, label);
    } else {
      if (input.tagName !== 'TEXTAREA') input.type = typeof value === 'number' ? 'number' : 'text';
      input.className = 'form-control'; input.value = String(value);
      if (input.type === 'number') { input.min = name.endsWith('Port') ? '1' : '0'; input.max = name.endsWith('Port') ? '65535' : '2147483647'; input.step = '1'; }
      else input.maxLength = 255;
      wrap.append(label, input);
    }
    if (help) wrap.append(el('p', help, 'help mt-2 mb-0'));
    return { wrap, input };
  }
  // A select for native schema options. An unexpected stored value stays selectable so that
  // saving other fields never silently replaces it.
  function optionField(rule, value, help) {
    const wrap = el('div'); const label = el('label', rule.name.replace(/([a-z])([A-Z])/g, '$1 $2'), 'form-label');
    const input = el('select', null, 'form-select'); input.name = rule.name; input.id = `field-${rule.name}-${++fieldSequence}`; label.htmlFor = input.id;
    const options = rule.options.some(option => option.value === value) ? rule.options : [...rule.options, { value, label: `Current value (${value})` }];
    for (const option of options) { const node = el('option', option.label); node.value = String(option.value); input.append(node); }
    input.value = String(value); wrap.append(label, input);
    if (help) wrap.append(el('p', help, 'help mt-2 mb-0'));
    return { wrap, input };
  }
  function administratorPassword() {
    const c = card('Built-in Administrator password'); c.outer.classList.add('section-gap');
    c.body.append(el('p', 'Changes the password of the built-in Administrator login, which is stored in hMailServer.ini rather than a mailbox. Administrator sessions must sign in again afterwards.', 'help'));
    const grid = el('div', null, 'form-grid'); const inputs = {};
    for (const [name, title, complete] of [['current', 'Current Administrator password', 'current-password'], ['replacement', 'New Administrator password', 'new-password'], ['confirmation', 'Confirm new password', 'new-password']]) {
      const wrap = el('div'); const input = el('input', null, 'form-control'); input.type = 'password'; input.autocomplete = complete; input.maxLength = 1024;
      input.id = `field-administrator-${name}-${++fieldSequence}`; const label = el('label', title, 'form-label'); label.htmlFor = input.id;
      wrap.append(label, input); grid.append(wrap); inputs[name] = input;
    }
    const clear = () => Object.values(inputs).forEach(input => { input.value = ''; });
    c.body.append(grid, button('Change Administrator password', async () => {
      if (!inputs.replacement.value || inputs.replacement.value !== inputs.confirmation.value) { notify('Enter the new password twice.', true); return; }
      try { await request('/settings/administrator-password', 'POST', { currentPassword: inputs.current.value, newPassword: inputs.replacement.value }); }
      finally { clear(); }
      notify('Administrator password changed.');
    }, 'btn btn-primary btn-sm mt-3'));
    return c.outer;
  }
  function backupPassphrase(credentials, reload) {
    const c = card('Backup passphrase'); c.outer.classList.add('section-gap');
    c.body.append(el('p', credentials.passphraseConfigured ? 'New backups are encrypted with a stored passphrase, which is never displayed. Keep a copy: without it, the archives cannot be restored.' : 'No passphrase is set, so new backups are not encrypted. Use at least 12 characters.', 'help'));
    const field = textInput('', 1024, 'password'); field.autocomplete = 'new-password';
    const actions = el('div', null, 'form-actions');
    actions.append(button('Store passphrase', async () => {
      if (field.value.length < 12) { notify('Use at least 12 characters.', true); return; }
      try { await request('/settings/backup/passphrase', 'PUT', { passphrase: field.value }); } finally { field.value = ''; }
      await reload(); notify('Backup passphrase stored.');
    }, 'btn btn-primary btn-sm'));
    if (credentials.passphraseConfigured) actions.append(button('Remove passphrase', async () => {
      if (!confirm('Remove the backup passphrase? New backups will not be encrypted.')) return;
      await request('/settings/backup/passphrase', 'PUT', { passphrase: '' }); await reload(); notify('Backup passphrase removed.');
    }, 'btn btn-outline-danger btn-sm'));
    c.body.append(labelled('backupPassphrase', 'New passphrase', field), actions);
    return c.outer;
  }
  function smartHostCredential(credentials, reload) {
    const c = card('Smart host credential'); c.outer.classList.add('section-gap');
    c.body.append(el('p', credentials.smartHostConfigured ? 'A credential is stored. It is never displayed.' : 'No credential is stored.', 'help'));
    const field = el('input', null, 'form-control'); field.type = 'password'; field.autocomplete = 'new-password'; field.maxLength = 1024;
    field.id = `field-smartHostCredential-${++fieldSequence}`; const label = el('label', 'New credential', 'form-label'); label.htmlFor = field.id;
    const actions = el('div', null, 'form-actions');
    actions.append(button('Store credential', async () => {
      if (!field.value) { notify('Enter the credential, or use Clear credential.', true); return; }
      try { await request('/settings/smtp/smart-host-credential', 'PUT', { password: field.value }); } finally { field.value = ''; }
      await reload(); notify('Smart host credential stored.');
    }, 'btn btn-primary btn-sm'));
    if (credentials.smartHostConfigured) actions.append(button('Clear credential', async () => {
      if (!confirm('Clear the stored smart host credential? Authenticated relaying stops working until a new one is stored.')) return;
      await request('/settings/smtp/smart-host-credential', 'PUT', { password: '' }); await reload(); notify('Smart host credential cleared.');
    }, 'btn btn-outline-danger btn-sm'));
    c.body.append(label, field, actions);
    return c.outer;
  }
  function editForm(values, onSave, help = {}) {
    const form = el('form'); const grid = el('div', null, 'form-grid'); const fields = {};
    Object.entries(values).forEach(([name, value]) => { const f = inputField(name, value, help[name]); fields[name] = f.input; grid.append(f.wrap); });
    const actions = el('div', null, 'form-actions'); const save = el('button', 'Save changes', 'btn btn-primary'); save.type = 'submit';
    actions.append(save); form.append(grid, actions);
    form.addEventListener('submit', async event => {
      event.preventDefault(); save.disabled = true;
      const body = {};
      Object.entries(values).forEach(([name, value]) => { body[name] = typeof value === 'boolean' ? fields[name].checked : typeof value === 'number' ? Number(fields[name].value) : fields[name].value; });
      try { await onSave(body); } catch (error) { notify(error.message, true); }
      finally { save.disabled = false; }
    });
    return form;
  }
  async function antispam(root) {
    const values = await request('/antispam');
    const note = el('p', 'SpamAssassin runs as a separate spamd service. The server DNSBL checks are independent: review both scores to avoid counting the same signal twice.', 'alert alert-light border'); root.append(note);
    const sections = [
      ['SpamAssassin connection', key => key.startsWith('SpamAssassin')],
      ['Authentication & sender checks', key => /^(Use|Check|DKIM|DMARC|AddAuthentication)/.test(key)],
      ['Scoring & message treatment', key => !key.startsWith('SpamAssassin') && !/^(Use|Check|DKIM|DMARC|AddAuthentication)/.test(key)],
    ];
    for (const [title, filter] of sections) {
      const c = card(title); c.outer.classList.add('section-gap');
      const selected = Object.fromEntries(Object.entries(values).filter(([key]) => filter(key)));
      c.body.append(editForm(selected, async body => { await request('/antispam', 'PATCH', body); notify('Spam protection settings saved.'); }, {
        SpamAssassinMergeScore: 'Use the score returned by SpamAssassin instead of the fixed server score.',
        SpamDeleteThreshold: 'Messages reaching this threshold are deleted. Review carefully before changing.',
        MaximumMessageSize: 'Maximum message size checked for spam, in KB.',
      }));
      if (title === 'SpamAssassin connection') {
        c.body.append(button('Test saved connection', async () => {
          const result = await request('/spamassassin/test', 'POST');
          if (!result.connected) { notify('SpamAssassin connection failed. Check the saved host and port, that spamd is running, and that it allows connections from this server (spamd --allowed-ips).', true); return; }
          notify(result.gtubeDetected ? 'SpamAssassin is reachable and recognized the GTUBE spam test message.' : 'SpamAssassin answered but did not flag the GTUBE test message. Check its rules and configuration.', !result.gtubeDetected);
        }));
      }
      root.append(c.outer);
    }
  }
  async function dnsbl(root) {
    const { items } = await request('/dnsbl');
    const c = card(); const heading = el('div', null, 'section-heading'); heading.append(el('h2', 'Your blocklist providers'));
    const editor = el('div', null, 'section-gap'); editor.hidden = true;
    const openEditor = (item = null) => {
      editor.replaceChildren(); editor.hidden = false;
      const e = card(item ? `Configure ${item.DNSHost}` : 'Add a DNS blocklist');
      const { id, ...values } = item || { Active: false, DNSHost: '', ExpectedResult: '127.0.0.*', RejectMessage: 'Rejected by configured DNS blocklist', Score: 3 };
      e.body.append(editForm(values, async body => {
        await request(id ? `/dnsbl/${id}` : '/dnsbl', id ? 'PATCH' : 'POST', body);
        await render(); notify('DNS blocklist saved.');
      }, { ExpectedResult: 'Use the provider’s documented return codes; the server supports wildcard matching.', DNSHost: 'The DNS lookup zone supplied by your chosen provider.' }));
      e.body.append(button('Cancel', () => { editor.hidden = true; })); editor.append(e.outer);
      editor.scrollIntoView({ behavior: 'smooth', block: 'start' });
    };
    heading.append(button('Add provider', () => openEditor(), 'btn btn-primary btn-sm')); c.body.append(heading);
    const search = el('input', null, 'form-control search mb-3'); search.type = 'search'; search.placeholder = 'Search providers…'; search.setAttribute('aria-label', 'Search DNS blocklists'); c.body.append(search);
    const tableWrap = el('div', null, 'table-responsive'); const table = el('table', null, 'table');
    const head = el('thead'); const row = el('tr'); ['Provider / zone', 'Status', 'Score', 'Actions'].forEach(text => row.append(el('th', text))); head.append(row);
    const body = el('tbody'); table.append(head, body); tableWrap.append(table); c.body.append(tableWrap);
    function rows() {
      body.replaceChildren();
      items.filter(item => item.DNSHost.toLowerCase().includes(search.value.toLowerCase())).forEach(item => {
        const tr = el('tr'); const host = el('td'); host.append(el('strong', item.DNSHost), el('div', item.ExpectedResult, 'help'));
        const state = el('td'); state.append(pill(item.Active ? 'Enabled' : 'Disabled', item.Active ? '' : 'warning'));
        const actions = el('td'); const group = el('div', null, 'd-flex gap-2 flex-wrap');
        group.append(button('Configure', () => openEditor(item)), button(item.Active ? 'Disable' : 'Enable', async () => {
          await request(`/dnsbl/${item.id}`, 'PATCH', { Active: !item.Active }); await render(); notify('Provider state updated.');
        }), button('Delete', async () => {
          if (!window.confirm(`Delete DNS blocklist ${item.DNSHost}?`)) return;
          await request(`/dnsbl/${item.id}`, 'DELETE'); await render(); notify('DNS blocklist deleted.');
        }, 'btn btn-outline-danger btn-sm'));
        actions.append(group); tr.append(host, state, el('td', item.Score), actions); body.append(tr);
      });
      if (!body.children.length) { const tr = el('tr'); const td = el('td', 'No matching providers. Add any compatible DNSBL service to get started.', 'text-secondary'); td.colSpan = 4; tr.append(td); body.append(tr); }
    }
    search.addEventListener('input', rows); rows(); root.append(c.outer, editor);
    root.append(el('p', 'Check provider licensing, resolver restrictions and return codes before enabling a list. New providers start disabled.', 'help mt-3'));
  }
  function showDnsResult(root, result) {
    root.replaceChildren(el('p', result.notice, 'alert alert-light border'));
    for (const kind of ['spf', 'dkim', 'dmarc']) {
      const record = result[kind]; const c = card(); c.outer.classList.add('section-gap');
      const heading = el('div', null, 'section-heading'); heading.append(el('h2', kind.toUpperCase()), pill(record.status, record.status === 'valid' ? '' : record.status === 'invalid' ? 'error' : 'warning'));
      c.body.append(heading, el('div', `${record.type} · ${record.name}`, 'record-label'));
      const published = Array.isArray(record.published) ? record.published.join('\n') : record.published;
      c.body.append(el('pre', published || 'No record returned.', 'record'));
      if (record.proposed) {
        c.body.append(el('div', 'Proposed record · review before publishing', 'record-label'), el('pre', record.proposed, 'record'));
        c.body.append(button('Copy proposed value', async () => { await navigator.clipboard.writeText(record.proposed); notify(`${kind.toUpperCase()} value copied.`); }));
      }
      const messages = [...record.errors, ...(record.warnings || [])];
      if (messages.length) { const list = el('ul', null, 'mt-3 mb-0'); messages.forEach(message => list.append(el('li', message))); c.body.append(list); }
      root.append(c.outer);
    }
  }
  function pager(body, result, offset, reload) {
    const row = el('div', null, 'domain-row');
    row.append(el('span', `${result.total} results · showing ${result.items.length ? offset + 1 : 0}–${offset + result.items.length}`, 'help'));
    const previous = button('Previous', () => reload(Math.max(0, offset - 50))); previous.disabled = offset === 0;
    const next = button('Next', () => reload(offset + 50)); next.disabled = offset + result.items.length >= result.total;
    row.append(previous, next); body.append(row);
  }
  function searchForm(body, value, reload) {
    const form = el('form', null, 'domain-row'); const field = inputField('search', value);
    const submit = el('button', 'Search', 'btn btn-outline-secondary'); submit.type = 'submit';
    form.append(field.wrap, submit); form.addEventListener('submit', async event => {
      event.preventDefault(); submit.disabled = true;
      try { await reload(field.input.value); } catch (error) { notify(error.message, true); }
      finally { submit.disabled = false; }
    }); body.append(form);
  }
  function accountEditor(editor, domain, account, saved) {
    editor.replaceChildren(); const create = !account; const c = card(create ? `New mailbox · ${domain.name}` : account.address);
    const values = create ? { address: `@${domain.name}`, password: '', active: true, maxSizeMB: domain.maxAccountSizeMB || 0, level: 0 }
      : { active: account.active, maxSizeMB: account.maxSizeMB, level: account.level, password: '' };
    c.body.append(el('p', 'Storage is in MB; 0 means unlimited where domain policy allows. Roles: 0 mailbox user, 1 domain administrator, 2 server administrator. Leave the password blank to keep it when editing. Addresses cannot be renamed here.', 'help'));
    const form = editForm(values, async fields => {
      if (!create && !fields.password) delete fields.password;
      await request(`/domains/${domain.id}/accounts${create ? '' : `/${account.id}`}`, create ? 'POST' : 'PATCH', fields);
      // Never retain the entered password in a detached editor or browser session.
      form.querySelector('[name=password]').value = ''; editor.replaceChildren();
      if (!create && account.id === user.accountId && (fields.password || fields.active === false || fields.level !== user.level)) {
        let result = await request('/session'); csrf = result.csrf;
        if (result.user) result = await request('/session', 'DELETE');
        csrf = result.csrf; user = null; ++generation; showIdentity(); return;
      }
      await saved(); notify(create ? 'Mailbox created.' : 'Mailbox saved.');
    });
    const password = form.querySelector('[name=password]'); password.type = 'password'; password.maxLength = 1024; password.autocomplete = 'new-password'; password.required = create;
    const address = form.querySelector('[name=address]'); if (address) { address.required = true; address.type = 'email'; address.maxLength = 320; }
    const role = form.querySelector('[name=level]'); role.max = user.level === 2 ? '2' : '1';
    if (account?.id === user.accountId && user.level !== 2) role.disabled = true;
    c.body.append(form, button('Cancel', () => editor.replaceChildren())); editor.append(c.outer);
  }
  async function accounts(editor, domain, offset = 0, search = '') {
    const result = await request(`/domains/${domain.id}/accounts?limit=50&offset=${offset}&search=${encodeURIComponent(search)}`);
    editor.replaceChildren(); const c = card(`Mailboxes · ${domain.name}`); const detail = el('div', null, 'section-gap');
    const reload = (next = offset, filter = search) => accounts(editor, domain, next, filter);
    searchForm(c.body, search, filter => reload(0, filter));
    c.body.append(button('New mailbox', () => accountEditor(detail, domain, null, () => reload(0)), 'btn btn-primary btn-sm'));
    for (const account of result.items) {
      const row = el('div', null, 'domain-row'); row.append(el('strong', account.address), pill(account.active ? 'Active' : 'Inactive', account.active ? '' : 'warning'), el('span', `${['Mailbox user', 'Domain administrator', 'Server administrator'][account.level]} · ${account.maxSizeMB || 'Unlimited'} MB`, 'help'));
      if (user.level === 2 || account.level !== 2) {
        row.append(button('Edit mailbox', () => accountEditor(detail, domain, account, () => reload())));
        row.append(button('Forwarding and replies', () => mailboxBehavior(detail, domain, account)));
        row.append(button('Signature', () => mailboxSignature(detail, domain, account)));
        row.append(button('Profile', () => mailboxProfile(detail, domain, account)));
        row.append(button('Rules', () => ruleList(detail, `/domains/${domain.id}/accounts/${account.id}/rules`, account.address)));
        row.append(button('External accounts', () => fetchAccountList(detail, domain, account)));
        if (account.id !== user.accountId) row.append(button('Delete mailbox', async () => {
          if (!confirm(`Permanently delete ${account.address} and all its messages? This cannot be undone.`)) return;
          await request(`/domains/${domain.id}/accounts/${account.id}`, 'DELETE'); await reload(0); notify('Mailbox deleted.');
        }, 'btn btn-outline-danger btn-sm'));
      }
      c.body.append(row);
    }
    if (!result.items.length) c.body.append(el('p', 'No matching mailboxes.', 'help'));
    pager(c.body, result, offset, next => reload(next)); editor.append(c.outer, detail);
  }
  function choiceField(name, value, choices) {
    const wrap = el('div'); const label = el('label', name.replace(/([a-z])([A-Z])/g, '$1 $2'), 'form-label');
    const input = el('select', null, 'form-select'); input.name = name; input.id = `choice-${++fieldSequence}`; label.htmlFor = input.id;
    for (const [id, title] of choices) { const option = el('option', title); option.value = String(id); input.append(option); }
    input.value = String(value); wrap.append(label, input); return { wrap, input };
  }
  async function queue(root, offset = 0, search = '') {
    const result = await request(`/queue?limit=50&offset=${offset}&search=${encodeURIComponent(search)}`);
    const c = card('Pending and held messages'); const detail = el('div', null, 'section-gap');
    const reload = (next = offset, filter = search) => queue(root, next, filter);
    c.body.append(el('p', 'Search sender or message ID. This is a snapshot: delivery may finish before an action. Busy messages cannot be changed; refresh and retry after delivery releases them. Removal is permanent. Retrying can release held mail.', 'help'));
    searchForm(c.body, search, filter => reload(0, filter));
    c.body.append(button('Check for due mail', async () => { await request('/queue/wake', 'POST'); notify('Delivery check requested.'); }));
    for (const item of result.items) {
      const row = el('div', null, 'domain-row'); row.append(el('strong', item.sender || 'Empty envelope sender'), el('span', `#${item.id} · ${item.size} bytes · ${item.attempts} attempts · next ${item.nextTry}`, 'help'), pill(item.busy ? 'Busy' : item.held ? 'Held' : 'Pending', item.busy || item.held ? 'warning' : ''));
      row.append(button('Inspect message', async () => {
        const message = await request(`/queue/${item.id}`); detail.replaceChildren(); const d = card(`Queue message ${message.id}`);
        d.body.append(el('p', `Created ${message.created} · ${message.recipientCount} recipients`, 'help'));
        for (const recipient of message.recipients) d.body.append(el('p', recipient));
        if (message.recipientCount > message.recipients.length) d.body.append(el('p', 'Only the first 100 recipients are shown.', 'help')); detail.append(d.outer);
      }));
      const retry = button('Retry message', async () => { if (!confirm(`Request delivery retry for message ${item.id}?`)) return; await request(`/queue/${item.id}/retry`, 'POST'); await reload(); notify('Message retry requested.'); });
      const remove = button('Remove message', async () => { if (!confirm(`Permanently remove queued message ${item.id}? It will not be delivered and cannot be recovered here.`)) return; await request(`/queue/${item.id}`, 'DELETE'); await reload(0); notify('Queue message removed.'); }, 'btn btn-outline-danger btn-sm');
      retry.disabled = remove.disabled = item.busy; row.append(retry, remove); c.body.append(row);
    }
    if (!result.items.length) c.body.append(el('p', 'No matching queued messages.', 'help'));
    pager(c.body, result, offset, next => reload(next)); root.replaceChildren(c.outer, detail);
  }
  async function routes(root, offset = 0, search = '') {
    const result = await request(`/routes?limit=50&offset=${offset}&search=${encodeURIComponent(search)}`); const c = card('Delivery routes'); const detail = el('div', null, 'section-gap');
    const reload = (next = offset, filter = search) => routes(root, next, filter);
    searchForm(c.body, search, filter => reload(0, filter));
    const edit = route => {
      detail.replaceChildren(); const create = !route; const d = card(create ? 'New route' : route.domain);
      d.body.append(el('p', 'Routes affect delivery immediately. Exact domains, *.domain and * are supported; broad patterns affect many destinations. New routes require STARTTLS and accept only listed recipients until All Addresses is enabled. Local sender/recipient classification changes relay policy; review it carefully.', 'help'));
      const values = create ? { domain: '', description: '', host: '', port: 25, tries: 4, retryMinutes: 60, allAddresses: false, recipientLocal: false, senderLocal: false, security: 3, requiresAuth: false, username: '', password: '', clearPassword: false }
        : Object.fromEntries(Object.entries({ ...route, password: '', clearPassword: false }).filter(([key]) => !['id', 'domain', 'passwordConfigured'].includes(key)));
      const form = editForm(values, async fields => {
        if (!fields.password && !fields.clearPassword) delete fields.password; delete fields.clearPassword;
        if (!create) fields = Object.fromEntries(Object.entries(fields).filter(([key, value]) => key === 'password' || value !== values[key]));
        if (!Object.keys(fields).length) { notify('No route changes.'); return; }
        if (!confirm(`Apply routing changes for ${route?.domain || fields.domain}? These affect delivery and SMTP policy immediately.`)) return;
        await request(`/routes${create ? '' : `/${route.id}`}`, create ? 'POST' : 'PATCH', fields);
        form.querySelector('[name=password]').value = ''; await reload(); notify('Route saved.');
      });
      const password = form.querySelector('[name=password]'); password.type = 'password'; password.maxLength = 1024; password.autocomplete = 'new-password';
      form.querySelector('[name=port]').max = '65535'; form.querySelector('[name=port]').min = '1'; form.querySelector('[name=tries]').min = route?.tries === 0 ? '0' : '1'; form.querySelector('[name=tries]').max = '1000'; form.querySelector('[name=retryMinutes]').min = route?.retryMinutes === 0 ? '0' : '1'; form.querySelector('[name=retryMinutes]').max = '10080';
      d.body.append(el('p', `${route?.passwordConfigured ? 'A password is configured.' : 'No saved password.'} Leave blank to preserve it, or select Clear Password. Authentication requires implicit TLS or required STARTTLS.`, 'help'), form, button('Cancel', () => detail.replaceChildren())); detail.append(d.outer);
    };
    c.body.append(button('New route', () => edit(null), 'btn btn-primary btn-sm'));
    for (const route of result.items) {
      const row = el('div', null, 'domain-row'); row.append(el('strong', route.domain), el('span', `${route.host}:${route.port} · ${route.allAddresses ? 'All recipients' : 'Listed recipients'}`, 'help'));
      row.append(button('Edit route', () => edit(route)), button('Route recipients', () => routeRecipients(detail, route)), button('Delete route', async () => {
        if (!confirm(`Delete route ${route.domain} and its recipient list? Check rules referencing this route first.`)) return;
        await request(`/routes/${route.id}`, 'DELETE'); await reload(0); notify('Route deleted.');
      }, 'btn btn-outline-danger btn-sm')); c.body.append(row);
    }
    if (!result.items.length) c.body.append(el('p', 'No matching routes.', 'help'));
    pager(c.body, result, offset, next => reload(next)); root.replaceChildren(c.outer, detail);
  }
  async function routeRecipients(root, route, offset = 0, search = '') {
    const path = `/routes/${route.id}/addresses`; const result = await request(`${path}?limit=50&offset=${offset}&search=${encodeURIComponent(search)}`); const c = card(`Allowed recipients · ${route.domain}`);
    const reload = (next = offset, filter = search) => routeRecipients(root, route, next, filter); searchForm(c.body, search, filter => reload(0, filter));
    c.body.append(el('p', 'These entries apply when All Addresses is disabled. Changes take effect immediately. Each entry is one full local or external email address.', 'help'));
    const form = editForm({ address: '' }, async fields => { if (!confirm(`Allow ${fields.address} through this route?`)) return; await request(path, 'POST', fields); await routeRecipients(root, route); notify('Route recipient added.'); });
    form.querySelector('[name=address]').type = 'email'; form.querySelector('[name=address]').required = true; c.body.append(form);
    for (const item of result.items) {
      const row = el('div', null, 'domain-row'); row.append(el('strong', item.address), button('Remove route recipient', async () => { if (!confirm(`Remove ${item.address} from this route?`)) return; await request(`${path}/${item.id}`, 'DELETE'); await routeRecipients(root, route); notify('Route recipient removed.'); })); c.body.append(row);
    }
    pager(c.body, result, offset, next => reload(next));
    root.replaceChildren(c.outer);
  }
  const ruleFields = [[0, 'Custom header'], [1, 'From'], [2, 'To'], [3, 'Cc'], [4, 'Subject'], [5, 'Body'], [6, 'Size in bytes'], [7, 'Recipient list'], [8, 'Delivery attempts']];
  const ruleMatches = [[1, 'Equals'], [2, 'Contains'], [3, 'Less than'], [4, 'Greater than'], [6, 'Does not contain'], [7, 'Does not equal'], [8, 'Wildcard']];
  const ruleActions = [[1, 'Delete message'], [2, 'Forward'], [3, 'Reply'], [4, 'Move to IMAP folder'], [6, 'Stop processing'], [7, 'Set header'], [8, 'Send using route'], [9, 'Create copy']];
  async function rules(root) { return ruleList(root, '/rules', 'Global rules'); }
  async function ruleList(root, path, title, offset = 0, search = '') {
    const result = await request(`${path}?limit=50&offset=${offset}&search=${encodeURIComponent(search)}`); const c = card(title); const detail = el('div', null, 'section-gap');
    const reload = (next = offset, filter = search) => ruleList(root, path, title, next, filter);
    c.body.append(el('p', 'Lower sort order runs first. New rules are disabled drafts. Disable a rule before changing its definition, then enable it separately. Already-processing mail can complete using its earlier rule snapshot.', 'help'));
    searchForm(c.body, search, filter => reload(0, filter)); c.body.append(button('New rule', () => ruleEditor(detail, path, title, null, reload), 'btn btn-primary btn-sm'));
    for (const rule of result.items) {
      const row = el('div', null, 'domain-row'); row.append(el('strong', rule.name), pill(rule.active ? 'Enabled' : 'Draft', rule.active ? '' : 'warning'), el('span', `Order ${rule.sortOrder} · ${rule.useAnd ? 'All conditions' : 'Any condition'}`, 'help'));
      if (!rule.active) row.append(button('Edit rule', async () => ruleEditor(detail, path, title, await request(`${path}/${rule.id}`), reload)));
      row.append(button(rule.active ? 'Disable rule' : 'Enable rule', async () => { if (!confirm(`${rule.active ? 'Disable' : 'Enable'} ${rule.name}? Active rules can delete, forward or change mail.`)) return; await request(`${path}/${rule.id}`, 'PATCH', { active: !rule.active }); await reload(); notify('Rule activation saved.'); }));
      row.append(button('Delete rule', async () => { if (!confirm(`Delete ${rule.name} and its criteria/actions?`)) return; await request(`${path}/${rule.id}`, 'DELETE'); await reload(0); notify('Rule deleted.'); }, 'btn btn-outline-danger btn-sm')); c.body.append(row);
    }
    if (!result.items.length) c.body.append(el('p', 'No matching rules.', 'help'));
    pager(c.body, result, offset, next => reload(next)); root.replaceChildren(c.outer, detail);
  }
  function ruleEditor(root, path, title, rule, saved) {
    root.replaceChildren(); const c = card(rule ? `Edit draft · ${rule.name}` : `New draft · ${title}`); const form = el('form');
    const name = inputField('name', rule?.name || ''); name.input.required = true;
    const useAnd = inputField('useAnd', rule?.useAnd ?? true); const order = inputField('sortOrder', rule?.sortOrder ?? 0);
    form.append(name.wrap, useAnd.wrap); if (rule) form.append(order.wrap);
    c.body.append(el('p', 'Up to 20 conditions and 20 ordered actions. Regex and legacy scripting/local-binding actions are unavailable here. Replies use the mailbox sender for mailbox rules. Engine/security headers cannot be overwritten. Failed persistence leaves a disabled draft for inspection and repair.', 'help'));
    const criteria = []; const actions = []; const criterionRoot = el('div'); const actionRoot = el('div');
    function criterionRow(value = { field: 4, header: '', match: 2, value: '' }) {
      if (criteria.length >= 20) { notify('A rule supports at most 20 conditions.', true); return; }
      const row = el('fieldset', null, 'card card-body section-gap'); row.append(el('legend', 'Condition', 'h6'));
      const field = choiceField('field', value.field, ruleFields), match = choiceField('match', value.match, ruleMatches), header = inputField('header', value.header || ''), text = inputField('value', value.value);
      const item = { row, read: () => ({ field: Number(field.input.value), header: header.input.value, match: Number(match.input.value), value: text.input.value }) }; criteria.push(item);
      row.append(field.wrap, match.wrap, header.wrap, text.wrap, button('Remove condition', () => { criteria.splice(criteria.indexOf(item), 1); row.remove(); })); criterionRoot.append(row);
    }
    function actionRow(value = { type: 6 }) {
      if (actions.length >= 20) { notify('A rule supports at most 20 actions.', true); return; }
      const row = el('fieldset', null, 'card card-body section-gap'); row.append(el('legend', 'Action', 'h6'));
      const choices = user.level === 2 ? ruleActions : ruleActions.filter(([id]) => id !== 8);
      const kind = choiceField('type', value.type, choices); const detail = el('div'); const item = { row, read: null }; actions.push(item);
      function fields() {
        detail.replaceChildren(); const type = Number(kind.input.value); const values = type === 2 ? { to: value.to || '', abortSpam: value.abortSpam ?? true }
          : type === 3 ? { fromAddress: value.fromAddress || (path === '/rules' ? '' : title), fromName: value.fromName || '', subject: value.subject || '', body: value.body || '', abortSpam: value.abortSpam ?? true }
          : type === 4 ? { folder: value.folder || 'INBOX' } : type === 7 ? { header: value.header || 'X-Processed', value: value.value || '' } : type === 8 ? { routeId: value.routeId || 1 } : {};
        const controls = {}; for (const [key, v] of Object.entries(values)) {
          const field = inputField(key, v); if (key === 'body') { const area = el('textarea', null, 'form-control'); area.id = field.input.id; area.name = key; area.rows = 4; area.maxLength = 8000; area.value = v; field.input.replaceWith(area); field.input = area; }
          controls[key] = field.input; detail.append(field.wrap);
        }
        item.read = () => ({ type, ...Object.fromEntries(Object.entries(values).map(([key, v]) => [key, typeof v === 'boolean' ? controls[key].checked : typeof v === 'number' ? Number(controls[key].value) : controls[key].value])) });
      }
      kind.input.addEventListener('change', fields); fields();
      row.append(kind.wrap, detail, button('Move action up', () => { const index = actions.indexOf(item); if (index > 0) { row.parentNode.insertBefore(row, actions[index-1].row); [actions[index-1], actions[index]] = [actions[index], actions[index-1]]; } }), button('Remove action', () => { actions.splice(actions.indexOf(item), 1); row.remove(); })); actionRoot.append(row);
    }
    form.append(el('h3', 'Conditions', 'h5 mt-4'), criterionRoot, button('Add condition', () => criterionRow()), el('h3', 'Actions', 'h5 mt-4'), actionRoot, button('Add action', () => actionRow()));
    (rule?.criteria || []).forEach(criterionRow); (rule?.actions || []).forEach(actionRow);
    if (!rule) { criterionRow(); actionRow(); }
    const submit = el('button', 'Save draft', 'btn btn-primary'); submit.type = 'submit'; form.append(el('div', null, 'section-gap'), submit);
    form.addEventListener('submit', async event => {
      event.preventDefault(); submit.disabled = true;
      try {
        const fields = { name: name.input.value, active: false, useAnd: useAnd.input.checked, criteria: criteria.map(item => item.read()), actions: actions.map(item => item.read()) };
        if (rule) fields.sortOrder = Number(order.input.value);
        if (new TextEncoder().encode(JSON.stringify(fields)).length > 16384) throw new Error('This draft exceeds the 16 KiB request limit. Reduce its text or number of entries.');
        await request(`${path}${rule ? `/${rule.id}` : ''}`, rule ? 'PATCH' : 'POST', fields); await saved(); notify('Rule draft saved. Enable it separately when ready.');
      } catch (error) { notify(error.message, true); } finally { submit.disabled = false; }
    }); c.body.append(form, button('Cancel', () => root.replaceChildren())); root.append(c.outer);
  }
  async function mailboxProfile(editor, domain, account, self = false) {
    const path = self ? '/self/profile' : `/domains/${domain.id}/accounts/${account.id}/profile`; const profile = await request(path);
    editor.replaceChildren(); const c = card(`Mailbox profile · ${account.address}`);
    c.body.append(el('p', `${profile.directoryBacked ? 'Directory-backed' : 'Local'} account · Last logon: ${profile.lastLogon || 'Not recorded'}`, 'help'));
    c.body.append(el('p', 'Names are used by signature macros. Directory authentication settings and credentials are managed separately.', 'help'));
    const form = editForm({ firstName: profile.firstName, lastName: profile.lastName }, async fields => {
      await request(path, 'PATCH', fields); editor.replaceChildren(); notify('Profile saved.');
    });
    c.body.append(form, button('Cancel', () => editor.replaceChildren())); editor.append(c.outer);
  }
  async function domainSettings(editor, domain) {
    const path = `/domains/${domain.id}/settings`; const values = await request(path);
    if (user.level !== 2) for (const key of ['maxAccounts', 'maxAliases', 'maxLists']) delete values[key];
    editor.replaceChildren(); const c = card(`Advanced settings · ${domain.name}`);
    c.body.append(el('p', 'Catch-all accepts otherwise unknown recipients and can increase unwanted mail. Choose an active mailbox in this primary domain, or leave blank to disable. Plus addressing uses one of + - . = _ as its separator.', 'help'));
    c.body.append(el('p', 'Object limits: 0 means unlimited. Existing objects are retained when limits are lowered. Domain signature policy can replace or append mailbox signatures; local-mail and reply switches affect both. HTML is source text and is not sanitized on save.', 'help'));
    c.body.append(el('p', 'Signature formats allow 16,000 UTF-8 bytes each. A save is limited to 16 KiB; large formats can be saved separately. Macros: %User.FirstName%, %User.LastName%, %User.Address%.', 'help'));
    const form = editForm(values, async fields => {
      const changed = Object.fromEntries(Object.entries(fields).filter(([key, value]) => value !== (typeof values[key] === 'string' ? values[key].replace(/\r\n?/g, '\n') : values[key])));
      if (!Object.keys(changed).length) { notify('No domain setting changes.'); return; }
      if (changed.signaturePlainText !== undefined) changed.signaturePlainText = changed.signaturePlainText.replace(/\n/g, '\r\n');
      if (['signaturePlainText', 'signatureHtml'].some(key => changed[key] !== undefined && new TextEncoder().encode(changed[key]).length > 16000)) throw new Error('Each signature format is limited to 16,000 UTF-8 bytes.');
      if (new TextEncoder().encode(JSON.stringify(changed)).length > 16384) throw new Error('This save exceeds the 16 KiB request limit. Shorten the signature or save each format separately.');
      if ((changed.catchAllAddress || (fields.signatureEnabled && Object.keys(changed).some(key => key.startsWith('signature')))) && !confirm(`Apply catch-all or active signature changes to ${domain.name}? These settings can affect every mailbox.`)) return;
      await request(path, 'PATCH', changed); editor.replaceChildren(); notify('Advanced domain settings saved.');
    });
    const catchAll = form.querySelector('[name=catchAllAddress]'); catchAll.type = 'email'; catchAll.maxLength = 255;
    form.querySelector('[name=plusAddressingCharacter]').maxLength = 1;
    for (const name of ['signaturePlainText', 'signatureHtml']) { const field = form.querySelector(`[name=${name}]`); field.rows = 5; field.maxLength = 16000; if (name === 'signatureHtml') field.spellcheck = false; }
    c.body.append(form, button('Cancel', () => editor.replaceChildren())); editor.append(c.outer);
  }
  async function domainNames(editor, domain, offset = 0, search = '') {
    const path = `/domains/${domain.id}/names`; const result = await request(`${path}?limit=50&offset=${offset}&search=${encodeURIComponent(search)}`);
    editor.replaceChildren(); const c = card(`Alternate domains · ${domain.name}`);
    const reload = (next = offset, filter = search) => domainNames(editor, domain, next, filter);
    c.body.append(el('p', 'Each alternate name maps addresses to this primary domain and is active immediately. Configure its DNS, certificates and authentication separately; adding a name does not prove DNS ownership. Server administrators manage these global names.', 'help'));
    searchForm(c.body, search, filter => reload(0, filter));
    if (user.level === 2) c.body.append(editForm({ name: '' }, async fields => {
      if (!confirm(`Activate ${fields.name} as an alternate name for ${domain.name}? Review DNS and ownership before proceeding.`)) return;
      await request(path, 'POST', fields); await reload(0); notify('Alternate domain added.');
    }));
    for (const item of result.items) {
      const row = el('div', null, 'domain-row'); row.append(el('strong', item.name));
      if (user.level === 2) row.append(button('Remove alternate domain', async () => {
        if (!confirm(`Remove ${item.name}? Mail addressed through this name will stop routing to ${domain.name}.`)) return;
        await request(`${path}/${item.id}`, 'DELETE'); await reload(0); notify('Alternate domain removed.');
      }, 'btn btn-outline-danger btn-sm'));
      c.body.append(row);
    }
    if (!result.items.length) c.body.append(el('p', 'No alternate domain names.', 'help'));
    pager(c.body, result, offset, next => reload(next)); editor.append(c.outer);
  }
  async function mailboxSignature(editor, domain, account, self = false) {
    const path = self ? '/self/signature' : `/domains/${domain.id}/accounts/${account.id}/signature`;
    const values = await request(path);
    editor.replaceChildren(); const c = card(`Signature · ${account.address}`);
    c.body.append(el('p', 'Choose plain text and HTML signatures for this mailbox. Domain policy can replace or append a signature, or omit it for local mail and replies. HTML is edited as source text. Saving does not sanitize email markup.', 'help'));
    c.body.append(el('p', 'Supported macros: %User.FirstName%, %User.LastName%, %User.Address%. Leave HTML empty to use the mail engine’s existing plain-text fallback. Disabling preserves both saved formats.', 'help'));
    c.body.append(el('p', 'Each format allows up to 16,000 UTF-8 bytes. A save has a 16 KiB request limit; large formats can be saved separately.', 'help'));
    const form = editForm(values, async fields => {
      // Textareas display CRLF/CR as LF. Toggling the flag must not rewrite saved source.
      const changed = Object.fromEntries(Object.entries(fields).filter(([key, value]) => value !==
        (typeof values[key] === 'string' ? values[key].replace(/\r\n?/g, '\n') : values[key])));
      if (!Object.keys(changed).length) { notify('No signature changes.'); return; }
      // The existing plain-to-HTML fallback recognizes Windows line breaks.
      if (changed.signaturePlainText !== undefined) changed.signaturePlainText = changed.signaturePlainText.replace(/\n/g, '\r\n');
      if (['signaturePlainText', 'signatureHtml'].some(key => changed[key] !== undefined && new TextEncoder().encode(changed[key]).length > 16000)) throw new Error('Each signature format is limited to 16,000 UTF-8 bytes.');
      if (new TextEncoder().encode(JSON.stringify(changed)).length > 16384) throw new Error('This save exceeds the 16 KiB request limit. Shorten the signature or save each format separately.');
      if (fields.signatureEnabled && !confirm(`Apply this active signature for ${account.address}? Domain policy also affects the result.`)) return;
      await request(path, 'PATCH', changed); editor.replaceChildren(); notify('Signature saved.');
    });
    for (const name of ['signaturePlainText', 'signatureHtml']) {
      const field = form.querySelector(`[name=${name}]`); field.rows = 6; field.maxLength = 16000;
      if (name === 'signatureHtml') field.spellcheck = false;
    }
    c.body.append(form, button('Cancel', () => editor.replaceChildren())); editor.append(c.outer);
  }
  async function mailboxBehavior(editor, domain, account, self = false) {
    const path = self ? '/self/behavior' : `/domains/${domain.id}/accounts/${account.id}/behavior`;
    const values = await request(path);
    editor.replaceChildren(); const c = card(`Forwarding and automatic replies · ${account.address}`);
    c.body.append(el('p', 'Forward to one local or external address. External forwarding can expose mail and affect SPF/DMARC delivery; review the destination. Keeping originals uses mailbox storage. Indirect forwarding loops are not checked here.', 'help'));
    c.body.append(el('p', 'Automatic replies use plain text and the existing per-sender suppression policy. Expiry occurs at the start of the selected date in server local time. Disabling replies resets sender history; editing an enabled reply does not. A blank subject uses Re: and the original subject.', 'help'));
    const form = editForm(values, async fields => {
      if ((fields.forwardEnabled || fields.autoReplyEnabled) && !confirm(`Apply active forwarding/reply settings for ${account.address}? Review the destination and reply text before proceeding.`)) return;
      await request(path, 'PATCH', fields); editor.replaceChildren(); notify('Forwarding and replies saved.');
    });
    const address = form.querySelector('[name=forwardAddress]'); address.type = 'email'; address.maxLength = 255;
    const body = form.querySelector('[name=autoReplyBody]'); body.rows = 6; body.maxLength = 16000;
    const expiry = form.querySelector('[name=autoReplyExpiresDate]'); expiry.type = 'date'; expiry.min = '1900-01-01';
    form.querySelector('[name=autoReplyExpires]').addEventListener('change', event => { expiry.required = event.target.checked; });
    expiry.required = values.autoReplyExpires;
    c.body.append(form, button('Cancel', () => editor.replaceChildren())); editor.append(c.outer);
  }
  async function addressing(editor, domain, kind, parent = null, offset = 0, search = '') {
    const singular = { aliases: 'alias', lists: 'distribution list', members: 'member' }[kind];
    const title = { aliases: 'Aliases', lists: 'Distribution lists', members: 'List members' }[kind];
    const path = `/domains/${domain.id}/${kind === 'members' ? `lists/${parent.id}/members` : kind}`;
    const result = await request(`${path}?limit=50&offset=${offset}&search=${encodeURIComponent(search)}`);
    const c = card(`${title} · ${parent?.address || domain.name}`); const detail = el('div', null, 'section-gap');
    const reload = (next = offset, filter = search) => addressing(editor, domain, kind, parent, next, filter);
    if (kind === 'aliases') c.body.append(el('p', 'Forward a local address to one local or external recipient. New aliases start disabled. Review external destinations before enabling forwarding.', 'help'));
    if (kind === 'lists') c.body.append(el('p', 'New lists start disabled, require SMTP authentication and accept member senders only. Posting modes check the SMTP envelope sender; they do not bind that address to the authenticated identity. Configure sender policy accordingly.', 'help'));
    if (kind === 'members') c.body.append(el('p', 'Each entry receives list mail and may post when members-only mode is selected. Local and external recipients are supported. Edit one address per entry.', 'help'));
    searchForm(c.body, search, filter => reload(0, filter));
    const edit = item => {
      detail.replaceChildren(); const create = !item; const formCard = card(create ? `New ${singular}` : `Edit ${singular} · ${item.address}`);
      let values;
      if (kind === 'members') values = { address: item?.address || '' };
      else if (kind === 'aliases') values = create ? { address: `@${domain.name}`, target: '', active: false } : { target: item.target, active: item.active };
      else values = create ? { address: `@${domain.name}`, active: false, mode: 1, requireAuth: true, requireAddress: '' }
        : { active: item.active, mode: item.mode, requireAuth: item.requireAuth, requireAddress: item.requireAddress };
      if (kind === 'lists') formCard.body.append(el('p', 'Anyone (public): any envelope sender. List members: addresses in this list. Specified sender: the announcement sender below. Domain senders: envelope addresses in this domain, including nonexistent mailboxes. Require Auth additionally requires SMTP authentication.', 'help'));
      const form = editForm(values, async fields => {
        if (kind === 'lists' && fields.active && (fields.mode === 0 || !fields.requireAuth)
            && !confirm('Save this active list with public posting or without SMTP authentication? Review its members, sender policy and exposure before proceeding.')) return;
        await request(`${path}${create ? '' : `/${item.id}`}`, create ? 'POST' : 'PATCH', fields);
        await reload(create ? 0 : offset); notify(`${singular[0].toUpperCase() + singular.slice(1)} saved.`);
      }, { requireAddress: 'Required for announcements. Leave empty for other modes if unused.' });
      for (const name of ['address', 'target', 'requireAddress']) {
        const field = form.querySelector(`[name=${name}]`);
        if (field) { field.type = 'email'; field.maxLength = 255; field.required = name !== 'requireAddress'; }
      }
      formCard.body.append(form, button('Cancel', () => detail.replaceChildren())); detail.append(formCard.outer);
    };
    c.body.append(button(`New ${singular}`, () => edit(null), 'btn btn-primary btn-sm'));
    for (const item of result.items) {
      const row = el('div', null, 'domain-row'); const text = el('div'); text.append(el('strong', item.address));
      if (kind === 'aliases') text.append(el('p', `Forwards to ${item.target}`, 'help'));
      if (kind === 'lists') text.append(el('p', `${['Public', 'Members only', 'Announcements', 'Domain senders'][item.mode] || 'Unknown mode'} · SMTP authentication ${item.requireAuth ? 'required' : 'not required'}`, 'help'));
      row.append(text); if (kind !== 'members') row.append(pill(item.active ? 'Active' : 'Inactive', item.active ? '' : 'warning'));
      if (kind === 'lists') row.append(button('Members', () => addressing(detail, domain, 'members', item)));
      row.append(button(`Edit ${singular}`, () => edit(item)), button(`Delete ${singular}`, async () => {
        const impact = kind === 'lists' ? ' and all its member entries' : kind === 'aliases' ? ' and stop forwarding at this address' : ' from this list';
        if (!confirm(`Delete ${item.address}${impact}? This cannot be undone.`)) return;
        await request(`${path}/${item.id}`, 'DELETE'); await reload(0); notify(`${singular[0].toUpperCase() + singular.slice(1)} deleted.`);
      }, 'btn btn-outline-danger btn-sm')); c.body.append(row);
    }
    if (!result.items.length) c.body.append(el('p', `No matching ${title.toLowerCase()}.`, 'help'));
    pager(c.body, result, offset, next => reload(next)); editor.replaceChildren(c.outer, detail);
  }
  async function domains(root, offset = 0, search = '') {
    const result = await request(`/domains?limit=50&offset=${offset}&search=${encodeURIComponent(search)}`); const { items } = result;
    const list = card('Configured domains'); const editor = el('div', null, 'section-gap');
    const reload = (next = offset, filter = search) => domains(root, next, filter);
    searchForm(list.body, search, filter => reload(0, filter));
    if (user.level === 2) list.body.append(button('New domain', () => {
      editor.replaceChildren(); const c = card('New domain');
      const form = editForm({ name: '', active: true, maxMessageSizeKB: 0, maxSizeMB: 0, maxAccountSizeMB: 0 }, async fields => {
        await request('/domains', 'POST', fields); await reload(0, ''); notify('Domain created.');
      }); form.querySelector('[name=name]').required = true; c.body.append(form); editor.append(c.outer);
    }, 'btn btn-primary btn-sm'));
    if (!items.length) list.body.append(el('p', 'No matching domains.', 'text-secondary'));
    items.forEach(domain => {
      const row = el('div', null, 'domain-row'); const text = el('div'); text.append(el('strong', domain.name), el('p', domain.dkimEnabled ? `DKIM signing enabled · selector ${domain.selector || 'not set'}` : 'DKIM signing is disabled', 'help'));
      row.append(text, pill(domain.active ? 'Active' : 'Inactive', domain.active ? '' : 'warning'));
      if (user.level >= 1) {
        row.append(button('Mailboxes', () => accounts(editor, domain)));
        row.append(button('Aliases', () => addressing(editor, domain, 'aliases')));
        row.append(button('Distribution lists', () => addressing(editor, domain, 'lists')));
        row.append(button('Advanced settings', () => domainSettings(editor, domain)));
        row.append(button('Alternate domains', () => domainNames(editor, domain)));
        row.append(button('DKIM', () => dkimPanel(editor, domain)));
        row.append(button('Edit domain', () => {
          editor.replaceChildren(); const c = card(`Domain settings · ${domain.name}`);
          const values = { active: domain.active };
          if (user.level === 2) Object.assign(values, { maxMessageSizeKB: domain.maxMessageSizeKB, maxSizeMB: domain.maxSizeMB, maxAccountSizeMB: domain.maxAccountSizeMB });
          c.body.append(el('p', 'Message size is in KB; storage limits are in MB. 0 means unlimited. Domain names cannot be renamed here.', 'help'), editForm(values, async fields => {
            await request(`/domains/${domain.id}`, 'PATCH', fields); await reload(); notify('Domain saved.');
          })); editor.append(c.outer);
        }));
      }
      if (user.level === 2 && user.domainId !== domain.id) row.append(button('Delete domain', async () => {
        if (!confirm(`Permanently delete ${domain.name}, every mailbox, alias, list and all stored messages? This cannot be undone.`)) return;
        await request(`/domains/${domain.id}`, 'DELETE'); await reload(0); notify('Domain deleted.');
      }, 'btn btn-outline-danger btn-sm'));
      if (user.level >= 1) row.append(button('Inspect DNS', () => {
        editor.replaceChildren(); const c = card(`Authentication setup · ${domain.name}`);
        c.body.append(el('p', 'Enter every public sending IP and any additional provider includes. Existing SPF mechanisms are preserved. Supply a public DKIM key only; private keys never belong here.', 'help'));
        const form = el('form'); const grid = el('div', null, 'form-grid'); const fields = {};
        for (const [name, label, value] of [['addresses', 'Sending IP addresses (comma separated)', ''], ['includes', 'Additional SPF include domains (comma separated)', ''], ['selector', 'DKIM selector', domain.selector || 'arzoie'], ['publicKey', 'DKIM public key (base64, optional)', '']]) {
          const field = inputField(name, value); field.wrap.querySelector('label').textContent = label;
          field.input.maxLength = name === 'publicKey' ? 4096 : 1024; fields[name] = field.input; grid.append(field.wrap);
        }
        const submit = el('button', 'Validate published DNS & build recommendations', 'btn btn-primary mt-4'); submit.type = 'submit';
        const results = el('div', null, 'dns-result'); form.append(grid, submit); c.body.append(form); editor.append(c.outer, results);
        form.addEventListener('submit', async event => {
          event.preventDefault(); submit.disabled = true; results.replaceChildren(el('p', 'Inspecting DNS…', 'help'));
          try {
            const split = value => value.split(',').map(v => v.trim()).filter(Boolean);
            const result = await request(`/domains/${domain.id}/dns`, 'POST', { addresses: split(fields.addresses.value), includes: split(fields.includes.value), selector: fields.selector.value, publicKey: fields.publicKey.value });
            showDnsResult(results, result);
          } catch (error) { results.replaceChildren(); notify(error.message, true); }
          finally { submit.disabled = false; }
        });
        editor.scrollIntoView({ behavior: 'smooth', block: 'start' });
      }));
      list.body.append(row);
    }); pager(list.body, result, offset, next => reload(next)); root.replaceChildren(list.outer, editor);
  }
  async function self(root) {
    const identity = await request('/self');
    const c = card(identity.address), editor = el('div', null, 'section-gap');
    const account = { address: identity.address };
    c.body.append(el('p', identity.directoryBacked ? 'Directory authentication is managed by your organization.' : 'Changes apply only to your signed-in mailbox.', 'help'));
    c.body.append(button('My profile', () => mailboxProfile(editor, null, account, true)),
      button('My signature', () => mailboxSignature(editor, null, account, true)),
      button('My forwarding and replies', () => mailboxBehavior(editor, null, account, true)),
      button('My rules', () => ruleList(editor, '/self/rules', identity.address)));
    if (identity.passwordChangeable) c.body.append(button('Change my password', () => {
      editor.replaceChildren(); const box = card('Change mailbox password'), form = el('form'), controls = {};
      for (const [name, title, autocomplete] of [['currentPassword', 'Current password', 'current-password'], ['newPassword', 'New password', 'new-password'], ['confirmPassword', 'Confirm new password', 'new-password']]) {
        const field = inputField(name, ''); field.input.type = 'password'; field.input.maxLength = 1024;
        field.input.autocomplete = autocomplete; field.input.required = true; field.wrap.querySelector('label').textContent = title;
        controls[name] = field.input; form.append(field.wrap);
      }
      const save = el('button', 'Change password and sign out', 'btn btn-primary mt-3'); save.type = 'submit'; form.append(save);
      form.addEventListener('submit', async event => {
        event.preventDefault(); if (controls.newPassword.value !== controls.confirmPassword.value) { notify('The new passwords do not match.', true); return; }
        save.disabled = true;
        try {
          const result = await request('/self/password', 'POST', { currentPassword: controls.currentPassword.value, newPassword: controls.newPassword.value });
          Object.values(controls).forEach(input => { input.value = ''; }); csrf = result.csrf; user = null; ++generation; showIdentity();
          $('login-error').textContent = 'Password changed. Sign in with your new password.';
        } catch (error) { notify(error.message, true); }
        finally { save.disabled = false; }
      });
      box.body.append(el('p', 'Use a unique password. All sessions using the previous password will be rejected.', 'help'), form);
      editor.append(box.outer);
    }));
    root.append(c.outer, editor);
  }
  async function policies(root) {
    const resources = [['surbl', 'URI blocklists (SURBL)'], ['blockedattachments', 'Blocked attachments'], ['incomingrelays', 'Trusted incoming relays'], ['securityranges', 'IP security ranges'], ['spamallowlist', 'Spam allowlist'], ['greylistexceptions', 'Greylisting exceptions']];
    const chooser = el('select', null, 'form-select'); chooser.id = 'policy-resource';
    const label = el('label', 'Policy collection', 'form-label'); label.htmlFor = chooser.id;
    for (const [value, title] of resources) { const option = el('option', title); option.value = value; chooser.append(option); }
    const listing = el('div', null, 'section-gap'), editor = el('div', null, 'section-gap');
    let sequence = 0;
    async function reload(offset = 0) {
      const run = ++sequence, resource = chooser.value, path = `/policies/${resource}`;
      editor.replaceChildren();
      listing.replaceChildren(el('p', 'Loading policies…', 'help'));
      $('notice').hidden = true;
      const result = await request(`${path}?offset=${offset}&limit=50`);
      if (run !== sequence || chooser.value !== resource) return;
      const c = card(resources.find(item => item[0] === resource)[1]);
      const warning = resource === 'securityranges'
        ? 'Changes affect new mail connections immediately. Higher priorities win; avoid equal-priority overlaps. Web administration remains available for recovery. Unauthenticated external delivery requires a separate risk acknowledgement; use only explicitly trusted source ranges. New ranges deny mail connections until configured.'
        : resource === 'incomingrelays'
        ? 'Trust only the exact IP ranges of your incoming relays. Trusting other senders can invalidate spam attribution.'
        : resource === 'blockedattachments' ? 'Attachment patterns apply when attachment blocking is enabled in server settings.'
        : resource === 'spamallowlist' ? 'Allowlisted senders skip every spam check, so mail forged with their address does too. Prefer an IP range together with an address pattern, and keep the list short.'
        : resource === 'greylistexceptions' ? 'Senders matching these IP patterns skip greylisting only; other spam checks still apply. Use them for large senders that retry from many addresses.'
          : 'Use each provider’s documented zone and usage policy. New URI blocklists start disabled.';
      c.body.append(el('p', warning, 'alert alert-light border'));
      if (resource === 'securityranges') {
        const probe = inputField('address', '127.0.0.1'); probe.input.maxLength = 64;
        const resultBox = el('p', '', 'help');
        c.body.append(probe.wrap, button('Inspect matching IP policy', async () => {
          const match = await request('/policies/securityranges/match', 'POST', { address: probe.input.value });
          resultBox.textContent = match.matched ? `${match.matched.name} (priority ${match.matched.priority}). ${match.note}` : 'No matching IP policy; new mail connections are denied.';
        }), resultBox);
      }
      function edit(item = null) {
        $('notice').hidden = true;
        const selected = item || result.defaults;
        const values = Object.fromEntries(result.schema.map(rule => [rule.name, selected[rule.name]]));
        const help = Object.fromEntries(result.schema.map(rule => [rule.name, rule.help]));
        const box = card(item ? 'Edit policy' : 'Add policy');
        const form = editForm(values, async fields => {
          const patch = item ? Object.fromEntries(Object.entries(fields).filter(([name, value]) => value !== item[name])) : fields;
          if (item && !Object.keys(patch).length) { notify('No policy fields changed.'); return; }
          if (!confirm('Apply this policy change? ' + warning)) return;
          await request(item ? `${path}/${item.id}` : path, item ? 'PATCH' : 'POST', item ? { ...patch, expected: item } : patch);
          await reload(offset); notify('Policy saved.');
        }, help);
        for (const rule of result.schema) {
          const input = form.elements.namedItem(rule.name);
          if (rule.type === 'integer') { input.min = String(rule.minimum); input.max = String(rule.maximum); input.required = true; }
          if (rule.type === 'string') input.maxLength = rule.maximum;
        }
        box.body.append(form, button('Cancel', () => editor.replaceChildren())); editor.replaceChildren(box.outer);
        editor.scrollIntoView({ behavior: 'smooth', block: 'start' });
      }
      c.body.append(button('Add policy', () => edit(), 'btn btn-primary btn-sm'));
      for (const item of result.items) {
        const row = el('div', null, 'domain-row');
        row.append(el('strong', item.name || item.zone || item.pattern || item.address), button('Edit policy', () => edit(item)));
        if (typeof item.active === 'boolean') row.append(pill(item.active ? 'Enabled' : 'Disabled', item.active ? '' : 'warning'));
        row.append(button('Delete policy', async () => {
          if (!confirm('Delete this policy? ' + warning)) return;
          await request(`${path}/${item.id}`, 'DELETE', { expected: item }); await reload(0); notify('Policy deleted.');
        }, 'btn btn-outline-danger btn-sm'));
        c.body.append(row);
      }
      if (!result.items.length) c.body.append(el('p', 'No policies in this collection.', 'help'));
      pager(c.body, result, offset, reload); listing.replaceChildren(c.outer);
    }
    chooser.addEventListener('change', () => reload().catch(error => notify(error.message, true)));
    root.append(label, chooser, listing, editor); await reload();
  }
  async function settings(root) {
    const sections = [['protocols', 'Enabled protocols'], ['smtp', 'SMTP delivery'], ['imap', 'IMAP'], ['pop3', 'POP3'],
      ['autoban', 'Auto-ban & invalid clients'], ['greylisting', 'Greylisting'], ['performance', 'Performance & caching'],
      ['general', 'General settings'], ['antivirus', 'Existing antivirus providers'], ['passwordhash', 'Password hashing policy'], ['logging', 'Logging'], ['tls', 'Mail transport TLS'], ['backup', 'Backup']];
    const chooser = el('select', null, 'form-select'); chooser.id = 'settings-section';
    const label = el('label', 'Settings section', 'form-label'); label.htmlFor = chooser.id;
    for (const [value, title] of sections) { const option = el('option', title); option.value = value; chooser.append(option); }
    const editor = el('div', null, 'section-gap'); let load = 0;
    async function reload() {
      const sequence = ++load; const section = chooser.value;
      editor.replaceChildren(el('p', 'Loading settings…', 'help'));
      const result = await request(`/settings/${section}`);
      if (sequence !== load || section !== chooser.value) return;
      const c = card(sections.find(item => item[0] === section)[1]); const form = el('form');
      const grid = el('div', null, 'form-grid'); const controls = new Map();
      for (const rule of result.schema) {
        const help = rule.help + (rule.restart ? ' Requires service restart.' : '') + (rule.direct ? ' Save this setting on its own.' : '');
        const field = rule.options ? optionField(rule, result.values[rule.name], help) : inputField(rule.name, result.values[rule.name], help);
        // These schemas are returned by the native allowlist, not executable UI templates.
        if (!rule.options && rule.type === 'integer') { field.input.type = 'number'; field.input.required = true; field.input.min = String(rule.minimum); field.input.max = String(rule.maximum); field.input.step = '1'; }
        if (!rule.options && rule.type === 'string') field.input.maxLength = rule.maximum;
        controls.set(rule.name, { input: field.input, rule }); grid.append(field.wrap);
      }
      const save = el('button', 'Save server settings', 'btn btn-primary mt-3'); save.type = 'submit';
      form.append(grid, save); c.body.append(form, el('p', 'Changes are checked against the values you loaded. Reload after a conflict. Restart is never performed automatically.', 'help mt-3'));
      editor.replaceChildren(c.outer);
      if (section === 'smtp') editor.append(smartHostCredential(result.credentials, reload));
      if (section === 'general') editor.append(administratorPassword());
      if (section === 'backup') editor.append(backupPassphrase(result.credentials, reload));
      if (section === 'antivirus') editor.append(await antivirusPanel());
      if (section === 'performance') {
        const index = card('Message index'); index.outer.classList.add('section-gap');
        index.body.append(el('p', 'Clearing removes indexed metadata. While indexing is enabled it is rebuilt gradually, and IMAP sorting is slower until then.', 'help'),
          button('Clear message index', async () => {
            if (!confirm('Clear the message index? It is rebuilt gradually while indexing is enabled.')) return;
            await request('/settings/performance/message-index/clear', 'POST'); notify('Message index cleared.');
          }, 'btn btn-outline-danger btn-sm'));
        editor.append(index.outer);
      }
      form.addEventListener('submit', async event => {
        event.preventDefault(); if (!form.reportValidity()) return;
        const fields = {}, expected = {}; let restart = false;
        for (const [name, { input, rule }] of controls) {
          const value = rule.type === 'boolean' ? input.checked : rule.type === 'integer' ? Number(input.value) : input.value;
          if (value !== result.values[name]) { fields[name] = value; expected[name] = result.values[name]; restart ||= rule.restart; }
        }
        if (!Object.keys(fields).length) { notify('No settings changed.'); return; }
        const direct = result.schema.find(rule => rule.direct && rule.name in fields);
        if (direct && Object.keys(fields).length > 1) { notify(`Save ${direct.name.replace(/([a-z])([A-Z])/g, '$1 $2')} on its own, then the other changes.`, true); return; }
        if (!confirm(`Apply ${Object.keys(fields).length} server setting change(s)? Mail processing policy may change.${restart ? ' Some changes require a later service restart.' : ''}`)) return;
        save.disabled = true;
        try { await request(`/settings/${section}`, 'PATCH', { fields, expected }); await reload(); notify(restart ? 'Settings saved. Restart the service to apply the marked changes.' : 'Server settings saved.'); }
        catch (error) { notify(error.message, true); }
        finally { save.disabled = false; }
      });
    }
    chooser.addEventListener('change', () => reload().catch(error => notify(error.message, true)));
    root.append(label, chooser, editor); await reload();
  }
  async function transport(root) {
    const chooser = el('select', null, 'form-select'); chooser.id = 'transport-resource';
    const label = el('label', 'Transport inventory', 'form-label'); label.htmlFor = chooser.id;
    for (const [value, title] of [['listeners', 'Mail listeners'], ['certificates', 'Configured certificates']]) {
      const option = el('option', title); option.value = value; chooser.append(option);
    }
    const search = inputField('search', '', 'Search protocol, address or port for listeners; name or ID for certificates.');
    search.input.id = 'transport-search'; search.wrap.querySelector('label').htmlFor = search.input.id;
    const form = el('form', null, 'form-actions'); const find = el('button', 'Search transport', 'btn btn-outline-secondary'); find.type = 'submit';
    form.append(search.wrap, find);
    const listing = el('div', null, 'section-gap'); let offset = 0, sequence = 0;
    async function reload(next = 0) {
      offset = next; const run = ++sequence, resource = chooser.value;
      const result = await request(`/transport/${resource}?limit=25&offset=${offset}&search=${encodeURIComponent(search.input.value)}`);
      if (run !== sequence) return;
      const c = card(resource === 'listeners' ? 'Configured listeners' : 'Configured certificates');
      c.body.append(el('p', resource === 'listeners'
        ? 'These are saved settings. Running listeners can differ until the service is restarted.'
        : 'Registrations do not confirm certificate validity, private-key access or expiry. Validate certificates before assigning them to mail services.', 'help'));
      if (!result.items.length) c.body.append(el('p', 'No matching configuration records.', 'help'));
      for (const item of result.items) {
        const row = el('div', null, 'domain-row'), summary = el('div');
        if (resource === 'listeners') {
          summary.append(el('strong', `${item.protocol} · ${item.address}:${item.port}`),
            el('p', `${item.security} · ${item.certificateId ? `Certificate ID ${item.certificateId}` : 'No certificate assigned'}`, 'help mb-0'));
        } else summary.append(el('strong', item.name || 'Unnamed certificate'));
        summary.append(el('p', `ID ${item.id}`, 'help mb-0')); row.append(summary); c.body.append(row);
      }
      pager(c.body, result, offset, reload); listing.replaceChildren(c.outer);
    }
    form.addEventListener('submit', event => { event.preventDefault(); reload().catch(error => notify(error.message, true)); });
    chooser.addEventListener('change', () => { search.input.value = ''; reload().catch(error => notify(error.message, true)); });
    const inspection = card('Inspect a PFX certificate');
    inspection.body.append(el('p', 'Check a certificate before installing it. Inspection does not install, register or assign a certificate.', 'help'));
    const inspectForm = el('form'), grid = el('div', null, 'form-grid');
    const fileWrap = el('div'), fileLabel = el('label', 'PFX file', 'form-label'), file = el('input', null, 'form-control');
    file.id = 'certificate-pfx'; file.type = 'file'; file.accept = '.pfx,.p12'; file.required = true; fileLabel.htmlFor = file.id;
    fileWrap.append(fileLabel, file, el('p', 'PFX/PKCS#12, up to 512 KiB. The file is used only for this inspection.', 'help'));
    const password = inputField('PFX password', '', 'Leave empty only when the file has no password.');
    password.input.type = 'password'; password.input.autocomplete = 'off'; password.input.maxLength = 1024;
    const hostWrap = el('div'), hostLabel = el('label', 'Mail hostnames or IP addresses', 'form-label'), hosts = el('textarea', null, 'form-control');
    hosts.id = 'certificate-hosts'; hosts.rows = 3; hosts.maxLength = 8192; hosts.required = true; hostLabel.htmlFor = hosts.id;
    hosts.placeholder = 'mail.example.com';
    hostWrap.append(hostLabel, hosts, el('p', 'One name per line or comma, up to 32. Use the names mail clients connect to.', 'help'));
    grid.append(fileWrap, password.wrap, hostWrap);
    const inspect = el('button', 'Inspect PFX', 'btn btn-primary mt-3'); inspect.type = 'submit';
    const report = el('div', null, 'section-gap'); inspectForm.append(grid, inspect); inspection.body.append(inspectForm, report);
    inspectForm.addEventListener('submit', async event => {
      event.preventDefault(); if (!inspectForm.reportValidity()) return;
      const selected = file.files[0], requestedHosts = hosts.value.split(/[,\r\n]+/).map(value => value.trim()).filter(Boolean);
      if (!selected || selected.size < 1 || selected.size > 524288) { notify('Select a PFX file no larger than 512 KiB.', true); return; }
      if (!requestedHosts.length || requestedHosts.length > 32) { notify('Supply between 1 and 32 mail hostnames or IP addresses.', true); return; }
      inspect.disabled = true; let bytes;
      try {
        bytes = new Uint8Array(await selected.arrayBuffer()); const chunks = [];
        for (let start = 0; start < bytes.length; start += 32768) chunks.push(String.fromCharCode(...bytes.subarray(start, start + 32768)));
        const result = await request('/transport/certificates/inspect', 'POST', { pfx: btoa(chunks.join('')), password: password.input.value, hosts: requestedHosts });
        if (!root.isConnected || currentPage !== 'transport') return;
        const details = card('PFX inspection results');
        details.body.append(el('p', 'Inspection only. Chain trust reflects this web host; mail clients can have different trust settings. Revocation was not checked.', 'help'),
          el('p', 'SHA-256 fingerprint', 'record-label'), el('pre', result.sha256, 'record'));
        for (const [title, value] of [['Subject', result.subject], ['Issuer', result.issuer],
          ['Valid from', result.notBefore], ['Valid until', result.notAfter], ['Key', `${result.keyAlgorithm} · ${result.keyBits} bits`],
          ['Validity period', result.isCurrentlyValid ? 'Current' : 'Expired or not yet valid'],
          ['Server authentication usage', result.serverAuthenticationEku ? 'Permitted' : 'Not permitted'],
          ['Chain trust (revocation not checked)', result.chainTrustedWithoutRevocationCheck ? 'Trusted by this web host' : 'Review chain trust'],
          ['Chain status', result.chainStatus.join(', ') || 'No chain errors reported']]) {
          const row = el('div', null, 'domain-row'); row.append(el('strong', title), el('span', value)); details.body.append(row);
        }
        for (const host of result.hosts) details.body.append(el('p', `${host.host}: ${host.matches ? 'matches' : 'does not match'}`, 'help'));
        for (const warning of result.warnings) details.body.append(el('p', warning, 'alert alert-warning mt-2'));
        report.replaceChildren(details.outer); notify('Certificate inspected. Review the individual checks and warnings.');
      } catch (error) { if (root.isConnected) notify(error.message, true); }
      finally { if (bytes) bytes.fill(0); password.input.value = ''; inspect.disabled = false; }
    });
    root.append(label, chooser, form, listing, inspection.outer); await reload();
  }
  async function diagnostics(root) {
    const result = await request('/diagnostics');
    const c = card('Runtime checks');
    result.checks.forEach(check => { const row = el('div', null, 'domain-row'); row.append(el('div', check.message), pill(check.safe ? 'OK' : 'Review', check.safe ? '' : 'warning')); c.body.append(row); });
    root.append(c.outer, el('p', 'Run behind HTTPS with a dedicated identity, private state storage and no direct access to application source. See the deployment guide.', 'help mt-3'));
    const native = card('Server diagnostics'); native.outer.classList.add('section-gap');
    native.body.append(el('p', 'Checks the data and backup directories, IP ranges, error logs, IPv6 and, when given, the MX records and connectivity of a local domain and outbound port 25 to a test domain.', 'help'));
    const grid = el('div', null, 'form-grid'); const inputs = {};
    for (const [name, title] of [['localDomain', 'Local domain (optional)'], ['testDomain', 'External test domain (optional)']]) {
      const wrap = el('div'); const input = el('input', null, 'form-control'); input.id = `field-${name}`; input.maxLength = 253;
      const label = el('label', title, 'form-label'); label.htmlFor = input.id; wrap.append(label, input); grid.append(wrap); inputs[name] = input;
    }
    const output = el('div', null, 'mt-3'); const run = generation;
    async function poll() {
      for (let attempt = 0; attempt < 60 && run === generation; ++attempt) {
        const result = await request('/diagnostics/results'); diagnosticResults(output, result);
        if (!result.running) return;
        await new Promise(resolve => setTimeout(resolve, 2000));
      }
    }
    native.body.append(grid, button('Run diagnostics', async () => {
      await request('/diagnostics/run', 'POST', { localDomain: inputs.localDomain.value.trim(), testDomain: inputs.testDomain.value.trim() }); await poll();
    }, 'btn btn-primary btn-sm mt-3'), output);
    const lookup = card('Mail server lookup'); lookup.outer.classList.add('section-gap');
    lookup.body.append(el('p', 'Shows the servers outgoing delivery would use for a domain: its MX hosts, or the domain itself when it has none. Routes and a smart host can override this.', 'help'));
    const domain = el('input', null, 'form-control'); domain.id = 'field-lookupDomain'; domain.maxLength = 253;
    const domainLabel = el('label', 'Domain', 'form-label'); domainLabel.htmlFor = domain.id; const servers = el('div', null, 'mt-3');
    lookup.body.append(domainLabel, domain, button('Look up mail servers', async () => {
      const result = await request('/diagnostics/mail-servers', 'POST', { domain: domain.value.trim() });
      servers.replaceChildren();
      if (!result.items.length) servers.append(el('p', result.resolved ? 'No mail servers were found.' : 'The lookup failed.', 'help'));
      for (const item of result.items) { const row = el('div', null, 'domain-row'); row.append(el('div', item.host), el('div', item.address, 'help')); servers.append(row); }
    }, 'btn btn-primary btn-sm mt-3'), servers);
    root.append(native.outer, lookup.outer);
    diagnosticResults(output, await request('/diagnostics/results'));
  }
  const securityLabels = { 0: 'None (legacy, unencrypted)', 1: 'SSL/TLS', 2: 'STARTTLS when offered (legacy)', 3: 'STARTTLS required' };
  function labelled(name, title, input) {
    const wrap = el('div'); input.id = `field-${name}-${++fieldSequence}`; input.name = name;
    const label = el('label', title, input.type === 'checkbox' ? 'form-check-label' : 'form-label'); label.htmlFor = input.id;
    if (input.type === 'checkbox') { wrap.className = 'form-check'; input.className = 'form-check-input'; wrap.append(input, label); }
    else wrap.append(label, input);
    return wrap;
  }
  function textInput(value, maximum, type = 'text') {
    const input = el('input', null, 'form-control'); input.type = type; input.value = value === undefined || value === null ? '' : String(value); input.maxLength = maximum; return input;
  }
  function checkInput(value) { const input = el('input'); input.type = 'checkbox'; input.checked = !!value; return input; }
  async function dkimPanel(editor, domain) {
    const state = await request(`/domains/${domain.id}/dkim`);
    const server = user.level === 2; const base = `/domains/${domain.id}/dkim`;
    const c = card(`DKIM signing for ${domain.name}`);
    c.body.append(el('p', state.signEnabled ? `Outgoing mail is signed with selector ${state.selector}.` : 'Outgoing mail is not signed.', 'help'));
    if (state.selector && !state.keyManaged) c.body.append(el('p', 'The active key file was set outside this panel. Generate a key here to manage rotation from the panel.', 'help'));
    for (const key of state.keys) {
      const block = el('div', null, 'section-gap'); block.append(el('strong', `${key.selector}${key.active ? ' (active)' : ''}`));
      block.append(el('div', `Publish a TXT record at ${key.dnsName}:`, 'help'));
      const record = el('textarea', null, 'form-control'); record.readOnly = true; record.rows = 3; record.value = key.dnsRecord; block.append(record);
      if (server && !key.active) {
        const skip = checkInput(false);
        block.append(labelled(`skip-${key.selector}`, 'Activate without checking DNS (the record must still be published before mail is signed)', skip),
          button('Activate this key', async () => {
            await request(`${base}/keys/${encodeURIComponent(key.selector)}/activate`, 'POST', { skipDnsCheck: skip.checked }); notify(`Key ${key.selector} is now active.`); await dkimPanel(editor, domain);
          }, 'btn btn-primary btn-sm mt-2'),
          button('Delete key', async () => {
            if (!confirm(`Delete the key ${key.selector}? Mail already signed with it cannot be verified once its DNS record is removed.`)) return;
            await request(`${base}/keys/${encodeURIComponent(key.selector)}`, 'DELETE'); notify('Key deleted.'); await dkimPanel(editor, domain);
          }, 'btn btn-outline-danger btn-sm mt-2'));
      }
      c.body.append(block);
    }
    if (!state.keys.length) c.body.append(el('p', 'No keys have been generated for this domain.', 'help'));
    if (server) {
      const grid = el('div', null, 'form-grid section-gap');
      const selector = textInput(`arzoie${new Date().getFullYear()}`, 63);
      grid.append(labelled('dkimSelector', 'New key selector', selector));
      c.body.append(grid, el('p', 'Keys are RSA 2048, the size large receivers expect.', 'help'), button('Generate key', async () => {
        await request(`${base}/keys`, 'POST', { selector: selector.value.trim() }); notify('Key generated. Publish its DNS record, then activate it.'); await dkimPanel(editor, domain);
      }, 'btn btn-outline-secondary btn-sm mt-2'));
      const settings = el('div', null, 'form-grid section-gap');
      const sign = checkInput(state.signEnabled), aliases = checkInput(state.signAliases);
      const header = el('select', null, 'form-select'), body = el('select', null, 'form-select'), algorithm = el('select', null, 'form-select');
      for (const [select, value] of [[header, state.headerCanonicalization], [body, state.bodyCanonicalization]]) {
        for (const [code, title] of [[1, 'Simple'], [2, 'Relaxed']]) { const option = el('option', title); option.value = String(code); select.append(option); } select.value = String(value);
      }
      for (const [code, title] of [[2, 'RSA-SHA256'], [1, 'RSA-SHA1 (obsolete)']]) { const option = el('option', title); option.value = String(code); algorithm.append(option); } algorithm.value = String(state.algorithm);
      settings.append(labelled('dkimSign', 'Sign outgoing mail', sign), labelled('dkimAliases', 'Also sign mail from alternate domain names', aliases),
        labelled('dkimHeader', 'Header canonicalization', header), labelled('dkimBody', 'Body canonicalization', body), labelled('dkimAlgorithm', 'Algorithm', algorithm));
      c.body.append(settings, button('Save DKIM settings', async () => {
        await request(base, 'PATCH', { signEnabled: sign.checked, signAliases: aliases.checked, headerCanonicalization: Number(header.value), bodyCanonicalization: Number(body.value), algorithm: Number(algorithm.value) });
        notify('DKIM settings saved.'); await dkimPanel(editor, domain);
      }, 'btn btn-primary btn-sm mt-2'));
    }
    editor.replaceChildren(c.outer);
  }
  async function antivirusPanel() {
    const status = await request('/antivirus');
    const c = card('Scanner tests'); c.outer.classList.add('section-gap');
    c.body.append(el('p', 'Each test scans a harmless EICAR test file and a clean file with the configured scanner. ClamAV is installed separately (on this server or another host) and connected above. The custom scanner and ClamWin run a program on this server, so their paths can only be changed with the desktop tool.', 'help'));
    const output = el('pre', '', 'log-text'); output.hidden = true;
    for (const [key, title, detail] of [['clamav', 'ClamAV', `${status.clamAv.host || '(no host)'}:${status.clamAv.port}`],
      ['custom', 'Custom scanner', status.customScanner.executable || '(not configured)'], ['clamwin', 'ClamWin', status.clamWin.executable || '(not configured)']]) {
      const row = el('div', null, 'domain-row'); const enabled = key === 'clamav' ? status.clamAv.enabled : key === 'custom' ? status.customScanner.enabled : status.clamWin.enabled;
      row.append(el('div', title), el('div', detail, 'help'), pill(enabled ? 'Enabled' : 'Disabled', enabled ? '' : 'warning'));
      row.append(button(`Test ${title}`, async () => {
        const result = await request('/antivirus/test', 'POST', { scanner: key });
        output.textContent = result.message || ''; output.hidden = !result.message;
        notify(result.passed ? `${title} detected the test virus.` : `${title} test failed. See the details below.`, !result.passed);
      }));
      c.body.append(row);
    }
    c.body.append(output);
    return c.outer;
  }
  async function fetchAccountList(detail, domain, account) {
    const base = `/domains/${domain.id}/accounts/${account.id}/fetchaccounts`;
    const result = await request(base);
    const c = card(`External accounts for ${account.address}`);
    c.body.append(el('p', 'Downloads mail from other POP3 servers into this mailbox. Passwords are stored encrypted and never displayed. New accounts start disabled and always use an encrypted connection.', 'help'));
    for (const item of result.items) {
      const row = el('div', null, 'domain-row'); const text = el('div');
      text.append(el('strong', item.name), el('div', `${item.username} at ${item.serverAddress}:${item.port} · ${securityLabels[item.security] || item.security} · every ${item.minutesBetweenFetch} min`, 'help'));
      row.append(text, pill(item.running ? 'Downloading' : item.active ? 'Enabled' : 'Disabled', item.active ? '' : 'warning'));
      row.append(button('Edit', () => fetchAccountForm(detail, domain, account, item)));
      if (item.active) row.append(button('Download now', async () => { await request(`${base}/${item.id}/run`, 'POST'); notify('Download requested.'); }));
      row.append(button('Delete', async () => {
        if (!confirm(`Delete the external account ${item.name}? Mail already downloaded stays in the mailbox.`)) return;
        await request(`${base}/${item.id}`, 'DELETE'); notify('External account deleted.'); await fetchAccountList(detail, domain, account);
      }, 'btn btn-outline-danger btn-sm'));
      c.body.append(row);
    }
    if (!result.items.length) c.body.append(el('p', 'No external accounts.', 'help'));
    c.body.append(button('New external account', () => fetchAccountForm(detail, domain, account, null), 'btn btn-primary btn-sm mt-2'));
    detail.replaceChildren(c.outer);
  }
  function fetchAccountForm(detail, domain, account, item) {
    const base = `/domains/${domain.id}/accounts/${account.id}/fetchaccounts`;
    const c = card(item ? `Edit ${item.name}` : 'New external account'); const form = el('form'); const grid = el('div', null, 'form-grid');
    const inputs = {
      name: textInput(item?.name, 255), serverAddress: textInput(item?.serverAddress, 253), port: textInput(item?.port ?? 995, 5, 'number'),
      username: textInput(item?.username, 255), password: textInput('', 1024, 'password'), minutesBetweenFetch: textInput(item?.minutesBetweenFetch ?? 30, 5, 'number'),
      mimeRecipientHeaders: textInput(item?.mimeRecipientHeaders ?? 'To,CC,X-RCPT-To,X-Envelope-To', 255),
      active: checkInput(item?.active), processMimeRecipients: checkInput(item?.processMimeRecipients), processMimeDate: checkInput(item?.processMimeDate),
      routeRecipients: checkInput(item?.routeRecipients), useAntiSpam: checkInput(item?.useAntiSpam ?? true), useAntiVirus: checkInput(item?.useAntiVirus ?? true)
    };
    inputs.password.autocomplete = 'new-password'; inputs.port.min = '1'; inputs.port.max = '65535'; inputs.minutesBetweenFetch.min = '1'; inputs.minutesBetweenFetch.max = '10080';
    for (const key of ['name', 'serverAddress', 'username']) inputs[key].required = true;
    if (!item) inputs.password.required = true;
    const security = el('select', null, 'form-select');
    for (const value of [1, 3]) { const option = el('option', securityLabels[value]); option.value = String(value); security.append(option); }
    if (item && ![1, 3].includes(item.security)) { const option = el('option', `Current: ${securityLabels[item.security]}`); option.value = 'keep'; security.append(option); security.value = 'keep'; }
    else security.value = String(item?.security ?? 1);
    const keep = el('select', null, 'form-select');
    for (const [value, title] of [[-1, 'Delete messages after downloading'], [0, 'Keep messages on the server'], ['days', 'Delete after a number of days']]) { const option = el('option', title); option.value = String(value); keep.append(option); }
    const days = textInput(item && item.daysToKeep > 0 ? item.daysToKeep : 7, 5, 'number'); days.min = '1'; days.max = '36500';
    keep.value = !item || item.daysToKeep === 0 ? '0' : item.daysToKeep === -1 ? '-1' : 'days';
    const titles = { name: 'Name', serverAddress: 'POP3 server', port: 'Port', username: 'User name', password: item ? (item.passwordConfigured ? 'New password (leave empty to keep)' : 'Password') : 'Password',
      minutesBetweenFetch: 'Minutes between downloads', mimeRecipientHeaders: 'Recipient headers', active: 'Enabled', processMimeRecipients: 'Deliver to recipients in MIME headers',
      processMimeDate: 'Use the message date header', routeRecipients: 'Allow route recipients', useAntiSpam: 'Spam protection', useAntiVirus: 'Virus scanning' };
    for (const [key, input] of Object.entries(inputs)) grid.append(labelled(key, titles[key], input));
    grid.append(labelled('security', 'Connection security', security), labelled('daysToKeep', 'Messages on the server', keep), labelled('days', 'Days to keep', days));
    const save = el('button', item ? 'Save external account' : 'Create external account', 'btn btn-primary mt-3'); save.type = 'submit';
    form.append(grid, save); c.body.append(form, button('Back to list', () => fetchAccountList(detail, domain, account), 'btn btn-outline-secondary btn-sm mt-2'));
    detail.replaceChildren(c.outer);
    form.addEventListener('submit', async event => {
      event.preventDefault(); if (!form.reportValidity()) return;
      const fields = {};
      for (const [key, input] of Object.entries(inputs)) fields[key] = input.type === 'checkbox' ? input.checked : input.type === 'number' ? Number(input.value) : input.value;
      if (!fields.password) delete fields.password;
      if (security.value !== 'keep') fields.security = Number(security.value);
      fields.daysToKeep = keep.value === 'days' ? Number(days.value) : Number(keep.value);
      if (!fields.processMimeRecipients) fields.routeRecipients = false;
      save.disabled = true;
      try { await request(item ? `${base}/${item.id}` : base, item ? 'PATCH' : 'POST', fields); inputs.password.value = ''; notify(item ? 'External account saved.' : 'External account created.'); await fetchAccountList(detail, domain, account); }
      catch (error) { inputs.password.value = ''; notify(error.message, true); }
      finally { save.disabled = false; }
    });
  }
  async function folders(root) {
    const groupsCard = card('Groups'); const folderCard = card('Public folders'); folderCard.outer.classList.add('section-gap');
    const detail = el('div', null, 'section-gap');
    root.append(el('p', 'Groups collect mailboxes so public-folder permissions can be granted once. Mailbox owners share their own folders from their mail client (IMAP ACL).', 'help'), groupsCard.outer, folderCard.outer, detail);
    async function loadGroups() {
      const result = await request('/groups');
      groupsCard.body.replaceChildren(el('h2', 'Groups'));
      for (const group of result.items) {
        const row = el('div', null, 'domain-row'); row.append(el('div', group.name), el('div', `${group.members} member(s)`, 'help'));
        row.append(button('Members', () => groupMembers(detail, group, loadGroups)));
        row.append(button('Rename', async () => {
          const name = prompt('New group name', group.name); if (!name || name === group.name) return;
          await request(`/groups/${group.id}`, 'PATCH', { name }); notify('Group renamed.'); await loadGroups();
        }));
        row.append(button('Delete', async () => {
          if (!confirm(`Delete the group ${group.name}? Its members lose the public-folder permissions granted through it.`)) return;
          await request(`/groups/${group.id}`, 'DELETE'); notify('Group deleted.'); await loadGroups(); await loadFolders();
        }, 'btn btn-outline-danger btn-sm'));
        groupsCard.body.append(row);
      }
      if (!result.items.length) groupsCard.body.append(el('p', 'No groups.', 'help'));
      const name = textInput('', 255); const form = el('form', null, 'form-actions');
      const add = el('button', 'Create group', 'btn btn-primary btn-sm'); add.type = 'submit';
      form.append(labelled('newGroup', 'New group name', name), add); groupsCard.body.append(form);
      form.addEventListener('submit', async event => {
        event.preventDefault(); if (!name.value.trim()) return;
        try { await request('/groups', 'POST', { name: name.value.trim() }); notify('Group created.'); await loadGroups(); } catch (error) { notify(error.message, true); }
      });
    }
    async function loadFolders() {
      const result = await request('/publicfolders');
      folderCard.body.replaceChildren(el('h2', 'Public folders'));
      for (const folder of result.items) {
        const row = el('div', null, 'domain-row'); row.append(el('div', folder.path));
        row.append(button('Permissions', () => folderPermissions(detail, folder)));
        row.append(button('Rename', async () => {
          const name = prompt('New folder name', folder.name); if (!name || name === folder.name) return;
          await request(`/publicfolders/${folder.id}`, 'PATCH', { name }); notify('Folder renamed.'); await loadFolders();
        }));
        row.append(button('Delete', async () => {
          if (!confirm(`Delete the public folder ${folder.path} and every message in it? This cannot be undone.`)) return;
          await request(`/publicfolders/${folder.id}`, 'DELETE'); notify('Public folder deleted.'); await loadFolders();
        }, 'btn btn-outline-danger btn-sm'));
        folderCard.body.append(row);
      }
      if (!result.items.length) folderCard.body.append(el('p', 'No public folders.', 'help'));
      const name = textInput('', 255); const parent = el('select', null, 'form-select');
      const top = el('option', '(top level)'); top.value = '0'; parent.append(top);
      for (const folder of result.items) { const option = el('option', folder.path); option.value = String(folder.id); parent.append(option); }
      const form = el('form', null, 'form-grid'); const add = el('button', 'Create folder', 'btn btn-primary btn-sm'); add.type = 'submit';
      form.append(labelled('newFolder', 'New folder name', name), labelled('parentFolder', 'Inside', parent), add); folderCard.body.append(form);
      form.addEventListener('submit', async event => {
        event.preventDefault(); if (!name.value.trim()) return;
        try { await request('/publicfolders', 'POST', { name: name.value.trim(), parentId: Number(parent.value) }); notify('Public folder created.'); await loadFolders(); } catch (error) { notify(error.message, true); }
      });
    }
    await loadGroups(); await loadFolders();
  }
  async function groupMembers(detail, group, reloadGroups) {
    const result = await request(`/groups/${group.id}/members`);
    const c = card(`Members of ${group.name}`);
    for (const member of result.items) {
      const row = el('div', null, 'domain-row'); row.append(el('div', member.address || `Account ${member.accountId}`));
      row.append(button('Remove', async () => {
        await request(`/groups/${group.id}/members/${member.id}`, 'DELETE'); notify('Member removed.'); await groupMembers(detail, group, reloadGroups); await reloadGroups();
      }, 'btn btn-outline-danger btn-sm'));
      c.body.append(row);
    }
    if (!result.items.length) c.body.append(el('p', 'No members.', 'help'));
    const address = textInput('', 320, 'email'); const form = el('form', null, 'form-actions');
    const add = el('button', 'Add member', 'btn btn-primary btn-sm'); add.type = 'submit';
    form.append(labelled('memberAddress', 'Mailbox address', address), add); c.body.append(form);
    form.addEventListener('submit', async event => {
      event.preventDefault();
      try {
        const accountId = await findAccountId(address.value.trim());
        await request(`/groups/${group.id}/members`, 'POST', { accountId }); notify('Member added.'); await groupMembers(detail, group, reloadGroups); await reloadGroups();
      } catch (error) { notify(error.message, true); }
    });
    detail.replaceChildren(c.outer);
  }
  async function findAccountId(address) {
    const at = address.lastIndexOf('@'); if (at < 1) throw new Error('Enter a full mailbox address.');
    const domainName = address.slice(at + 1).toLowerCase();
    const domainList = await request(`/domains?search=${encodeURIComponent(domainName)}`);
    const domain = domainList.items.find(item => item.name.toLowerCase() === domainName); if (!domain) throw new Error('That domain is not hosted here.');
    const accounts = await request(`/domains/${domain.id}/accounts?search=${encodeURIComponent(address)}`);
    const account = accounts.items.find(item => item.address.toLowerCase() === address.toLowerCase()); if (!account) throw new Error('No mailbox has that address.');
    return account.id;
  }
  async function folderPermissions(detail, folder) {
    const result = await request(`/publicfolders/${folder.id}/permissions`);
    const c = card(`Permissions for ${folder.path}`);
    const kinds = ['Mailbox', 'Group', 'Anyone'];
    for (const permission of result.items) {
      const block = el('div', null, 'section-gap'); block.append(el('strong', `${kinds[permission.type]}: ${permission.type === 2 ? 'every mailbox' : permission.holder}`));
      const grid = el('div', null, 'form-grid'); const checks = {};
      for (const right of result.rights) { checks[right] = checkInput(permission.rights[right]); grid.append(labelled(`right-${permission.id}-${right}`, right.replace(/([a-z])([A-Z])/g, '$1 $2'), checks[right])); }
      block.append(grid, button('Save permission', async () => {
        const rights = {}; for (const [right, input] of Object.entries(checks)) rights[right] = input.checked;
        await request(`/publicfolders/${folder.id}/permissions/${permission.id}`, 'PATCH', { rights }); notify('Permission saved.');
      }, 'btn btn-primary btn-sm mt-2'), button('Remove permission', async () => {
        if (!confirm('Remove this permission?')) return;
        await request(`/publicfolders/${folder.id}/permissions/${permission.id}`, 'DELETE'); notify('Permission removed.'); await folderPermissions(detail, folder);
      }, 'btn btn-outline-danger btn-sm mt-2'));
      c.body.append(block);
    }
    if (!result.items.length) c.body.append(el('p', 'No permissions: only the server can see this folder.', 'help'));
    const kind = el('select', null, 'form-select'); kinds.forEach((title, index) => { const option = el('option', title); option.value = String(index); kind.append(option); });
    const holder = textInput('', 320); const form = el('form', null, 'form-grid'); const add = el('button', 'Add permission', 'btn btn-primary btn-sm'); add.type = 'submit';
    form.append(labelled('permissionType', 'Grant to', kind), labelled('permissionHolder', 'Mailbox address or group name', holder), add); c.body.append(form);
    form.addEventListener('submit', async event => {
      event.preventDefault();
      try {
        const body = { type: Number(kind.value), rights: { lookup: true, read: true } };
        if (body.type === 0) body.accountId = await findAccountId(holder.value.trim());
        if (body.type === 1) {
          const group = (await request('/groups?limit=100')).items.find(item => item.name.toLowerCase() === holder.value.trim().toLowerCase());
          if (!group) throw new Error('No group has that name.'); body.groupId = group.id;
        }
        await request(`/publicfolders/${folder.id}/permissions`, 'POST', body); notify('Permission added with lookup and read rights.'); await folderPermissions(detail, folder);
      } catch (error) { notify(error.message, true); }
    });
    detail.replaceChildren(c.outer);
  }
  async function maintenance(root) {
    const status = await request('/backup');
    const backup = card('Backups');
    backup.body.append(el('p', status.destination ? `Archives are written to ${status.destination}. Choose what they contain and the destination under Server settings → Backup.` : 'No backup folder is set. Set one under Server settings → Backup.', 'help'));
    const actions = el('div', null, 'form-actions');
    actions.append(button('Start backup', async () => {
      if (!confirm('Start a backup now? Large message stores take a while; progress is written to hmailserver_backup.log.')) return;
      await request('/backup/start', 'POST'); notify('Backup started. See hmailserver_backup.log under Logs for progress.'); await render();
    }, 'btn btn-primary'));
    backup.body.append(el('p', status.running ? 'A backup or restore is running.' : 'No backup or restore is running.', 'help'), actions);
    for (const archive of status.archives) {
      const row = el('div', null, 'domain-row'); row.append(el('div', archive.name), el('div', `${Math.ceil(archive.size / 1048576)} MiB · ${archive.modified}`, 'help'));
      row.append(button('Restore…', () => restoreForm(root, archive)));
      backup.body.append(row);
    }
    if (!status.archives.length) backup.body.append(el('p', 'No archives in the backup folder.', 'help'));
    root.append(backup.outer, await secretKeyPanel());
  }
  function restoreForm(root, archive) {
    const c = card(`Restore ${archive.name}`); c.outer.classList.add('section-gap');
    c.body.append(el('p', 'Restoring replaces the chosen parts of this server with the archive contents. Take a fresh backup first. Restoring settings also restores the protected settings of the server the archive came from, so import its installation key first if it came from another server.', 'alert alert-warning'));
    const settings = checkInput(false), domainsBox = checkInput(false), messages = checkInput(false), passphrase = textInput('', 1024, 'password'), confirmName = textInput('', 80);
    passphrase.autocomplete = 'off';
    const grid = el('div', null, 'form-grid');
    grid.append(labelled('restoreSettings', 'Settings', settings), labelled('restoreDomains', 'Domains and mailboxes', domainsBox), labelled('restoreMessages', 'Messages', messages),
      labelled('restorePassphrase', 'Archive passphrase (if encrypted)', passphrase), labelled('restoreConfirm', 'Type the archive name to confirm', confirmName));
    c.body.append(grid, button('Restore', async () => {
      const body = { name: archive.name, confirmName: confirmName.value, settings: settings.checked, domains: domainsBox.checked, messages: messages.checked };
      if (passphrase.value) body.passphrase = passphrase.value;
      try { await request('/backup/restore', 'POST', body); notify('Restore started. See hmailserver_backup.log for progress.'); } finally { passphrase.value = ''; }
    }, 'btn btn-danger btn-sm mt-3'));
    root.append(c.outer);
  }
  async function secretKeyPanel() {
    const key = await request('/secretkey');
    const c = card('Installation key'); c.outer.classList.add('section-gap');
    c.body.append(el('p', `Key ${key.keyId} protects stored passwords and encrypted backups. Export it, protected by a passphrase of at least 12 characters, before moving to another server; import an exported key on the new one. Keep exports private.`, 'help'));
    const passphrase = textInput('', 1024, 'password'); passphrase.autocomplete = 'new-password';
    const output = el('textarea', null, 'form-control'); output.rows = 3; output.readOnly = true; output.hidden = true;
    const exportGrid = el('div', null, 'form-grid'); exportGrid.append(labelled('exportPassphrase', 'Export passphrase', passphrase));
    c.body.append(exportGrid, button('Export key', async () => {
      try { const result = await request('/secretkey/export', 'POST', { passphrase: passphrase.value }); output.value = result.exported; output.hidden = false; notify('Key exported. Copy it to a safe place.'); }
      finally { passphrase.value = ''; }
    }, 'btn btn-outline-secondary btn-sm mt-2'), output);
    const imported = el('textarea', null, 'form-control'); imported.rows = 3; imported.maxLength = 16384; const importPassphrase = textInput('', 1024, 'password'); importPassphrase.autocomplete = 'off';
    const importGrid = el('div', null, 'form-grid section-gap');
    importGrid.append(labelled('importKey', 'Exported key to import', imported), labelled('importPassphrase', 'Its passphrase', importPassphrase));
    c.body.append(importGrid, button('Import key', async () => {
      if (!confirm('Import this key? Every protected setting is re-protected under it; keep the current key exported first.')) return;
      try { const result = await request('/secretkey/import', 'POST', { exported: imported.value.trim(), passphrase: importPassphrase.value }); notify(`Key ${result.keyId} imported.`); imported.value = ''; }
      finally { importPassphrase.value = ''; }
    }, 'btn btn-outline-danger btn-sm mt-2'));
    return c.outer;
  }
  const serverStates = ['Unknown', 'Stopped', 'Starting', 'Running', 'Stopping'];
  async function server(root) {
    const status = await request('/status');
    const metrics = el('div', null, 'grid');
    [['SERVER STATUS', serverStates[status.state] || `State ${status.state}`], ['STARTED', status.startTime || '—'], ['VERSION', `${status.version} ${status.architecture}`],
      ['SMTP SESSIONS', status.sessions.smtp], ['IMAP SESSIONS', status.sessions.imap], ['POP3 SESSIONS', status.sessions.pop3],
      ['MESSAGES PROCESSED', status.processedMessages], ['SPAM DETECTED', status.spamMessagesDetected], ['VIRUSES REMOVED', status.virusesRemoved]].forEach(([label, value]) => {
      const c = card(); c.body.append(el('div', label, 'metric-label'), el('div', value, 'metric')); metrics.append(c.outer);
    });
    root.append(metrics);
    const control = card('Mail services'); control.outer.classList.add('section-gap');
    control.body.append(el('p', 'Stopping disconnects every SMTP, IMAP and POP3 session and pauses delivery and external fetching. This console stays available, so you can start the services again here. Counters cover the time since the service process started.', 'help'));
    const actions = el('div', null, 'form-actions');
    if (status.state === 3) actions.append(button('Stop mail services', async () => {
      if (!confirm('Stop all mail services? Connected users are disconnected and no mail is received or delivered until you start them again.')) return;
      await request('/server/stop', 'POST', { expectedState: 3 }); await render(); notify('Mail services stopped.');
    }, 'btn btn-outline-danger'));
    if (status.state === 1) actions.append(button('Start mail services', async () => {
      await request('/server/start', 'POST', { expectedState: 1 }); await render(); notify('Mail services started.');
    }, 'btn btn-primary'));
    actions.append(button('Refresh status', () => render()));
    control.body.append(actions);
    const cache = card('Cache hit rates'); cache.outer.classList.add('section-gap');
    for (const [name, rate] of Object.entries(status.cacheHitRates)) { const row = el('div', null, 'domain-row'); row.append(el('div', name), el('div', `${rate}%`)); cache.body.append(row); }
    root.append(control.outer, cache.outer);
  }
  async function messages(root) {
    const result = await request('/servermessages');
    root.append(el('p', 'These texts are used in notifications and replies the server generates. Names are fixed; only the text can change. Some messages are sent to external senders, so avoid internal details.', 'help'));
    for (const item of result.items) {
      const c = card(item.name); c.outer.classList.add('section-gap');
      const text = el('textarea', null, 'form-control'); text.value = item.text; text.rows = 4; text.maxLength = result.maximum;
      text.id = `field-message-${item.id}`; const label = el('label', 'Text', 'form-label'); label.htmlFor = text.id;
      c.body.append(label, text, button('Save message', async () => {
        if (text.value === item.text) { notify('No changes to save.'); return; }
        Object.assign(item, await request(`/servermessages/${item.id}`, 'PATCH', { expected: { id: item.id, name: item.name, text: item.text }, text: text.value }));
        text.value = item.text; notify('Server message saved.');
      }, 'btn btn-primary btn-sm mt-2'));
      root.append(c.outer);
    }
  }
  async function sendout(root) {
    const c = card('Send a message to accounts');
    c.body.append(el('p', 'One message is queued to every active account in an active domain whose address matches the pattern (* matches any characters). Preview the audience first; sending requires that exact count.', 'help'));
    const form = el('form'); const grid = el('div', null, 'form-grid'); const fields = {};
    for (const [name, value, kind, maximum] of [['recipients', '*', 'text', 320], ['fromAddress', '', 'email', 320], ['fromName', 'Server administrator', 'text', 255], ['subject', '', 'text', 998], ['body', '', 'textarea', 12288]]) {
      const wrap = el('div'); const input = el(kind === 'textarea' ? 'textarea' : 'input', null, 'form-control');
      if (kind !== 'textarea') input.type = kind; else input.rows = 8;
      input.id = `field-sendout-${name}`; input.name = name; input.value = value; input.maxLength = maximum; input.required = name !== 'fromName';
      const label = el('label', name.replace(/([a-z])([A-Z])/g, '$1 $2'), 'form-label'); label.htmlFor = input.id; wrap.append(label, input);
      fields[name] = input; grid.append(wrap);
    }
    let audience = null;
    const summary = el('p', 'Preview the recipients before sending.', 'help mt-3');
    const send = el('button', 'Send message', 'btn btn-primary'); send.type = 'submit'; send.disabled = true;
    fields.recipients.addEventListener('input', () => { audience = null; send.disabled = true; summary.textContent = 'Preview the recipients before sending.'; });
    const preview = button('Preview recipients', async () => {
      const result = await request('/sendout/preview', 'POST', { recipients: fields.recipients.value });
      audience = { pattern: fields.recipients.value, count: result.recipients }; send.disabled = result.recipients < 1;
      summary.textContent = result.recipients ? `${result.recipients} account(s) match.` : 'No active accounts match this pattern.';
    });
    const actions = el('div', null, 'form-actions'); actions.append(preview, send);
    form.append(grid, summary, actions); c.body.append(form); root.append(c.outer);
    form.addEventListener('submit', async event => {
      event.preventDefault(); if (!form.reportValidity() || !audience || audience.pattern !== fields.recipients.value) return;
      if (!confirm(`Send this message to ${audience.count} account(s)? It cannot be recalled once delivered.`)) return;
      send.disabled = true;
      try {
        const body = {}; for (const [name, input] of Object.entries(fields)) body[name] = input.value;
        const result = await request('/sendout', 'POST', { ...body, expectedRecipients: audience.count });
        notify(`Message queued to ${result.recipients} account(s).`); fields.subject.value = ''; fields.body.value = '';
      } catch (error) { notify(error.message, true); }
      finally { send.disabled = !audience; }
    });
  }
  async function logs(root) {
    const result = await request('/logs');
    const viewer = el('div', null, 'section-gap');
    const c = card('Log files');
    c.body.append(el('p', 'Logs contain addresses, IP addresses and message metadata; credentials are masked by the server. Pages show up to 16 KiB at a time, newest text last.', 'help'));
    async function show(name, before) {
      const page = await request(`/logs/${encodeURIComponent(name)}${before === undefined ? '' : `?before=${before}`}`);
      const view = card(name); const text = el('pre', page.text || '(empty)', 'log-text'); text.tabIndex = 0;
      const actions = el('div', null, 'form-actions');
      if (page.start > 0) actions.append(button('Older', () => show(name, page.start)));
      actions.append(button('Newest', () => show(name)));
      view.body.append(el('p', `Bytes ${page.start}–${page.end} of ${page.size}.`, 'help'), text, actions);
      viewer.replaceChildren(view.outer);
    }
    for (const file of result.items) {
      const row = el('div', null, 'domain-row');
      row.append(el('div', file.name), el('div', `${Math.ceil(file.size / 1024)} KiB · ${file.modified}`, 'help'), button('View', () => show(file.name)));
      c.body.append(row);
    }
    if (!result.items.length) c.body.append(el('p', 'No log files found. Enable logging under Server settings → Logging.', 'help'));
    root.append(c.outer, viewer);
  }
  function diagnosticResults(container, result) {
    container.replaceChildren();
    if (result.running) { container.append(el('p', 'Diagnostics are running…', 'help')); return; }
    if (!result.results.length) { container.append(el('p', 'No diagnostics have run since the service started.', 'help')); return; }
    container.append(el('p', `Completed ${result.completed}.`, 'help'));
    for (const item of result.results) {
      const row = el('div', null, 'domain-row'); const text = el('div');
      text.append(el('strong', item.name), el('div', item.description, 'help'));
      row.append(text, pill(item.success ? 'OK' : 'Review', item.success ? '' : 'warning')); container.append(row);
      if (item.details) container.append(el('pre', item.details, 'log-text'));
    }
  }
  async function render() {
    if (!user) return;
    const run = ++generation; let page = location.hash.slice(1) || 'overview';
    if (!pages[page] || (user.level !== 2 && !['overview', 'domains', 'self'].includes(page)) || (user.level < 1 && page === 'domains') || (page === 'self' && !(user.accountId > 0))) page = 'overview';
    currentPage = page; $('notice').hidden = true;
    $('page-title').textContent = pages[page][0]; $('page-description').textContent = pages[page][1];
    document.querySelectorAll('[data-page]').forEach(node => {
      if (node.dataset.page === page) node.setAttribute('aria-current', 'page'); else node.removeAttribute('aria-current');
    });
    $('content').setAttribute('aria-busy', 'true'); $('content').replaceChildren(el('p', 'Loading…', 'help'));
    const container = el('div');
    try {
      await ({ overview, domains, antispam, dnsbl, diagnostics, queue, routes, rules, settings, policies, self, transport, server, messages, sendout, logs, folders, maintenance })[page](container);
      if (run === generation) $('content').replaceChildren(container);
    } catch (error) { if (run === generation) { $('content').replaceChildren(); notify(error.message, true); } }
    finally { if (run === generation) $('content').setAttribute('aria-busy', 'false'); }
  }
  $('login-form').addEventListener('submit', async event => {
    event.preventDefault(); const submit = event.currentTarget.querySelector('button'); submit.disabled = true; $('login-error').textContent = '';
    try {
      const result = await request('/session', 'POST', { username: $('username').value, password: $('password').value });
      csrf = result.csrf; user = result.user; $('password').value = ''; showIdentity(); await render();
    } catch (error) { $('login-error').textContent = error.message; }
    finally { submit.disabled = false; }
  });
  $('sign-out').addEventListener('click', async () => {
    try { const result = await request('/session', 'DELETE'); csrf = result.csrf; user = null; ++generation; showIdentity(); }
    catch (error) { notify(error.message, true); }
  });
  $('refresh').addEventListener('click', render); window.addEventListener('hashchange', render);
  request('/session').then(result => { csrf = result.csrf; user = result.user; showIdentity(); return render(); })
    .catch(error => { $('startup').textContent = error.message; });
})();
