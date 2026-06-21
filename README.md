# eBay Marketplace — Azure Functions (.NET 8)

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
git clone https://github.com/Capponi-Ivan-Org/eBay-Marketplace.git
cd "eBay-Marketplace/eBay Marketplace"
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

| Variable | Used by | Description |
|----------|---------|-------------|
| `EBAY_VERIFICATION_TOKEN` | both | Token you also enter in the eBay portal (32–80 chars). |
| `EBAY_ENDPOINT_URL` | account-deletion | Exact public HTTPS URL of the account-deletion function (used in the hash). |
| `EBAY_PLATFORM_ENDPOINT_URL` | notifications | Exact public HTTPS URL of the notifications function (used in the hash). |

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
eBay Marketplace/
├─ MarketplaceAccountDeletionFunction.cs   # /api/ebay/account-deletion
├─ PlatformNotificationsFunction.cs        # /api/ebay/notifications
├─ Models/EbayNotification.cs              # payload + challenge response DTOs
├─ Program.cs                              # isolated worker bootstrap
└─ host.json
```

## Roadmap / notes

- The `POST` path currently trusts the payload after parsing. For defence in depth you can additionally verify the `x-ebay-signature` header against eBay's public key (Notification API `getPublicKey`) before processing.

## Related guides

Step-by-step write-ups (EN / IT):

- [eBay developer account in production: the deletion webhook](https://ivancapponi.com/en/guides/ebay-developer-account-production-deletion-webhook)
- [eBay Marketplace Account Deletion on Azure Functions](https://ivancapponi.com/en/guides/ebay-account-deletion-azure-functions)

## License

[MIT](LICENSE) © Ivan Capponi
