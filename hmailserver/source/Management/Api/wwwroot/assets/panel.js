'use strict';
(() => {
  const $ = (id) => document.getElementById(id);
  let csrf = '', user = null, currentPage = '', generation = 0;
  const pages = {
    overview: ['Overview', 'Your mail infrastructure, at a glance.'],
    domains: ['Domains & DNS', 'Inspect authentication records and keep all your sending providers working together.'],
    antispam: ['Spam protection', 'Fine-tune filtering, authentication checks, and your SpamAssassin connection.'],
    dnsbl: ['DNS blocklists', 'Your providers. Your policy. Manage multiple lists without vendor restrictions.'],
    diagnostics: ['Diagnostics', 'Check the administration runtime and deployment configuration.'],
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
    const payload = await response.json();
    if (!response.ok) {
      if (response.status === 401 && path !== '/session') { user = null; showIdentity(); }
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
    }
  }
  async function overview(root) {
    if (user.level !== 2) {
      const c = card('Your domain workspace');
      c.body.append(el('p', 'Inspect your domain and its DNS authentication configuration.', 'text-secondary'));
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
    c.body.append(el('p', 'Preview scope: existing administration functions are being migrated in stages. This panel currently covers inspection, spam settings and DNSBL management; it does not yet replace every legacy administration page.', 'help'));
    root.append(c.outer);
  }
  function inputField(name, value, help = '') {
    const wrap = el('div'); const label = el('label', name.replace(/([a-z])([A-Z])/g, '$1 $2'), 'form-label');
    const input = el('input'); input.name = name; input.id = `field-${name}`; label.htmlFor = input.id;
    if (typeof value === 'boolean') {
      wrap.className = 'form-check'; input.type = 'checkbox'; input.className = 'form-check-input'; input.checked = value;
      label.className = 'form-check-label'; wrap.append(input, label);
    } else {
      input.type = typeof value === 'number' ? 'number' : 'text'; input.className = 'form-control'; input.value = String(value);
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
  async function domains(root) {
    const { items } = await request('/domains'); const list = card('Configured domains'); const editor = el('div', null, 'section-gap');
    if (!items.length) list.body.append(el('p', 'No domains are configured. Domain creation will arrive in a later migration slice.', 'text-secondary'));
    items.forEach(domain => {
      const row = el('div', null, 'domain-row'); const text = el('div'); text.append(el('strong', domain.name), el('p', domain.dkimEnabled ? `DKIM signing enabled · selector ${domain.selector || 'not set'}` : 'DKIM signing is disabled', 'help'));
      row.append(text, pill(domain.active ? 'Active' : 'Inactive', domain.active ? '' : 'warning'));
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
    }); root.append(list.outer, editor);
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
    if (!pages[page] || (user.level !== 2 && !['overview', 'domains'].includes(page))) page = 'overview';
    currentPage = page; $('notice').hidden = true;
    $('page-title').textContent = pages[page][0]; $('page-description').textContent = pages[page][1];
    document.querySelectorAll('[data-page]').forEach(node => {
      if (node.dataset.page === page) node.setAttribute('aria-current', 'page'); else node.removeAttribute('aria-current');
    });
    $('content').setAttribute('aria-busy', 'true'); $('content').replaceChildren(el('p', 'Loading…', 'help'));
    const container = el('div');
    try {
      await ({ overview, domains, antispam, dnsbl, diagnostics })[page](container);
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
