// Authenticatie via OpenID Connect (Keycloak), met de bibliotheek oidc-client-ts.
// De globale `oidc` wordt als gewoon script geladen in index.html en callback.html.

const settings = {
    authority: 'https://g18.ipv6.thelastgym.be/auth/realms/unbreakable',
    client_id: 'unbreakable-spa',
    redirect_uri: location.origin + '/callback.html',
    post_logout_redirect_uri: location.origin + '/',
    response_type: 'code',    // authorization code flow
    scope: 'openid profile',  // PKCE (S256) staat standaard aan in oidc-client-ts
};

export const userManager = new oidc.UserManager(settings);

let huidige = null;

// Haalt de eerder bewaarde gebruiker op en onthoudt die, zodat currentUser() synchroon kan.
export async function loadUser() {
    try {
        huidige = await userManager.getUser();
    } catch {
        huidige = null;
    }
    return huidige;
}

// De naam van de ingelogde gebruiker (claim preferred_username), of null.
export function currentUser() {
    return huidige && !huidige.expired ? huidige.profile.preferred_username : null;
}

// Het access token dat als Bearer naar de API gaat.
export function accessToken() {
    return huidige && !huidige.expired ? huidige.access_token : null;
}

export function login() {
    return userManager.signinRedirect();
}

export function logout() {
    huidige = null;
    return userManager.signoutRedirect();
}
