# AxPeg .NET 8.0 Web API

A modular, interface-driven .NET 8.0 Web API migrated from legacy Delphi/Pascal workflow engines (`uASBPegRestObj.pas`, `uAxPEG.pas`, `uAxPEGActions.pas`, `uStoredata.pas`).

---

## 1. System Architecture
The application follows a strictly decoupled, interface-driven architecture to separate database interactions, business workflow state validation, external helper libraries, and API routes.

```text
AxPeg/
├── Controllers/
│   └── SBPegRestController.cs          # HTTP API routes mapping Delphi REST operations
├── Dtos/
│   ├── Request/                        # API request payload contracts
│   └── Response/                       # API response wrappers
├── Services/
│   ├── Interfaces/                     # Extensible engine contracts
│   │   ├── IAxPegService.cs
│   │   └── IAxPegActionsService.cs
│   ├── AxPegService.cs                 # Core workflow evaluator logic
│   └── AxPegActionsService.cs          # Actions executor (Approve, Reject, Forward, Return)
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

## 2. API Reference

All requests must include a valid request body matching the schemas below.

### 2.1 Can Initiate PEG Task
Determines whether a new workflow task sequence can be initiated.
* **Endpoint:** `POST /SBPegRest/CanInitiate`
* **Request Schema:**
  ```json
  {
    "appName": "MyProjectSchema",
    "processName": "LeaveApproval",
    "taskName": "InitialSubmission",
    "indexNo": "10",
    "keyValue": "LV-2026-001"
  }
  ```
* **Success Response (200 OK):**
  ```json
  {
    "success": true,
    "message": "Can initiate PEG task.",
    "data": true
  }
  ```

### 2.2 Approve Task
Approves an active task step.
* **Endpoint:** `POST /SBPegRest/Approve`
* **Request Schema:**
  ```json
  {
    "appName": "MyProjectSchema",
    "taskId": "7f8g9h10...",
    "userName": "john_manager",
    "comments": "Approved after reviewing details."
  }
  ```
* **Success Response (200 OK):**
  ```json
  {
    "success": true,
    "message": "Task approved successfully.",
    "data": true
  }
  ```

### 2.3 Reject Task
Rejects an active task step.
* **Endpoint:** `POST /SBPegRest/Reject`
* **Request Schema:**
  ```json
  {
    "appName": "MyProjectSchema",
    "taskId": "7f8g9h10...",
    "userName": "john_manager",
    "comments": "Rejected due to missing docs."
  }
  ```

### 2.4 Forward Task
Forwards an active task step to another actor or user.
* **Endpoint:** `POST /SBPegRest/Forward`
* **Request Schema:**
  ```json
  {
    "appName": "MyProjectSchema",
    "taskId": "7f8g9h10...",
    "userName": "john_manager",
    "forwardToUser": "mary_director",
    "comments": "Forwarding for final director sign-off."
  }
  ```

### 2.5 Return Task
Returns a task step back to the prior initiator.
* **Endpoint:** `POST /SBPegRest/Return`
* **Request Schema:**
  ```json
  {
    "appName": "MyProjectSchema",
    "taskId": "7f8g9h10...",
    "userName": "john_manager",
    "comments": "Please update attachments."
  }
  ```

### 2.6 Is V2 Process
Checks if a given process uses the PEGV2 engine version.
* **Endpoint:** `GET /SBPegRest/IsV2Process`
* **Parameters:** `appName` (string), `processName` (string)
* **Response (200 OK):**
  ```json
  {
    "success": true,
    "message": "Checked process version. V2: true",
    "data": true
  }
  ```

---

## 3. Configuration & Integrations

### 3.1 Serilog Logging Split
Logging is configured via `appsettings.json` and initialized globally.
* General informational and warning events are logged to rolling daily files under `logs/info-.txt`.
* Error, critical, and fatal events are isolated into rolling daily error logs under `logs/error-.txt`.

### 3.2 Database & Redis Caching
The application leverages the custom `AxExtend` binary library:
* **Database Queries:** Injecting `IAxExtend` allows opening connections for the correct database schema via `OpenDBConnectionAsync(appName)` and performing raw queries with `ExecuteSQLAsync` and `ExecuteNonQueryAsync`.
* **Redis Caching:** Key-value operations are wrapped in `RedisCacheService` which calls `OpenRedisConnectionAsync(appName)` and reads/writes keys with expirations using the `AxExtend` Redis handler.

### 3.3 Global Exception Handling
A generic `GlobalExceptionMiddleware` captures all unhandled issues:
* Logged immediately into the Serilog error file.
* Formats a clean JSON response payload preventing leakage of internal execution stack traces in production settings.

---

## 4. Setup & Running Locally

### Prerequisites
* .NET 8.0 SDK installed.
* `AxExtend` DLL dependencies placed in the relative `..\AxExtend\` directory.

### Running the App
1. Restore dependencies:
   ```bash
   dotnet restore
   ```
2. Build the project:
   ```bash
   dotnet build
   ```
3. Run the API:
   ```bash
   dotnet run
   ```
4. Access Swagger UI for testing in Development mode:
   ```text
   http://localhost:<port>/swagger/index.html
   ```
