# RFC 6749 Section 4 Compliance Guide

## Overview

This document describes the complete implementation of **RFC 6749 Section 4: Obtaining Authorization** in the `ClientCredentialFlow` resource. All four grant types are now fully supported.

---

## Compliance Status

| RFC Section | Grant Type | Status | Implementation |
|-------------|------------|--------|----------------|
| **§4.1** | Authorization Code Grant | ✅ **COMPLETE** | Full support with all sub-sections |
| **§4.2** | Implicit Grant | ✅ **COMPLETE** | Full support with all sub-sections |
| **§4.3** | Resource Owner Password Credentials | ✅ **COMPLETE** | Full support with security warnings |
| **§4.4** | Client Credentials Grant | ✅ **COMPLETE** | Full support for confidential clients |

**Overall Compliance: 100% (4 out of 4 grant types implemented)**

---

## 1. Authorization Code Grant (RFC 6749 §4.1)

### Description
The authorization code grant type is used to obtain both access tokens and refresh tokens. It is optimized for confidential clients and provides the highest security through the use of an intermediary authorization code.

### Flow Diagram
```
 +----------+
 | Resource |
 |  Owner   |
 +----------+
      ^
      |
     (B)
 +----|-----+          Client Identifier      +---------------+
 |         -+----(A)-- & Redirection URI ---->|               |
 |  User-   |                                 | Authorization |
 |  Agent  -+----(B)-- User authenticates --->|     Server    |
 |          |                                 |               |
 |         -+----(C)-- Authorization Code ---<|               |
 +-|----|---+                                 +---------------+
   |    |                                           ^      v
  (A)  (C)                                          |      |
   |    |                                           |      |
   ^    v                                           |      |
 +---------+                                        |      |
 |         |>---(D)-- Authorization Code -----------'      |
 |  Client |          & Redirection URI                    |
 |         |                                               |
 |         |<---(E)----- Access Token ---------------------'
 +---------+       (w/ Optional Refresh Token)
```

### Implementation

#### §4.1.1 Authorization Request
```csharp
var authRequest = new AuthorizationRequest
{
    ResponseType = "code",              // REQUIRED: "code" for authorization code grant
    ClientId = "your-client-id",        // REQUIRED: Client identifier
    RedirectUri = "https://...",        // OPTIONAL: Where to send user after auth
    Scope = "read write",               // OPTIONAL: Space-delimited scopes
    State = "random-csrf-token"         // RECOMMENDED: CSRF protection
};

var authUrl = flow.BuildAuthorizationUrl(authRequest);
// Redirect user to: authUrl
```

#### §4.1.2 Authorization Response
User is redirected back to your redirect_uri with:
```
https://your-app.com/callback?code=AUTH_CODE&state=random-csrf-token
```

Parse the response:
```csharp
var response = flow.ParseAuthorizationResponse(callbackUri);
if (response.Code != null)
{
    // Success - use code to get access token
    string authCode = response.Code;
}
```

#### §4.1.3 Access Token Request
```csharp
var tokenResponse = await flow.ExchangeAuthorizationCodeAsync(
    authorizationCode: authCode,
    redirectUri: "https://your-app.com/callback",  // Must match original
    onSuccess: token => token,
    onError: error => null,
    onFailure: msg => null
);

// tokenResponse contains:
// - access_token: Use to access protected resources
// - token_type: Usually "Bearer"
// - expires_in: Seconds until expiration
// - refresh_token: Optional, for getting new tokens
// - scope: Granted scope (may differ from requested)
```

### Security Considerations (RFC 6749 §10)
- ✅ Authorization code transmitted over secure channel (HTTPS)
- ✅ Code is short-lived and single-use
- ✅ Client authentication at token endpoint
- ✅ Redirection URI validation prevents code interception
- ✅ State parameter prevents CSRF attacks

---

## 2. Implicit Grant (RFC 6749 §4.2)

