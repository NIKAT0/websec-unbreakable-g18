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

// One-time pad modulo 37. richting +1 = versleutelen, -1 = ontsleutelen.
export function otp(tekst, sleutel, richting) {
    return [...tekst].map((teken, i) => {
        const waarde = ALFABET.indexOf(teken) + richting * ALFABET.indexOf(sleutel[i]);
        return ALFABET[((waarde % 37) + 37) % 37];
    }).join('');
}
