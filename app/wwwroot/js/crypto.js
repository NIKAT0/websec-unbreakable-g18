// Client-side crypto (opdracht 3). De server ziet nooit het bericht of een sleutel.

export const ALFABET = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_';   // A = 0, 0 = 26, _ = 36

export function normaliseer(tekst) {
    return tekst.trim().toUpperCase().replaceAll(' ', '_');
}

// Geeft een foutmelding terug, of null als alles in orde is.
export function controleer(tekst, sleutel) {
    if (sleutel.length !== 40 || [...sleutel].some(t => !ALFABET.includes(t))) {
        return 'De sleutel moet een lijn van 40 tekens uit het codeboek zijn.';
    }
    if (tekst.length > sleutel.length) {
        return `Het bericht mag maximaal ${sleutel.length} tekens lang zijn.`;
    }
    const fout = [...tekst].find(t => !ALFABET.includes(t));
    if (fout) {
        return `Het teken '${fout}' kan niet versleuteld worden: enkel A-Z, 0-9 en spaties.`;
    }
    return null;
}

// HMAC-SHA256 met Web Crypto (enkel via HTTPS of op localhost).
// De MAC gaat over het lijnnummer en de ciphertext, zodat ook het nummer niet ongemerkt kan wijzigen.
const enc = new TextEncoder();

export function macData(keyLine, ciphertext) {
    return `${keyLine}|${ciphertext}`;
}

async function hmacSleutel(geheim) {
    return crypto.subtle.importKey('raw', enc.encode(geheim), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign', 'verify']);
}

export async function maakMac(geheim, data) {
    const mac = await crypto.subtle.sign('HMAC', await hmacSleutel(geheim), enc.encode(data));
    return btoa(String.fromCharCode(...new Uint8Array(mac)));
}

// verify vergelijkt in constante tijd, dus geen eigen vergelijking met ===.
export async function controleerMac(geheim, data, macBase64) {
    try {
        const mac = Uint8Array.from(atob(macBase64), c => c.charCodeAt(0));
        return await crypto.subtle.verify('HMAC', await hmacSleutel(geheim), mac, enc.encode(data));
    } catch {
        return false;
    }
}

// One-time pad modulo 37. richting +1 = versleutelen, -1 = ontsleutelen.
export function otp(tekst, sleutel, richting) {
    return [...tekst].map((teken, i) => {
        const waarde = ALFABET.indexOf(teken) + richting * ALFABET.indexOf(sleutel[i]);
        return ALFABET[((waarde % 37) + 37) % 37];
    }).join('');
}