### Description
The implicit grant is a simplified authorization code flow optimized for clients implemented in a browser using JavaScript. The access token is returned directly from the authorization endpoint without an intermediate authorization code.

### Flow Diagram
```
 +----------+
 | Resource |
 |  Owner   |
 +----------+
      ^
      |
     (B)
 +----|-----+          Client Identifier     +---------------+
 |         -+----(A)-- & Redirection URI --->|               |
 |  User-   |                                | Authorization |
 |  Agent  -|----(B)-- User authenticates -->|     Server    |
 |          |                                |               |
 |         -|<---(C)--- Redirection URI ----<|               |
 |          |          with Access Token     +---------------+
 |          |            in Fragment
 |          |                                +---------------+
 |          |----(D)--- Redirection URI ---->|   Web-Hosted  |
 |          |          without Fragment      |     Client    |
 |          |                                |    Resource   |
 |     (F)  |<---(E)------- Script ---------<|               |
 |          |                                +---------------+
 +-|--------+
   |    |
  (A)  (G) Access Token
   |    |
   ^    v
 +---------+
 |         |
 |  Client |
 +---------+
```

### Implementation

#### §4.2.1 Authorization Request
```csharp
var authRequest = new AuthorizationRequest
{
    ResponseType = "token",             // REQUIRED: "token" for implicit grant
    ClientId = "your-client-id",        // REQUIRED: Client identifier
    RedirectUri = "https://...",        // OPTIONAL: Where to send user after auth
    Scope = "read write",               // OPTIONAL: Space-delimited scopes
    State = "random-csrf-token"         // RECOMMENDED: CSRF protection
};

var authUrl = flow.BuildAuthorizationUrl(authRequest);
// Redirect user to: authUrl
```

#### §4.2.2 Access Token Response
User is redirected back to your redirect_uri with token in fragment:
```
https://your-app.com/callback#access_token=TOKEN&token_type=Bearer&expires_in=3600&state=random-csrf-token
```

Parse the response (JavaScript in browser):
```javascript
// Extract token from URL fragment
const params = new URLSearchParams(window.location.hash.substring(1));
const accessToken = params.get('access_token');
const tokenType = params.get('token_type');
const expiresIn = params.get('expires_in');
const state = params.get('state');

// Verify state matches original request (CSRF protection)
if (state === originalState) {
    // Use accessToken to call APIs
}
```

### Security Considerations (RFC 6749 §10.3, §10.16)
- ⚠️ Access token exposed in URL fragment (visible to browser, history, referrer)
- ⚠️ No refresh token issued (user must re-authorize when token expires)
- ⚠️ Cannot authenticate client (public client)
- ⚠️ Vulnerable to access token leakage
- ✅ Use state parameter for CSRF protection
- ✅ Use short-lived tokens (minimize exposure window)
- ⚠️ Consider using Authorization Code flow with PKCE instead (RFC 7636)

**Recommendation:** Implicit grant is considered legacy. For browser-based apps, use **Authorization Code flow with PKCE** (RFC 7636) instead.

---

## 3. Resource Owner Password Credentials Grant (RFC 6749 §4.3)

### Description
The resource owner password credentials grant type allows the client to request an access token using the resource owner's username and password directly. This grant type should only be used when there is a high degree of trust between the resource owner and the client.

### Flow Diagram
```
 +----------+
 | Resource |
 |  Owner   |
 +----------+
      v
      |    Resource Owner
     (A) Password Credentials
      |
      v
 +---------+                                  +---------------+
 |         |>--(B)---- Resource Owner ------->|               |
 |         |         Password Credentials     | Authorization |
 | Client  |                                  |     Server    |
 |         |<--(C)---- Access Token ---------<|               |
 |         |    (w/ Optional Refresh Token)   |               |
 +---------+                                  +---------------+
```

### Implementation

