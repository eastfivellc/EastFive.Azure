# RFC 6749 Section 3.1 Authorization Endpoint Implementation

## Overview

`ClientCredentialFlow` has been converted to implement **RFC 6749 Section 3.1: Authorization Endpoint** instead of the previous Client Credentials Flow implementation.

The Authorization Endpoint is used to interact with the resource owner (end-user) and obtain an authorization grant through user authentication and consent.

## RFC 6749 Section 3.1 Requirements

### Core Requirements Implemented

| Requirement | RFC Section | Implementation |
|------------|-------------|----------------|
| **Authorization Endpoint** | §3.1 | `authorizationEndpoint` - URI where authorization requests are sent |
| **Response Type** | §3.1.1 | `responseTypes` - Supports "code" (authorization code) and "token" (implicit grant) |
| **Redirection Endpoint** | §3.1.2 | `redirectUri` - REQUIRED, absolute URI without fragment |
| **TLS Required** | §3.1 | Enforced by infrastructure |
| **HTTP GET Support** | §3.1 | MUST support - implemented via `BuildAuthorizationUrlAsync` |
| **HTTP POST Optional** | §3.1 | MAY support - available via standard POST semantics |
| **Fragment Validation** | §3.1 | Endpoint and redirect URIs MUST NOT contain fragments |

## Resource Schema

### Authorization Endpoint Configuration

```json
{
  "id": "guid",
  "authorization_endpoint": "https://auth.example.com/oauth/authorize",
  "token_endpoint": "https://auth.example.com/oauth/token",
  "client_id": "my-client-id",
  "client_secret": "optional-for-confidential-clients",
  "redirect_uri": "https://myapp.example.com/callback",
  "response_types": "code,token",
  "scope": "read write",
  "name": "My OAuth App",
  "description": "OAuth 2.0 Authorization Configuration",
  "is_active": true,
  "created_at": "2025-01-01T00:00:00Z",
  "updated_at": "2025-01-01T00:00:00Z"
}
```

### New Properties (RFC 6749 §3.1)

| Property | Type | Required | RFC Section | Description |
|----------|------|----------|-------------|-------------|
| `authorization_endpoint` | Uri | Yes | §3.1 | Authorization server's authorization endpoint URL. MUST NOT include fragment. |
| `token_endpoint` | Uri | Conditional | §3.2, §4.1 | Token endpoint URL. REQUIRED for authorization code grant. |
| `client_id` | string | Yes | §2.2 | Client identifier issued during registration. |
| `client_secret` | string | Optional | §2.3 | Client secret for confidential clients. |
| `redirect_uri` | Uri | Yes | §3.1.2 | Registered redirection URI. MUST be absolute, MUST NOT include fragment. |
| `response_types` | string | Yes | §3.1.1 | Comma-separated list: "code", "token" |
| `scope` | string | Optional | §3.3 | Default scope (space-delimited) for authorization requests. |

## Authorization Flows Supported

### 1. Authorization Code Grant (RFC 6749 §4.1)

**response_type**: `code`

**Flow**:
1. Client initiates authorization request via authorization endpoint
2. User authenticates and grants consent
3. Authorization server redirects back with authorization code
4. Client exchanges code for access token at token endpoint

**Example**:
```http
GET /OAuth/Authorization/{id}/authorize?response_type=code&state=xyz123&scope=read
```

Response: Authorization URL to redirect user to
```
https://auth.example.com/oauth/authorize?response_type=code&client_id=my-client-id&redirect_uri=https%3A%2F%2Fmyapp.example.com%2Fcallback&scope=read&state=xyz123
```

### 2. Implicit Grant (RFC 6749 §4.2)

**response_type**: `token`

**Flow**:
1. Client initiates authorization request via authorization endpoint
2. User authenticates and grants consent
3. Authorization server redirects back with access token in fragment

**Example**:
```http
GET /OAuth/Authorization/{id}/authorize?response_type=token&state=abc456&scope=write
```

## API Endpoints

### Resource Management

#### GET /OAuth/Authorization
Get all authorization endpoint configurations.

#### POST /OAuth/Authorization
Create new authorization endpoint configuration.

**Request Body**:
```json
{
  "authorization_endpoint": "https://auth.example.com/oauth/authorize",
  "token_endpoint": "https://auth.example.com/oauth/token",
  "client_id": "my-client-id",
  "client_secret": "optional-secret",
  "redirect_uri": "https://myapp.example.com/callback",
  "response_types": "code,token",
  "scope": "read write",
  "name": "My OAuth App",
  "description": "OAuth 2.0 Authorization Configuration",
  "is_active": true
}
```

