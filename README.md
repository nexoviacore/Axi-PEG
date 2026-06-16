# AxPeg .NET 8.0 Web API

A modular, interface-driven .NET 8.0 Web API fully migrated from all 5 legacy Delphi/Pascal workflow and data persistence files.

---

## 1. Project Conversion Inventory
The legacy Delphi modules are fully mapped and converted into their respective .NET namespaces as follows:

| Source Delphi File | Target C# Class / File | Status | Description |
| :--- | :--- | :--- | :--- |
| **uStoredata 2.pas** | [StoreDataRepository.cs](file:///D:/dotnet/PegDotnetConversion/AxPeg/Repositories/StoreDataRepository.cs) | **100% Converted** | Transactions, Sequence generators, History recording, and Site sync. |
| **uAxPEG 1.pas** | [AxPegService.cs](file:///D:/dotnet/PegDotnetConversion/AxPeg/Services/AxPegService.cs) | **100% Converted** | Task limits checks, Workflow validators, Subtask trees, and Parameter parsing. |
| **uAxPEGActions 1.pas** | [AxPegActionsService.cs](file:///D:/dotnet/PegDotnetConversion/AxPeg/Services/AxPegActionsService.cs) | **100% Converted** | Task operations (Approve, Reject, etc.), status syncing, and amendments. |
| **uAxPEGActions_utf8.pas** | [AxPegActionsService.cs](file:///D:/dotnet/PegDotnetConversion/AxPeg/Services/AxPegActionsService.cs) | **100% Converted** | Handled along with the main action class translation. |
| **uASBPegRestObj.pas** | [SBPegRestService.cs](file:///D:/dotnet/PegDotnetConversion/AxPeg/Services/SBPegRestService.cs) | **100% Converted** | XML/JSON serializers, validation string sanitizers, and session connections. |

---

## 2. System Architecture

```text
AxPeg/
├── Controllers/
│   └── SBPegRestController.cs          # HTTP API routes mapping Delphi REST entries
├── Dtos/
│   ├── Request/                        # API request payload contracts
│   └── Response/                       # API response wrappers
├── Services/
│   ├── Interfaces/                     # Extensible engine contracts
│   │   ├── IAxPegService.cs
│   │   ├── IAxPegActionsService.cs
│   │   └── ISBPegRestService.cs
│   ├── AxPegService.cs                 # Core workflow evaluator logic
│   ├── AxPegActionsService.cs          # Actions executor (Approve, Reject, Forward, Return)
│   └── SBPegRestService.cs             # Helper serializers and connection states
├── Repositories/
│   ├── Interfaces/
│   │   └── IStoreDataRepository.cs
│   └── StoreDataRepository.cs          # Database storage mapping & Dapper wrappers
├── Lib/
│   ├── Interfaces/
│   │   ├── IRabbitMQPublisher.cs
│   │   ├── IEmailService.cs
│   │   └── IRedisCacheService.cs
│   ├── RedisCacheService.cs            # Custom key-value Redis caching
│   ├── RabbitMQPublisher.cs            # Event messaging queue integration
│   └── EmailService.cs                 # SMTP mail delivery configuration
├── Exceptions/
│   ├── CustomExceptions.cs             # Application-specific exceptions
│   └── GlobalExceptionMiddleware.cs    # Caught exceptions JSON serializer middleware
├── Program.cs                          # App bootstrapper & Dependency Injection
└── appsettings.json                    # Configuration & Serilog logging
```

---

## 3. Core API Services & Actions

All HTTP entry endpoints are exposed via [SBPegRestController.cs](file:///D:/dotnet/PegDotnetConversion/AxPeg/Controllers/SBPegRestController.cs).

### 3.1 REST Utilities Service (`ISBPegRestService`)
Exposes helper methods to handle legacy string formats, JSON-to-XML documents conversion, and database session bindings.
* `Task<string> LoadXMLDataFromWSAsync(string xml)`: Validates and parses structural XML nodes.
* `Task<string> ConvertJSONToXMLDocAsync(string json)`: Dynamically converts JSON structures into XML representation.
* `Task<string> ConnectToProjectAsync(string db)`: Calls the `IAxExtend` module database pool context.
* `Task<string> CloseProjectAsync()`: Safely terminates the database connection scope.
* `string MakeValidJsonString(string input)`: Sanitizes string delimiters to escape JSON formatting rules.

---

## 4. Run & Run Local Host

### Prerequisites
* .NET 8.0 SDK.
* `AxExtend` dependency DLLs placed in the sibling directory `..\AxExtend\`.

### Commands
1. **Restore:**
   ```bash
   dotnet restore
   ```
2. **Build:**
   ```bash
   dotnet build
   ```
3. **Run Web Host:**
   ```bash
   dotnet run
   ```
4. **Interactive testing (Swagger UI):**
   [http://localhost:5012/swagger/index.html](http://localhost:5012/swagger/index.html)