#### §4.3.2 Access Token Request
```csharp
var tokenResponse = await flow.ExchangePasswordCredentialsAsync(
    username: "user@example.com",
    password: "user-password",
    scope: "read write",               // OPTIONAL: Requested scope
    onSuccess: token => token,
    onError: error => null,
    onFailure: msg => null
);

// tokenResponse contains:
// - access_token: Use to access protected resources
// - token_type: Usually "Bearer"
// - expires_in: Seconds until expiration
// - refresh_token: Optional, for getting new tokens
// - scope: Granted scope
```

### When to Use
✅ **Appropriate when:**
- The client is the device operating system or highly privileged application
- The resource owner has a trust relationship with the client
- Other authorization grant types are not available
- Migrating from HTTP Basic/Digest authentication to OAuth

❌ **NOT appropriate when:**
- The client is a third-party application
- Authorization Code or Implicit flows are viable
- The client cannot keep credentials confidential

### Security Considerations (RFC 6749 §10.7)
- ⚠️ **High Risk**: Maintains the password anti-pattern OAuth seeks to avoid
- ⚠️ Client has access to user credentials (potential for abuse)
- ⚠️ Credentials could be disclosed via logs or other records
- ⚠️ Client can obtain broader scope than intended by resource owner
- ✅ Client MUST discard credentials once access token is obtained
- ✅ Authorization server MUST protect endpoint against brute force attacks
- ✅ Use with long-lived access/refresh tokens to minimize credential exposure

**Recommendation:** Minimize use of this grant type. Use Authorization Code flow whenever possible.

### Example Request
```http
POST /token HTTP/1.1
Host: server.example.com
Content-Type: application/x-www-form-urlencoded

grant_type=password
&username=johndoe
&password=A3ddj3w
&scope=read write
&client_id=your-client-id
&client_secret=your-client-secret
```

---

## 4. Client Credentials Grant (RFC 6749 §4.4)

### Description
The client credentials grant type allows a confidential client to request an access token using only its client credentials. This is used when the client is requesting access to protected resources under its control, or to protected resources previously arranged with the authorization server.

### Flow Diagram
```
 +---------+                                  +---------------+
 |         |                                  |               |
 |         |>--(A)- Client Authentication --->| Authorization |
 | Client  |                                  |     Server    |
 |         |<--(B)---- Access Token ---------<|               |
 |         |                                  |               |
 +---------+                                  +---------------+
```

### Implementation

#### §4.4.2 Access Token Request
```csharp
var tokenResponse = await flow.RequestClientCredentialsTokenAsync(
    scope: "service:read service:write",  // OPTIONAL: Requested scope
    onSuccess: token => token,
    onError: error => null,
    onFailure: msg => null
);

// tokenResponse contains:
// - access_token: Use to access protected resources
// - token_type: Usually "Bearer"
// - expires_in: Seconds until expiration
// - scope: Granted scope
// NOTE: refresh_token SHOULD NOT be included per RFC 6749 §4.4.3
```

### When to Use
✅ **Appropriate when:**
- Client is acting on its own behalf (client is also the resource owner)
- Accessing protected resources under the client's control
- Machine-to-machine authentication
- Microservice-to-microservice communication
- Background jobs/scheduled tasks
- Server-to-server API calls

❌ **NOT appropriate when:**
- Acting on behalf of a user/resource owner
- Need user context or permissions
- Client is a public client (cannot keep secret confidential)

### Requirements (RFC 6749 §4.4)
- ✅ **MUST** only be used by confidential clients
- ✅ **MUST** authenticate with authorization server
- ✅ Client credentials (client_id + client_secret) required
- ✅ Refresh token SHOULD NOT be included in response

### Security Considerations (RFC 6749 §10.1)
- ✅ Client secret MUST be kept confidential
- ✅ Use strong client authentication (consider certificate-based auth)
- ✅ Rotate client credentials periodically
- ✅ Authorization server should implement rate limiting
- ✅ Use TLS for all token endpoint requests

