# AxPeg

> **High-Performance Workflow & Process Execution Engine**  
> Modern, modular, interface-driven .NET 8.0 Web API migrated from legacy Delphi/Pascal workflow engines.

[![Build & Test](https://img.shields.io/badge/build-passing-brightgreen.svg)]()
[![Framework](https://img.shields.io/badge/.NET-8.0-blue.svg)]()
[![Architecture](https://img.shields.io/badge/Architecture-Clean%20%2F%20SOLID-orange.svg)]()

AxPeg provides business process workflow state evaluation, task sequence generation, action approvals, parent-subtask tree tracking, dynamic parameter resolution, and integration with Redis caching and RabbitMQ messaging queues.

> [!NOTE]
> This service replaces legacy Delphi modules (`uStoredata 2.pas`, `uAxPEG 1.pas`, `uAxPEGActions 1.pas`, `uASBPegRestObj.pas`) with a fully asynchronous, thread-safe, and testable C# Web API.

---

## Features

- **Workflow Evaluation & Validation**: State validation, task limit checks, index skipping, and multi-branch condition checks.
- **Task Action Operations**: Asynchronous task approval (`Approve`), rejection (`Reject`), forward delegation (`Forward`), and return tracking (`Return`).
- **Dynamic Database Isolation**: Multi-tenant database connection management via `IAxExtend`.
- **Event Messaging & Caching**: Distributed notification publishing via RabbitMQ (STOMP client) and key-value state caching via Redis.
- **Serilog Diagnostics**: Split event logging for runtime telemetry (`info-.txt`) and exception monitoring (`error-.txt`).
- **Robust Exception Handling**: Custom middleware ensuring standardized `ServiceResponse<T>` JSON error payloads without stack trace leaks.

---

## Architecture & Project Structure

The project follows clean architectural boundaries separating Controllers, Services, Repositories, and Infrastructure abstractions:

```text
AxPeg/
├── Controllers/
│   └── SBPegRestController.cs          # HTTP API endpoints (/api/v1/SBPegRest)
├── Dtos/
│   ├── Request/                        # Payload contracts (InitiateRequest, ActionRequest)
│   └── Response/                       # Standard response contract (ServiceResponse<T>)
├── Services/
│   ├── Interfaces/                     # Service abstractions (IAxPegService, IAxPegActionsService, etc.)
│   ├── AxPegService.cs                 # Process state evaluator & subtask tree validator
│   ├── AxPegActionsService.cs          # Task lifecycle executor (Approve, Reject, Forward, Return)
│   └── SBPegRestService.cs             # Legacy payload transformation & XML/JSON serializers
├── Repositories/
│   ├── Interfaces/                     # Data access abstractions
│   └── StoreDataRepository.cs          # Database operations, sequence generation, change history
├── Lib/
│   ├── Interfaces/                     # Infrastructure service abstractions
│   ├── RedisCacheService.cs            # Redis cache integration
│   ├── RabbitMQPublisher.cs            # RabbitMQ notification publisher
│   └── EmailService.cs                 # SMTP email notification dispatch
├── Exceptions/
│   ├── CustomExceptions.cs             # Domain exception types
│   └── GlobalExceptionMiddleware.cs    # Global exception handler middleware
├── Program.cs                          # Application bootstrapper & DI setup
└── appsettings.json                    # Application configuration & Serilog rules
```

### Module Mapping

| Legacy Delphi Module | Modern .NET Component | Primary Responsibility |
| :--- | :--- | :--- |
| `uStoredata 2.pas` | `StoreDataRepository.cs` | Transactions, sequence generators, history recording, site sync |
| `uAxPEG 1.pas` | `AxPegService.cs` | Task limits, process evaluation, subtask trees, parameter parsing |
| `uAxPEGActions 1.pas` | `AxPegActionsService.cs` | Task lifecycle actions (Approve, Reject, Forward, Return) |
| `uASBPegRestObj.pas` | `SBPegRestService.cs` | Legacy payload transformations, XML/JSON conversion |

---

## API Reference

All endpoints are versioned under `/api/v1/SBPegRest` and return standardized `ServiceResponse<T>` payloads.

### Endpoints Overview

| Method | Endpoint | Description |
| :--- | :--- | :--- |
| `POST` | `/api/v1/SBPegRest/CanInitiate` | Evaluates if a workflow task can be initiated |
| `POST` | `/api/v1/SBPegRest/Approve` | Approves an active task |
| `POST` | `/api/v1/SBPegRest/Reject` | Rejects an active task |
| `POST` | `/api/v1/SBPegRest/Forward` | Forwards a task to another user |
| `POST` | `/api/v1/SBPegRest/Return` | Returns a task to the prior step |
| `GET`  | `/api/v1/SBPegRest/IsV2Process` | Checks if a process definition is PEGV2 |

### Request & Response Examples

#### Approve Task (`POST /api/v1/SBPegRest/Approve`)

**Request Body:**
```json
{
  "appName": "ProductionApp",
  "taskId": "TASK-100234",
  "userName": "john.doe",
  "comments": "Approved after document verification"
}
```

**Response Body:**
```json
{
  "success": true,
  "message": "Task approved successfully.",
  "data": true
}
```

> [!TIP]
> Swagger UI is enabled in development mode for interactive API exploration at `http://localhost:5012/swagger/index.html`.

---

## Getting Started

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Local or remote SQL Database (managed via `AxExtend.dll` binaries in `../AxExtend/`)
- Redis instance (optional for caching)
- RabbitMQ server (optional for notification publishing)

### Installation & Build

1. Navigate to the project directory:
   ```bash
   cd AxPeg
   ```

2. Restore dependencies:
   ```bash
   dotnet restore
   ```

3. Build the solution:
   ```bash
   dotnet build
   ```

### Running Locally

To launch the ASP.NET Core web host locally:

```bash
dotnet run --project AxPeg.csproj
```

The API will start listening on configured HTTP/HTTPS ports (default: `http://localhost:5012`).

---

## Running Tests

Automated unit and service tests are located in `AxPeg.Tests`.

Run all tests:

```bash
dotnet test ..\AxPeg.Tests\AxPeg.Tests.csproj
```

> [!IMPORTANT]
> Ensure all build artifacts are closed before running tests to prevent file lock issues on Windows environments.
