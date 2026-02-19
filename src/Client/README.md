# MERSEL.Services.GibUserList.Client

.NET HTTP client SDK for the MERSEL GIB User List API. Query Turkish tax authority (GIB) e-Invoice and e-Despatch taxpayer lists with built-in HMAC-SHA256 authentication.

## Installation

```bash
dotnet add package MERSEL.Services.GibUserList.Client
```

## Quick Start

### Using `IConfiguration` binding

```json
// appsettings.json
{
  "GibUserListClient": {
    "BaseUrl": "https://gib-userlist.example.com",
    "AccessKey": "your-access-key",
    "SecretKey": "your-secret-key"
  }
}
```

```csharp
builder.Services.AddGibUserListClient(builder.Configuration);
```

### Using inline options

```csharp
builder.Services.AddGibUserListClient(options =>
{
    options.BaseUrl = "https://gib-userlist.example.com";
    options.AccessKey = "your-access-key";
    options.SecretKey = "your-secret-key";
    options.Timeout = TimeSpan.FromSeconds(30);
});
```

### Inject and use

```csharp
public class MyService(IGibUserListClient client)
{
    public async Task CheckTaxPayer(string vkn)
    {
        var response = await client.GetEInvoiceGibUserAsync(vkn);
        
        // response.Data      -> GibUserResponse (taxpayer info)
        // response.LastSyncAt -> last server sync timestamp
    }
}
```

## Features

### Taxpayer Lookup

| Method | Description |
|--------|-------------|
| `GetEInvoiceGibUserAsync(identifier)` | Lookup e-Invoice taxpayer by VKN/TCKN |
| `GetEDespatchGibUserAsync(identifier)` | Lookup e-Despatch taxpayer by VKN/TCKN |
| `SearchEInvoiceGibUsersAsync(query)` | Search e-Invoice taxpayers by title |
| `SearchEDespatchGibUsersAsync(query)` | Search e-Despatch taxpayers by title |
| `BatchGetEInvoiceGibUsersAsync(identifiers)` | Batch lookup up to 100 e-Invoice taxpayers |
| `BatchGetEDespatchGibUsersAsync(identifiers)` | Batch lookup up to 100 e-Despatch taxpayers |

### Change Tracking (Delta Sync)

```csharp
var changes = await client.GetEInvoiceChangesAsync(since: lastSyncDate);

foreach (var change in changes.Data.Items)
{
    // change.ChangeType -> Added, Removed
    // change.Identifier -> VKN/TCKN
}
```

### Archive (Full List Bootstrap)

```csharp
// Download the latest full taxpayer list
await using var stream = (await client.GetLatestEInvoiceArchiveAsync()).Data;

// Or list available archive files
var archives = await client.ListEInvoiceArchivesAsync();
```

### Sync Status

```csharp
var status = await client.GetSyncStatusAsync();
// status.LastSyncAt, status.EInvoiceCount, status.EDespatchCount
```

## Consumer Protocol

1. **Initial bootstrap**: Download the full list via `GetLatestEInvoiceArchiveAsync()`
2. **Delta tracking**: Poll changes via `GetEInvoiceChangesAsync(since)` periodically
3. **410 Gone**: If the retention window has expired, re-bootstrap from step 1

## Authentication

HMAC-SHA256 signing is automatically enabled when both `AccessKey` and `SecretKey` are provided. Requests are signed with `Authorization`, `X-Timestamp`, and `X-Nonce` headers. No additional configuration required.

## Target Frameworks

- .NET 8.0
- .NET 9.0

## License

MIT - see [LICENSE](https://github.com/mersel-os/gib-user-list/blob/main/LICENSE)