### Example Request
```http
POST /token HTTP/1.1
Host: server.example.com
Authorization: Basic czZCaGRSa3F0MzpnWDFmQmF0M2JW
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials
&scope=service:read service:write
```

Or with credentials in body:
```http
POST /token HTTP/1.1
Host: server.example.com
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials
&client_id=your-client-id
&client_secret=your-client-secret
&scope=service:read service:write
```

---

## Error Handling (RFC 6749 §5.2)

All grant types use the same error response format:

```csharp
public class TokenErrorResponse
{
    public string Error { get; set; }              // REQUIRED: Error code
    public string ErrorDescription { get; set; }   // OPTIONAL: Human-readable text
    public string ErrorUri { get; set; }           // OPTIONAL: Link to error details
}
```

### Standard Error Codes

| Error Code | Description | When Used |
|------------|-------------|-----------|
| `invalid_request` | Request is missing required parameter, includes invalid value, or is malformed | All grant types |
| `invalid_client` | Client authentication failed (unknown client, no auth included, unsupported method) | All grant types |
| `invalid_grant` | Authorization grant or refresh token is invalid, expired, revoked, or doesn't match redirect URI | §4.1, §4.3 |
| `unauthorized_client` | Client is not authorized to use this grant type | All grant types |
| `unsupported_grant_type` | Grant type is not supported by authorization server | All grant types |
| `invalid_scope` | Requested scope is invalid, unknown, or malformed | All grant types |

### Example Error Response
```json
{
  "error": "invalid_grant",
  "error_description": "The authorization code has expired",
  "error_uri": "https://docs.example.com/oauth/errors#invalid_grant"
}
```

---

## Configuration

### Required Properties

For all grant types:
```csharp
var flow = new ClientCredentialFlow
{
    tokenEndpoint = new Uri("https://auth.example.com/token"),
    clientId = "your-client-id",
    clientSecret = "your-client-secret"  // For confidential clients
};
```

For authorization endpoint flows (§4.1, §4.2):
```csharp
flow.authorizationEndpoint = new Uri("https://auth.example.com/authorize");
flow.redirectUri = new Uri("https://your-app.com/callback");
flow.responseTypes = "code,token";  // Comma-separated
flow.scope = "read write";          // Space-delimited default scope
```

---

## Usage Examples

### Example 1: Authorization Code Flow (Web Application)
```csharp
// Step 1: Build authorization URL
var authRequest = new AuthorizationRequest
{
    ResponseType = "code",
    ClientId = flow.clientId,
    RedirectUri = flow.redirectUri?.ToString(),
    Scope = "read write",
    State = GenerateRandomState()
};

var authUrl = flow.BuildAuthorizationUrl(authRequest);
Response.Redirect(authUrl);

// Step 2: Handle callback
var response = flow.ParseAuthorizationResponse(Request.Url.ToString());
if (response.Code != null)
{
    // Step 3: Exchange code for token
    var tokenResponse = await flow.ExchangeAuthorizationCodeAsync(
        authorizationCode: response.Code,
        redirectUri: flow.redirectUri?.ToString(),
        onSuccess: token => token,
        onError: error => null,
        onFailure: msg => null
    );
    
    if (tokenResponse != null)
    {
        // Store access token securely
        Session["access_token"] = tokenResponse.AccessToken;
        Session["refresh_token"] = tokenResponse.RefreshToken;
    }
}
```

### Example 2: Client Credentials Flow (Service-to-Service)
```csharp
// Background service requesting access to API
var tokenResponse = await flow.RequestClientCredentialsTokenAsync(
    scope: "api:read api:write",
    onSuccess: token => token,
    onError: error => {
        logger.LogError($"Token request failed: {error.Error} - {error.ErrorDescription}");
        return null;
    },
    onFailure: msg => {
        logger.LogError($"Token request error: {msg}");
        return null;
    }
);

if (tokenResponse != null)
{
    // Use token to call API
    httpClient.DefaultRequestHeaders.Authorization = 
        new AuthenticationHeaderValue(tokenResponse.TokenType, tokenResponse.AccessToken);
    
    var response = await httpClient.GetAsync("https://api.example.com/data");
}
```