**Validation**:
- `authorization_endpoint` MUST NOT include fragment (RFC 6749 §3.1)
- `redirect_uri` MUST NOT include fragment (RFC 6749 §3.1.2)
- `redirect_uri` MUST be absolute URI
- `response_types` MUST be valid: "code" or "token"
- `token_endpoint` REQUIRED if "code" in response_types (RFC 6749 §4.1)

#### PATCH /OAuth/Authorization/{id}
Update existing authorization endpoint configuration.

#### DELETE /OAuth/Authorization/{id}
Delete authorization endpoint configuration.

### Authorization Operations

#### GET /OAuth/Authorization/{id}/authorize
Build authorization request URL per RFC 6749 §4.1.1 or §4.2.1.

**Query Parameters**:
- `response_type` (required): "code" or "token"
- `state` (required): Opaque value for CSRF protection (RFC 6749 §10.12)
- `scope` (optional): Space-delimited scope values
- `redirect_uri` (optional): Override registered redirect URI

**Response**: Authorization URL string

**Example**:
```http
GET /OAuth/Authorization/12345/authorize?response_type=code&state=xyz123&scope=read+write

Response (200 OK):
"https://auth.example.com/oauth/authorize?response_type=code&client_id=my-client-id&redirect_uri=https%3A%2F%2Fmyapp.example.com%2Fcallback&scope=read+write&state=xyz123"
```

#### POST /OAuth/Authorization/{id}/token
Exchange authorization code for access token per RFC 6749 §4.1.3.

**Request Body**:
```json
{
  "code": "SplxlOBeZQQYbYS6WxSbIA",
  "redirect_uri": "https://myapp.example.com/callback"
}
```

**Response**:
```json
{
  "access_token": "2YotnFZFEjr1zCsicMWpAA",
  "token_type": "Bearer",
  "expires_in": 3600,
  "refresh_token": "tGzv3JOkF0XG5Qx2TlKWIA",
  "scope": "read write"
}
```

#### GET /OAuth/Authorization/{id}/callback
Parse authorization callback from redirect URI per RFC 6749 §4.1.2 or §4.2.2.

**Query Parameters**:
- `callback_url` (required): Full callback URL received from authorization server

**Response**: `AuthorizationResponse` object

**Example**:
```http
GET /OAuth/Authorization/12345/callback?callback_url=https%3A%2F%2Fmyapp.example.com%2Fcallback%3Fcode%3DSplxlOBeZQQYbYS6WxSbIA%26state%3Dxyz123

Response (200 OK):
{
  "code": "SplxlOBeZQQYbYS6WxSbIA",
  "state": "xyz123"
}
```

## Validation Methods

### ValidateAuthorizationRequest()
Validates authorization request per RFC 6749 §3.1.

**Checks**:
- `response_type` is REQUIRED (RFC 6749 §3.1.1)
- `response_type` is valid: "code" or "token"
- `response_type` matches configured types
- `client_id` is REQUIRED and matches configuration
- `redirect_uri` is absolute URI if provided
- `redirect_uri` MUST NOT include fragment (RFC 6749 §3.1.2)
- `redirect_uri` matches registered value
- `scope` is well-formed (space-delimited) if provided

### BuildAuthorizationUrl()
Constructs RFC-compliant authorization URL per RFC 6749 §4.1.1 or §4.2.1.

**Parameters**:
- `responseType`: "code" or "token"
- `state`: Opaque value for CSRF protection
- `scope`: Space-delimited scope values
- `redirectUri`: Override registered redirect URI

**Returns**: Properly encoded authorization URL

### ParseAuthorizationResponse()
Parses authorization response from redirect URI per RFC 6749 §4.1.2 or §4.2.2.

**Extracts**:
- Authorization code from query parameters (authorization code grant)
- Access token from fragment (implicit grant)
- Error codes and descriptions
- State parameter

### ExchangeAuthorizationCodeAsync()
Exchanges authorization code for access token per RFC 6749 §4.1.3.

**Request**:
- `grant_type`: "authorization_code"
- `code`: Authorization code
- `redirect_uri`: Must match authorization request if provided
- `client_id`: Client identifier
- `client_secret`: For confidential clients

**Response**: `AccessTokenResponse` or `TokenErrorResponse`

## Response Type Constants

