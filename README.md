# eBay Account Deletion Webhook — Azure Functions (.NET 8)

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![Azure Functions](https://img.shields.io/badge/Azure%20Functions-v4%20isolated-0062AD)](https://learn.microsoft.com/azure/azure-functions/dotnet-isolated-process-guide)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

Production-ready endpoints in C# / .NET 8 on **Azure Functions** (isolated worker) for two mandatory eBay integration requirements:

| Function | Route | Purpose |
|----------|-------|---------|
| `MarketplaceAccountDeletion` | `/api/ebay/account-deletion` | Handles eBay's mandatory **Marketplace Account Deletion** webhook — required to unlock the production keyset. |
| `PlatformNotifications` | `/api/ebay/notifications` | Receives eBay **Platform Notifications** (e.g. `MARKETPLACE_ACCOUNT_DELETION`, and any topic you register). |

Both expose the same two-part contract eBay expects on a single HTTPS URL:

- **`GET ?challenge_code=…`** → returns `SHA-256(challengeCode + verificationToken + endpointUrl)` as JSON `{ "challengeResponse": "<hash>" }`.
- **`POST`** → validates and processes the signed notification, responding `200` immediately and doing the heavy work asynchronously.

> The concatenation order for the hash is **mandatory**: `challengeCode + verificationToken + endpointUrl`. A wrong order, a different URL than the one saved in the portal, or a non-`application/json` response will fail eBay's validation.

---

## Quick start

### 1 — Clone

```bash
git clone https://github.com/Capponi-Ivan-Org/ebay-account-deletion-webhook.git
cd "ebay-account-deletion-webhook/EbayAccountDeletionWebhook"
```

### 2 — Configure

Copy the example settings and fill in your values:

```bash
cp local.settings.json.example local.settings.json
```

```jsonc
{
  "Values": {
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "EBAY_VERIFICATION_TOKEN": "<32-80 chars: letters, numbers, - and _>",
    "EBAY_ENDPOINT_URL": "https://<app>.azurewebsites.net/api/ebay/account-deletion",
    "EBAY_PLATFORM_ENDPOINT_URL": "https://<app>.azurewebsites.net/api/ebay/notifications"
  }
}
```

### 3 — Run locally

```bash
func start
```

### 4 — Deploy to Azure

```bash
az functionapp create \
  --name <app-name> --resource-group <rg> \
  --consumption-plan-location westeurope \
  --runtime dotnet-isolated --runtime-version 8 \
  --functions-version 4 --storage-account <storage>

func azure functionapp publish <app-name>
```

Set the same `EBAY_*` values under **Function App → Configuration → Application settings** (or via a Key Vault reference).

### 5 — Validate in the eBay Developer Portal

1. Open **Application Keys → Marketplace account deletion/closure**.
2. Enter the endpoint URL (`https://<app>.azurewebsites.net/api/ebay/account-deletion`).
3. Enter the same `EBAY_VERIFICATION_TOKEN`.
4. Click **Save** — eBay fires the challenge `GET`. If the hash matches, the endpoint is verified and your production keyset is unlocked.

---

## Configuration

| Variable | Required | Used by | Description |
|----------|----------|---------|-------------|
| `EBAY_VERIFICATION_TOKEN` | yes | both | Token you also enter in the eBay portal (32–80 chars). |
| `EBAY_ENDPOINT_URL` | yes | account-deletion | Exact public HTTPS URL of the account-deletion function (used in the hash). |
| `EBAY_PLATFORM_ENDPOINT_URL` | yes | notifications | Exact public HTTPS URL of the notifications function (used in the hash). |
| `EBAY_CLIENT_ID` | optional | signature | App (client) ID — enables `x-ebay-signature` verification (see below). |
| `EBAY_CLIENT_SECRET` | optional | signature | App (client) secret — enables `x-ebay-signature` verification. |
| `EBAY_API_BASE_URL` | optional | signature | eBay API base, defaults to `https://api.ebay.com` (use `https://api.sandbox.ebay.com` for sandbox). |

Values are read from environment variables / App Settings — **never hardcode them**. `local.settings.json` is git-ignored.

---

## Adding your deletion logic

Both functions log the incoming user and leave a `TODO` where you delete or anonymise the user's data. In `MarketplaceAccountDeletionFunction.ProcessAccountDeletionAsync`:

```csharp
// e.g. with Entity Framework Core:
await _db.Users
    .Where(u => u.EbayUserId == userId)
    .ExecuteDeleteAsync();
```

Keep it **idempotent** — eBay may deliver the same notification more than once.

---

## Project layout

```
EbayAccountDeletionWebhook/
├─ MarketplaceAccountDeletionFunction.cs   # /api/ebay/account-deletion
├─ PlatformNotificationsFunction.cs        # /api/ebay/notifications
├─ Models/EbayNotification.cs              # payload + challenge response DTOs
├─ Program.cs                              # isolated worker bootstrap
└─ host.json
```

## Signature verification

Both `POST` handlers can verify the `x-ebay-signature` header before trusting the payload:

1. The header is Base64-encoded JSON (`alg`, `kid`, `signature`, `digest`).
2. eBay's public key is fetched from the Notification API (`getPublicKey`) using an app OAuth token obtained via client-credentials, then cached (~1h for the key, until expiry for the token).
3. The signature (DER-encoded) is verified with **ECDSA / SHA-1** over the raw request body.

This is **opt-in**: set `EBAY_CLIENT_ID` and `EBAY_CLIENT_SECRET` to enable it. When they are set, a notification whose signature does not validate is rejected with **HTTP 412 Precondition Failed** (eBay will retry). When they are absent, verification is skipped (a warning is logged) and the endpoint behaves as before — so the challenge flow keeps working without OAuth credentials.

## Related guides

Step-by-step write-ups (EN / IT):

- [eBay developer account in production: the deletion webhook](https://ivancapponi.com/en/guides/ebay-developer-account-production-deletion-webhook)
- [eBay Marketplace Account Deletion on Azure Functions](https://ivancapponi.com/en/guides/ebay-account-deletion-azure-functions)

## License

[MIT](LICENSE) © Ivan Capponi