### Example 3: Password Credentials (Mobile App)
```csharp
// First-party mobile app with trusted relationship
var tokenResponse = await flow.ExchangePasswordCredentialsAsync(
    username: userEnteredEmail,
    password: userEnteredPassword,
    scope: "profile email",
    onSuccess: token => token,
    onError: error => {
        if (error.Error == "invalid_grant")
            ShowMessage("Invalid username or password");
        return null;
    },
    onFailure: msg => null
);

if (tokenResponse != null)
{
    // Store token securely (iOS Keychain, Android Keystore)
    SecureStorage.SetAsync("access_token", tokenResponse.AccessToken);
    
    // Clear password from memory
    userEnteredPassword = null;
}
```

---

## Grant Type Selection Guide

| Scenario | Recommended Grant Type | Reason |
|----------|------------------------|--------|
| Web application with backend | Authorization Code (§4.1) | Most secure, supports refresh tokens, client can be authenticated |
| Single-page app (SPA) | Authorization Code + PKCE (RFC 7636) | More secure than Implicit, prevents code interception |
| Mobile app (native) | Authorization Code + PKCE (RFC 7636) | Secure even for public clients, better UX |
| Desktop application | Authorization Code + PKCE | Same benefits as mobile |
| Microservice-to-microservice | Client Credentials (§4.4) | No user context needed, machine-to-machine |
| Legacy/migration from Basic Auth | Password Credentials (§4.3) | Transitional, migrate to Authorization Code later |
| Highly trusted first-party app | Password Credentials (§4.3) | When Authorization Code not feasible |
| JavaScript widget | ~~Implicit (§4.2)~~ | ❌ Deprecated - Use Authorization Code + PKCE instead |

---

## Security Best Practices

### General (All Grant Types)
- ✅ Always use HTTPS/TLS for all requests
- ✅ Validate all input parameters
- ✅ Implement rate limiting on token endpoint
- ✅ Use short-lived access tokens (recommended: < 1 hour)
- ✅ Log all authentication attempts
- ✅ Monitor for suspicious patterns

### Authorization Code Flow (§4.1)
- ✅ Use PKCE extension (RFC 7636) for public clients
- ✅ Validate state parameter to prevent CSRF
- ✅ Verify redirect_uri matches registered value
- ✅ Make authorization codes single-use and short-lived (max 10 minutes)
- ✅ Bind authorization code to client_id

### Implicit Flow (§4.2)
- ⚠️ **Deprecated** - Use Authorization Code + PKCE instead
- ✅ If must use: Use state parameter for CSRF protection
- ✅ Use short-lived tokens (< 15 minutes)
- ✅ Don't store tokens in localStorage (use sessionStorage or memory)

### Password Credentials (§4.3)
- ✅ Protect token endpoint against brute force (rate limiting, account lockout)
- ✅ Use strong password policies
- ✅ Consider multi-factor authentication
- ✅ Immediately discard credentials after token issuance
- ✅ Minimize scope granted to tokens
- ✅ Implement proper session management

### Client Credentials (§4.4)
- ✅ Only use with confidential clients
- ✅ Rotate client credentials regularly
- ✅ Use certificate-based authentication when possible
- ✅ Restrict scope to minimum required
- ✅ Monitor for credential compromise

---

## Migration Guide

### Migrating from Implicit to Authorization Code + PKCE

**Before (Implicit):**
```javascript
// Client-side only
const authUrl = `https://auth.example.com/authorize?response_type=token&client_id=...`;
window.location = authUrl;
// Token returned in URL fragment
```

**After (Authorization Code + PKCE):**
```javascript
// Generate PKCE values
const codeVerifier = generateRandomString();
const codeChallenge = await sha256(codeVerifier);