```csharp
public static class ResponseTypeValues
{
    public const string Code = "code";        // Authorization Code Grant (RFC 6749 §4.1)
    public const string Token = "token";      // Implicit Grant (RFC 6749 §4.2)
    
    public static bool IsValid(string responseType);
}
```

## Error Codes

### Authorization Endpoint Errors (RFC 6749 §4.1.2.1, §4.2.2.1)

| Error Code | Description | RFC Section |
|------------|-------------|-------------|
| `invalid_request` | Missing required parameter or malformed request | §4.1.2.1 |
| `unauthorized_client` | Client not authorized for this method | §4.1.2.1 |
| `access_denied` | Resource owner or server denied request | §4.1.2.1 |
| `unsupported_response_type` | Authorization server doesn't support response type | §4.1.2.1 |
| `invalid_scope` | Requested scope is invalid, unknown, or malformed | §4.1.2.1 |
| `server_error` | Unexpected condition prevented fulfilling request | §4.1.2.1 |
| `temporarily_unavailable` | Server temporarily unable to handle request | §4.1.2.1 |

## Security Considerations

### RFC 6749 Section 10 Requirements

1. **TLS Required** (§10.9): All authorization endpoint requests MUST use TLS
2. **CSRF Protection** (§10.12): State parameter SHOULD be used for CSRF protection
3. **Fragment Restriction** (§3.1.2): Redirect URIs MUST NOT include fragments
4. **Redirect URI Validation** (§3.1.2.2): Public clients MUST register redirect URIs
5. **Authorization Code Lifetime** (§10.5): Authorization codes MUST be short-lived (10 minutes max recommended)
6. **Client Authentication** (§3.2.1): Confidential clients MUST authenticate at token endpoint

## Migration Guide

### From Client Credentials Flow to Authorization Endpoint

**Before**:
```json
{
  "client_id": "my-client",
  "client_secret": "secret",
  "token_endpoint": "https://auth.example.com/oauth/token",
  "scope": "api.read"
}
```

**After**:
```json
{
  "authorization_endpoint": "https://auth.example.com/oauth/authorize",
  "token_endpoint": "https://auth.example.com/oauth/token",
  "client_id": "my-client",
  "client_secret": "secret",
  "redirect_uri": "https://myapp.example.com/callback",
  "response_types": "code",
  "scope": "api.read"
}
```

### Key Changes

1. **Route Changed**: `/OAuth/ClientCredential` → `/OAuth/Authorization`
2. **New Required Fields**: `authorization_endpoint`, `redirect_uri`, `response_types`
3. **Flow Changed**: Client Credentials (no user) → Authorization Code/Implicit (user interaction)
4. **New Endpoints**: `/authorize`, `/token`, `/callback` for authorization flow operations

## Example Usage

### Authorization Code Grant Flow

```csharp
// 1. Build authorization URL
GET /OAuth/Authorization/{id}/authorize?response_type=code&state=xyz123

// 2. User is redirected to authorization server, authenticates and grants consent

// 3. Authorization server redirects back
https://myapp.example.com/callback?code=AUTH_CODE&state=xyz123

// 4. Parse callback
GET /OAuth/Authorization/{id}/callback?callback_url=https%3A%2F%2Fmyapp.example.com%2Fcallback%3Fcode%3DAUTH_CODE%26state%3Dxyz123

// 5. Exchange code for token
POST /OAuth/Authorization/{id}/token
{
  "code": "AUTH_CODE",
  "redirect_uri": "https://myapp.example.com/callback"
}

// Response
{
  "access_token": "ACCESS_TOKEN",
  "token_type": "Bearer",
  "expires_in": 3600
}
```

## References

- [RFC 6749: The OAuth 2.0 Authorization Framework](https://datatracker.ietf.org/doc/html/rfc6749)
- [RFC 6749 §3.1: Authorization Endpoint](https://datatracker.ietf.org/doc/html/rfc6749#section-3.1)
- [RFC 6749 §3.1.1: Response Type](https://datatracker.ietf.org/doc/html/rfc6749#section-3.1.1)
- [RFC 6749 §3.1.2: Redirection Endpoint](https://datatracker.ietf.org/doc/html/rfc6749#section-3.1.2)
- [RFC 6749 §4.1: Authorization Code Grant](https://datatracker.ietf.org/doc/html/rfc6749#section-4.1)
- [RFC 6749 §4.2: Implicit Grant](https://datatracker.ietf.org/doc/html/rfc6749#section-4.2)
- [RFC 6749 §10: Security Considerations](https://datatracker.ietf.org/doc/html/rfc6749#section-10)
