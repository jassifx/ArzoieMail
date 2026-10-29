'use strict';
(() => {
  const $ = (id) => document.getElementById(id);
  let csrf = '', user = null, currentPage = '', generation = 0, fieldSequence = 0;
  const pages = {
    overview: ['Overview', 'Your mail infrastructure, at a glance.'],
    domains: ['Domains & DNS', 'Manage domains, mailboxes, storage limits and authentication records.'],
    antispam: ['Spam protection', 'Fine-tune filtering, authentication checks, and your SpamAssassin connection.'],
    dnsbl: ['DNS blocklists', 'Your providers. Your policy. Manage multiple lists without vendor restrictions.'],
    diagnostics: ['Diagnostics', 'Check the administration runtime and deployment configuration.'],
    queue: ['Delivery queue', 'Inspect pending mail and request a retry or removal.'],
    routes: ['Routing', 'Manage next-hop delivery and allowed recipients.'],
    rules: ['Global rules', 'Build disabled drafts, then validate and enable processing rules.'],
    settings: ['Server settings', 'Configure protocols, security policy, performance and existing scanners.'],
    policies: ['Filtering & relays', 'Manage URI blocklists, attachment rules and explicitly trusted incoming relays.'],
    self: ['My mailbox', 'Manage your own profile, signature, forwarding, replies, rules and password.'],
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
    const input = el(['autoReplyBody', 'signaturePlainText', 'signatureHtml'].includes(name) ? 'textarea' : ['level', 'mode', 'signatureMethod', 'security'].includes(name) ? 'select' : 'input'); input.name = name; input.id = `field-${name}-${++fieldSequence}`; label.htmlFor = input.id;
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
          notify(result.connected ? 'SpamAssassin connection succeeded.' : 'SpamAssassin connection failed. Check the saved host, port and service.', !result.connected);
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
    const resources = [['surbl', 'URI blocklists (SURBL)'], ['blockedattachments', 'Blocked attachments'], ['incomingrelays', 'Trusted incoming relays'], ['securityranges', 'IP security ranges']];
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
        row.append(el('strong', item.name || item.zone || item.pattern), button('Edit policy', () => edit(item)));
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
      ['general', 'General settings'], ['antivirus', 'Existing antivirus providers'], ['passwordhash', 'Password hashing policy'], ['logging', 'Logging']];
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
        const field = inputField(rule.name, result.values[rule.name], rule.help + (rule.restart ? ' Requires service restart.' : ''));
        // These schemas are returned by the native allowlist, not executable UI templates.
        if (rule.type === 'integer') { field.input.type = 'number'; field.input.required = true; field.input.min = String(rule.minimum); field.input.max = String(rule.maximum); field.input.step = '1'; }
        if (rule.type === 'string') field.input.maxLength = rule.maximum;
        controls.set(rule.name, { input: field.input, rule }); grid.append(field.wrap);
      }
      const save = el('button', 'Save server settings', 'btn btn-primary mt-3'); save.type = 'submit';
      form.append(grid, save); c.body.append(form, el('p', 'Changes are checked against the values you loaded. Reload after a conflict. Restart is never performed automatically.', 'help mt-3'));
      editor.replaceChildren(c.outer);
      form.addEventListener('submit', async event => {
        event.preventDefault(); if (!form.reportValidity()) return;
        const fields = {}, expected = {}; let restart = false;
        for (const [name, { input, rule }] of controls) {
          const value = rule.type === 'boolean' ? input.checked : rule.type === 'integer' ? Number(input.value) : input.value;
          if (value !== result.values[name]) { fields[name] = value; expected[name] = result.values[name]; restart ||= rule.restart; }
        }
        if (!Object.keys(fields).length) { notify('No settings changed.'); return; }
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
  async function diagnostics(root) {
    const result = await request('/diagnostics');
    const c = card('Runtime checks');
    result.checks.forEach(check => { const row = el('div', null, 'domain-row'); row.append(el('div', check.message), pill(check.safe ? 'OK' : 'Review', check.safe ? '' : 'warning')); c.body.append(row); });
    root.append(c.outer, el('p', 'Run behind HTTPS with a dedicated identity, private state storage and no direct access to application source. See the deployment guide.', 'help mt-3'));
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
      await ({ overview, domains, antispam, dnsbl, diagnostics, queue, routes, rules, settings, policies, self })[page](container);
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