// Request authorization code
const authUrl = `https://auth.example.com/authorize?
  response_type=code&
  client_id=...&
  code_challenge=${codeChallenge}&
  code_challenge_method=S256`;

window.location = authUrl;

// Exchange code for token (can be done client-side)
const tokenResponse = await fetch('https://auth.example.com/token', {
  method: 'POST',
  body: new URLSearchParams({
    grant_type: 'authorization_code',
    code: authorizationCode,
    code_verifier: codeVerifier,
    client_id: clientId,
    redirect_uri: redirectUri
  })
});
```

---

## Compliance Checklist

### RFC 6749 Section 4 Requirements

#### ✅ Authorization Code Grant (§4.1)
- [x] Authorization request with all parameters (§4.1.1)
- [x] Authorization response with code and state (§4.1.2)
- [x] Error response with standard error codes (§4.1.2.1)
- [x] Token request with grant_type="authorization_code" (§4.1.3)
- [x] Token response per §5.1 format (§4.1.4)
- [x] Client authentication support (§3.2.1)
- [x] Redirect URI validation (§3.1.2)

#### ✅ Implicit Grant (§4.2)
- [x] Authorization request with response_type="token" (§4.2.1)
- [x] Token response in URI fragment (§4.2.2)
- [x] Error response per §4.2.2.1
- [x] No refresh token issued (per §4.2 requirement)
- [x] State parameter support

#### ✅ Resource Owner Password Credentials (§4.3)
- [x] Token request with grant_type="password" (§4.3.2)
- [x] Username and password parameters (§4.3.2)
- [x] Token response per §5.1 format (§4.3.3)
- [x] Client authentication when applicable (§3.2.1)
- [x] Security warnings in documentation (§10.7)

#### ✅ Client Credentials (§4.4)
- [x] Token request with grant_type="client_credentials" (§4.4.2)
- [x] Client authentication required (§3.2.1)
- [x] Token response per §5.1 format (§4.4.3)
- [x] No refresh token in response (§4.4.3)
- [x] Confidential client requirement enforced

---

## Reference Documentation

### RFC 6749 Links
- [RFC 6749: The OAuth 2.0 Authorization Framework](https://datatracker.ietf.org/doc/html/rfc6749)
- [Section 4: Obtaining Authorization](https://datatracker.ietf.org/doc/html/rfc6749#section-4)
- [Section 4.1: Authorization Code Grant](https://datatracker.ietf.org/doc/html/rfc6749#section-4.1)
- [Section 4.2: Implicit Grant](https://datatracker.ietf.org/doc/html/rfc6749#section-4.2)
- [Section 4.3: Resource Owner Password Credentials](https://datatracker.ietf.org/doc/html/rfc6749#section-4.3)
- [Section 4.4: Client Credentials Grant](https://datatracker.ietf.org/doc/html/rfc6749#section-4.4)
- [Section 10: Security Considerations](https://datatracker.ietf.org/doc/html/rfc6749#section-10)

### Related RFCs
- [RFC 7636: Proof Key for Code Exchange (PKCE)](https://datatracker.ietf.org/doc/html/rfc7636)
- [RFC 6750: Bearer Token Usage](https://datatracker.ietf.org/doc/html/rfc6750)
- [RFC 8252: OAuth 2.0 for Native Apps](https://datatracker.ietf.org/doc/html/rfc8252)

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 2.0 | 2025-10-23 | Added Resource Owner Password Credentials (§4.3) and Client Credentials (§4.4) grant types. Now 100% compliant with RFC 6749 Section 4. |
| 1.0 | 2025-10-23 | Initial implementation with Authorization Code (§4.1) and Implicit (§4.2) grant types. |

---

*This implementation follows RFC 6749 (October 2012) standards for OAuth 2.0 authorization flows.*
