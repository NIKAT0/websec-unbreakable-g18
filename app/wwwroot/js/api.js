// Praat met de API. Alles wat de SPA weet, komt van hier.
// De identiteit zit niet meer in een header, maar in het access token (Bearer).

import { accessToken } from './auth.js';

async function call(method, path, body) {
    const headers = {};

    const token = accessToken();
    if (token) {
        headers['Authorization'] = 'Bearer ' + token;
    }

    const options = { method, headers };

    if (body !== undefined) {
        headers['Content-Type'] = 'application/json';
        options.body = JSON.stringify(body);
    }

    const response = await fetch('/api' + path, options);
    if (!response.ok) {
        throw new Error(`${method} ${path} gaf ${response.status}`);
    }

    const type = response.headers.get('Content-Type') || '';
    return type.includes('application/json') ? response.json() : response.text();
}

export const api = {
    users: () => call('GET', '/users'),
    inbox: (from) => call('GET', '/messages' + (from ? '?from=' + encodeURIComponent(from) : '')),
    search: (q) => call('GET', '/messages/search?q=' + encodeURIComponent(q)),
    message: (id) => call('GET', '/messages/' + id),
    sent: () => call('GET', '/messages/sent'),
    send: (message) => call('POST', '/messages', message),
    remove: (id) => call('DELETE', '/messages/' + id),
    exportJson: () => call('GET', '/messages/export'),
};
