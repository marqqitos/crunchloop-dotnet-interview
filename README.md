# dotnet-interview / TodoApi

[![Open in Coder](https://dev.crunchloop.io/open-in-coder.svg)](https://dev.crunchloop.io/templates/fly-containers/workspace?param.Git%20Repository=git@github.com:crunchloop/dotnet-interview.git)

This is a simple Todo List API built in .NET 8. This project is currently being used for .NET full-stack candidates.

## Prerequisites

Before you begin, ensure you have the following installed on your system:

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (latest version)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Quick Start with Docker. Use VS Code Dev Container Extension
1. Install the "Dev Containers" extension in VS Code
2. Open the command palette (Ctrl+Shift+P / Cmd+Shift+P)
3. Select "Dev Containers: Reopen in Container"
4. This will open the project inside the container with full .NET tooling
5. You can then run all .NET commands directly in the integrated terminal

### Alternative Approach. Start the Database Services with Docker Compose

The project includes a Docker Compose file that sets up two SQL Server instances - one for the main TodoApi and one for the ExternalTodoApi.

```bash
docker-compose -f .devcontainer/docker-compose.yml up -d sqlserver external-sqlserver
```

This will start:
- **Main SQL Server** on port 1433 (for TodoApi)
- **External SQL Server** on port 1434 (for ExternalTodoApi)

**Note**: Wait a few moments for both databases to fully start up before proceeding.

### 2. Build the Solution

```bash
dotnet build
```

### 3. Run Database Migrations

Apply the database migrations to set up the required tables:

```bash
# For TodoApi (connects to sqlserver:1433)
dotnet ef database update --project TodoApi

# For ExternalTodoApi (connects to external-sqlserver:1434)
dotnet ef database update --project ExternalTodoApi
```

### 4. Run the APIs

You'll need to run both APIs in separate terminal windows:

#### Terminal 1 - ExternalTodoApi
```bash
dotnet run --project ExternalTodoApi
```

The ExternalTodoApi will run on `https://localhost:8080`.

#### Terminal 2 - TodoApi
```bash
dotnet run --project TodoApi
```

The TodoApi will run on `https://localhost:5083`.

## Synchronization

The TodoApi includes a background synchronization service that automatically syncs data with the ExternalTodoApi. Here's how to execute synchronization:

### Automatic Synchronization
The synchronization runs automatically in the background every 30 seconds when TodoAPI is running (this is configurable in appsettings.Development.json).

You can monitor the sync process through:
1. **Logs**: Check the console output for synchronization activity
2. **Database**: Monitor the sync tracking fields in the database

### Manual Synchronization
To manually trigger synchronization, you can use the sync endpoints:

```bash
# Trigger sync from Local to External
curl -X POST https://localhost:5083/api/sync/todolists

# Trigger sync from External to Local
curl -X POST https://localhost:7000/api/sync/todolists/inbound

# Trigger sync from External to Local
curl -X POST https://localhost:7000/api/sync/todolists/bidirectional
```

### Synchronization Features
- **Conflict Resolution**: The system handles conflicts between local and external data
- **Retry Logic**: Failed sync operations are retried with exponential backoff
- **State Tracking**: Sync status is tracked in the database
- **Background Processing**: Continuous synchronization runs in the background

## Troubleshooting

### Common Issues

1. **Database Connection Failed**
   - Ensure Docker is running
   - Wait for both SQL Server instances to fully start (check logs: `docker logs sqlserver` and `docker logs external-sqlserver`)
   - Verify the connection strings in `appsettings.Development.json`:
     - TodoApi connects to `sqlserver:1433`
     - ExternalTodoApi connects to `external-sqlserver:1434`

2. **Port Already in Use**
   - Check if ports 1433, 1434, 8080, or 5083 are already occupied
   - Stop conflicting services or change ports in `Properties/launchSettings.json`

3. **Migration Errors**
   - Ensure both databases are running and accessible
   - Check that the connection strings are correct
   - Try dropping and recreating the databases if needed

4. **Build Errors**
	- Clean the project
	- Delete bin/ and obj/ folders and rebuild the projects

## Test

To run tests:

`dotnet test`

Check integration tests at: (https://github.com/crunchloop/interview-tests)

## Contact

- Martín Fernández (mfernandez@crunchloop.io)

## About Crunchloop

![crunchloop](https://crunchloop.io/logo-blue.png)

We strongly believe in giving back :rocket:. Let's work together [`Get in touch`](https://crunchloop.io/contact).
